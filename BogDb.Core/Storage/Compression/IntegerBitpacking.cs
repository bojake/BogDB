using System;
using System.Runtime.CompilerServices;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Compression
{
    public struct BitpackInfo<T> where T : unmanaged
    {
        public byte BitWidth;
        public bool HasNegative;
        public T Offset;
    }

    public static class IntegerBitpacking
    {
        public static BitpackInfo<T> GetPackingInfo<T>(CompressionMetadata metadata) where T : unmanaged
        {
            if (!IsSupportedIntegerType(typeof(T)))
                throw new NotSupportedException($"IntegerBitpacking does not support type {typeof(T).Name}.");

            if (IsSignedIntegerType(typeof(T)))
            {
                var min = Convert.ToInt64(metadata.Min);
                var max = Convert.ToInt64(metadata.Max);
                if (max < min)
                    throw new ArgumentException($"Invalid metadata range: min={min}, max={max}.");

                var range = (ulong)(max - min);
                return new BitpackInfo<T>
                {
                    BitWidth = (byte)GetBitWidth(range),
                    HasNegative = min < 0,
                    Offset = SignedToT<T>(min)
                };
            }

            var uMin = Convert.ToUInt64(metadata.Min);
            var uMax = Convert.ToUInt64(metadata.Max);
            if (uMax < uMin)
                throw new ArgumentException($"Invalid metadata range: min={uMin}, max={uMax}.");

            return new BitpackInfo<T>
            {
                BitWidth = (byte)GetBitWidth(uMax - uMin),
                HasNegative = false,
                Offset = UnsignedToT<T>(uMin)
            };
        }
        
        private static int GetBitWidth(uint value)
        {
            if (value == 0) return 0;
            return 32 - System.Numerics.BitOperations.LeadingZeroCount(value);
        }

        private static int GetBitWidth(ulong value)
        {
            if (value == 0) return 0;
            return 64 - System.Numerics.BitOperations.LeadingZeroCount(value);
        }

        public static bool CanUpdateInPlace<T>(Span<T> values, CompressionMetadata metadata, NullMask nullMask, ulong nullMaskOffset) where T : unmanaged
        {
            if (!IsSupportedIntegerType(typeof(T)))
                return false;

            if (IsSignedIntegerType(typeof(T)))
            {
                var min = Convert.ToInt64(metadata.Min);
                var max = Convert.ToInt64(metadata.Max);
                for (var i = 0; i < values.Length; i++)
                {
                    if (nullMask.IsNull((uint)(nullMaskOffset + (ulong)i)))
                        continue;
                    var v = TToSigned(values[i]);
                    if (v < min || v > max)
                        return false;
                }
                return true;
            }

            var uMin = Convert.ToUInt64(metadata.Min);
            var uMax = Convert.ToUInt64(metadata.Max);
            for (var i = 0; i < values.Length; i++)
            {
                if (nullMask.IsNull((uint)(nullMaskOffset + (ulong)i)))
                    continue;
                var v = TToUnsigned(values[i]);
                if (v < uMin || v > uMax)
                    return false;
            }
            return true;
        }
        
        public static ulong NumValues<T>(ulong pageSize, CompressionMetadata metadata) where T : unmanaged
        {
            var info = GetPackingInfo<T>(metadata);
            if (info.BitWidth == 0)
            {
                return ulong.MaxValue;
            }
            return (pageSize * 8) / info.BitWidth;
        }

        public static unsafe ulong CompressNextPage<T>(ref byte* srcBuffer, ulong numValuesRemaining, byte* dstBuffer, ulong dstBufferSize, CompressionMetadata metadata) where T : unmanaged
        {
            if (metadata.Compression == CompressionType.UNCOMPRESSED)
            {
                // Uncompressed.CompressNextPage...
                return 0;
            }

            var info = GetPackingInfo<T>(metadata);
            if (info.BitWidth == 0)
            {
                srcBuffer += numValuesRemaining * (ulong)Unsafe.SizeOf<T>();
                return 0;
            }

            ulong numValuesToCompress = Math.Min(numValuesRemaining, NumValues<T>(dstBufferSize, metadata));
            ulong sizeToCompress = (numValuesToCompress * info.BitWidth / 8) + ((numValuesToCompress * info.BitWidth % 8) != 0 ? 1ul : 0ul);

            for (ulong i = 0; i < numValuesToCompress; i++)
            {
                var val = Unsafe.Read<T>(srcBuffer + (i * (ulong)Unsafe.SizeOf<T>()));
                var encoded = EncodeValueAsUInt64(val, info);
                BitpackingUtils.PackSingle(encoded, dstBuffer, info.BitWidth, (int)i);
            }

            srcBuffer += numValuesToCompress * (ulong)Unsafe.SizeOf<T>();
            return sizeToCompress;
        }

        public static unsafe void DecompressFromPage<T>(byte* srcBuffer, ulong srcOffset, byte* dstBuffer, ulong dstOffset, ulong numValues, CompressionMetadata metadata) where T : unmanaged
        {
            var info = GetPackingInfo<T>(metadata);

            for (ulong i = 0; i < numValues; ++i)
            {
                if (info.BitWidth == 0)
                {
                    Unsafe.Write(dstBuffer + ((dstOffset + i) * (ulong)Unsafe.SizeOf<T>()), info.Offset);
                    continue;
                }

                BitpackingUtils.UnpackSingle(srcBuffer, out ulong encoded, info.BitWidth, (int)(srcOffset + i));
                var val = DecodeValueFromUInt64<T>(encoded, info);
                Unsafe.Write(dstBuffer + ((dstOffset + i) * (ulong)Unsafe.SizeOf<T>()), val);
            }
        }

        private static bool IsSupportedIntegerType(Type t) =>
            t == typeof(sbyte) || t == typeof(byte) ||
            t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) ||
            t == typeof(long) || t == typeof(ulong);

        private static bool IsSignedIntegerType(Type t) =>
            t == typeof(sbyte) || t == typeof(short) || t == typeof(int) || t == typeof(long);

        private static ulong EncodeValueAsUInt64<T>(T value, BitpackInfo<T> info) where T : unmanaged
        {
            if (IsSignedIntegerType(typeof(T)))
            {
                var v = TToSigned(value);
                var offset = TToSigned(info.Offset);
                return (ulong)(v - offset);
            }
            return TToUnsigned(value) - TToUnsigned(info.Offset);
        }

        private static T DecodeValueFromUInt64<T>(ulong encoded, BitpackInfo<T> info) where T : unmanaged
        {
            if (IsSignedIntegerType(typeof(T)))
            {
                var offset = TToSigned(info.Offset);
                return SignedToT<T>(offset + (long)encoded);
            }
            return UnsignedToT<T>(TToUnsigned(info.Offset) + encoded);
        }

        private static long TToSigned<T>(T value) where T : unmanaged
        {
            if (typeof(T) == typeof(sbyte)) return (sbyte)(object)value;
            if (typeof(T) == typeof(short)) return (short)(object)value;
            if (typeof(T) == typeof(int)) return (int)(object)value;
            if (typeof(T) == typeof(long)) return (long)(object)value;
            throw new NotSupportedException($"Type {typeof(T).Name} is not a supported signed integer type.");
        }

        private static ulong TToUnsigned<T>(T value) where T : unmanaged
        {
            if (typeof(T) == typeof(byte)) return (byte)(object)value;
            if (typeof(T) == typeof(ushort)) return (ushort)(object)value;
            if (typeof(T) == typeof(uint)) return (uint)(object)value;
            if (typeof(T) == typeof(ulong)) return (ulong)(object)value;
            throw new NotSupportedException($"Type {typeof(T).Name} is not a supported unsigned integer type.");
        }

        private static T SignedToT<T>(long value) where T : unmanaged
        {
            if (typeof(T) == typeof(sbyte)) return (T)(object)(sbyte)value;
            if (typeof(T) == typeof(short)) return (T)(object)(short)value;
            if (typeof(T) == typeof(int)) return (T)(object)(int)value;
            if (typeof(T) == typeof(long)) return (T)(object)value;
            throw new NotSupportedException($"Type {typeof(T).Name} is not a supported signed integer type.");
        }

        private static T UnsignedToT<T>(ulong value) where T : unmanaged
        {
            if (typeof(T) == typeof(byte)) return (T)(object)(byte)value;
            if (typeof(T) == typeof(ushort)) return (T)(object)(ushort)value;
            if (typeof(T) == typeof(uint)) return (T)(object)(uint)value;
            if (typeof(T) == typeof(ulong)) return (T)(object)value;
            throw new NotSupportedException($"Type {typeof(T).Name} is not a supported unsigned integer type.");
        }
    }
}
