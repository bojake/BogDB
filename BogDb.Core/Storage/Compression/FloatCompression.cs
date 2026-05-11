using System;
using System.Runtime.CompilerServices;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Compression
{
    public class FloatCompression<T> where T : unmanaged
    {
        public FloatCompression()
        {
            if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
            {
                throw new NotSupportedException("FloatCompression only supports float and double.");
            }
        }

        private static PhysicalTypeID GetBitpackingPhysicalType()
        {
            if (typeof(T) == typeof(float))
                return PhysicalTypeID.INT32;
            return PhysicalTypeID.INT64;
        }

        public unsafe ulong CompressNextPageWithExceptions(
            ref byte* srcBuffer,
            ulong srcOffset,
            ulong numValuesRemaining,
            byte* dstBuffer,
            ulong dstBufferSize,
            EncodeExceptionView<T> exceptionBuffer,
            ulong exceptionBufferSize,
            ref ulong exceptionCount,
            CompressionMetadata metadata)
        {
            var encodedTypeSize = (ulong)(typeof(T) == typeof(float) ? sizeof(uint) : sizeof(ulong));
            var maxByBuffer = dstBufferSize / encodedTypeSize;
            var numValuesToCompress = (int)Math.Min(numValuesRemaining, maxByBuffer);
            var srcStart = srcBuffer + (srcOffset * (ulong)Unsafe.SizeOf<T>());

            for (var posInPage = 0; posInPage < numValuesToCompress; ++posInPage)
            {
                var floatValue = Unsafe.Read<T>(srcStart + (posInPage * Unsafe.SizeOf<T>()));
                if (typeof(T) == typeof(float))
                {
                    var encoded = BitConverter.SingleToUInt32Bits((float)(object)floatValue);
                    Unsafe.Write(dstBuffer + (posInPage * sizeof(uint)), encoded);
                }
                else
                {
                    var encoded = (ulong)BitConverter.DoubleToInt64Bits((double)(object)floatValue);
                    Unsafe.Write(dstBuffer + (posInPage * sizeof(ulong)), encoded);
                }
            }

            srcBuffer = srcStart + ((ulong)numValuesToCompress * (ulong)Unsafe.SizeOf<T>());

            return (ulong)numValuesToCompress * encodedTypeSize;
        }

        public static ulong NumValues(ulong dataSize, CompressionMetadata metadata)
        {
            return Uncompressed.NumValues(dataSize, GetBitpackingPhysicalType()); 
        }

        public unsafe void DecompressFromPage(
            byte* srcBuffer,
            ulong srcOffset,
            byte* dstBuffer,
            ulong dstOffset,
            ulong numValues,
            CompressionMetadata metadata)
        {
            for (ulong i = 0; i < numValues; ++i)
            {
                T decodedValue;
                if (typeof(T) == typeof(float))
                {
                    var packedInteger = Unsafe.Read<uint>(srcBuffer + ((srcOffset + i) * sizeof(uint)));
                    decodedValue = (T)(object)BitConverter.UInt32BitsToSingle(packedInteger);
                }
                else
                {
                    var packedInteger = Unsafe.Read<ulong>(srcBuffer + ((srcOffset + i) * sizeof(ulong)));
                    decodedValue = (T)(object)BitConverter.Int64BitsToDouble((long)packedInteger);
                }

                Unsafe.Write(dstBuffer + (dstOffset + i) * (ulong)Unsafe.SizeOf<T>(), decodedValue);
            }
        }
    }

    public struct EncodeException<T> where T : unmanaged
    {
        public T Value;
        public uint PosInChunk;
        
        public static unsafe int SizeInBytes() => sizeof(EncodeException<T>);
    }

    public unsafe struct EncodeExceptionView<T> where T : unmanaged
    {
        public byte* Bytes;

        public EncodeException<T> GetValue(uint elementOffset)
        {
            byte* elementAddress = Bytes + elementOffset * EncodeException<T>.SizeInBytes();
            return Unsafe.Read<EncodeException<T>>(elementAddress);
        }

        public void SetValue(EncodeException<T> exception, ulong elementOffset)
        {
            byte* elementAddress = Bytes + elementOffset * (ulong)EncodeException<T>.SizeInBytes();
            Unsafe.Write(elementAddress, exception);
        }
    }
}
