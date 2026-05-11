using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace BogDb.Core.Storage.Compression
{
    public static class BitpackingUtils
    {
        private const int SizeOfCompressedTypeBits = 32;

        public static unsafe void UnpackSingle<T>(byte* srcCursor, out T dst, ushort bitWidth, int srcOffset) 
            where T : unmanaged
        {
            if (bitWidth == 0)
            {
                dst = default;
                return;
            }

            int srcBufferOffsetBytes = srcOffset * bitWidth / SizeOfCompressedTypeBits * sizeof(uint);
            int shiftRight = srcOffset * bitWidth % SizeOfCompressedTypeBits;

            uint* castedSrcCursor = (uint*)(srcCursor + srcBufferOffsetBytes);

            var val = ReadBitsAsUInt128(castedSrcCursor, bitWidth, shiftRight);
            dst = UInt128ToValue<T>(val);
        }

        public static unsafe void PackSingle<T>(T src, byte* dstBuffer, ushort bitWidth, int dstOffset) 
            where T : unmanaged
        {
            if (bitWidth == 0)
                return;

            int dstBufferOffsetBytes = dstOffset * bitWidth / SizeOfCompressedTypeBits * sizeof(uint);
            int shiftLeft = dstOffset * bitWidth % SizeOfCompressedTypeBits;

            uint* castedDstBuffer = (uint*)(dstBuffer + dstBufferOffsetBytes);
            var mask = LowBitsMaskUInt128(bitWidth);
            var inVal = ValueToUInt128(src) & mask;
            WriteBitsFromUInt128(castedDstBuffer, inVal, bitWidth, shiftLeft);
        }

        private static UInt128 LowBitsMaskUInt128(int bits)
        {
            if (bits <= 0)
                return UInt128.Zero;
            if (bits >= 128)
                return UInt128.MaxValue;
            return (UInt128.One << bits) - UInt128.One;
        }

        private static uint LowBitsMaskUInt32(int bits)
        {
            if (bits <= 0)
                return 0u;
            if (bits >= 32)
                return uint.MaxValue;
            return (1u << bits) - 1u;
        }

        private static unsafe UInt128 ReadBitsAsUInt128(uint* words, int bitWidth, int shiftRight)
        {
            UInt128 result = UInt128.Zero;
            var bitsRead = 0;
            var wordIndex = 0;
            var bitOffset = shiftRight;

            while (bitsRead < bitWidth)
            {
                var available = SizeOfCompressedTypeBits - bitOffset;
                var take = Math.Min(bitWidth - bitsRead, available);
                var chunk = (words[wordIndex] >> bitOffset) & LowBitsMaskUInt32(take);
                result |= (UInt128)chunk << bitsRead;
                bitsRead += take;
                wordIndex++;
                bitOffset = 0;
            }

            return result & LowBitsMaskUInt128(bitWidth);
        }

        private static unsafe void WriteBitsFromUInt128(uint* words, UInt128 value, int bitWidth, int shiftLeft)
        {
            var bitsWritten = 0;
            var wordIndex = 0;
            var bitOffset = shiftLeft;

            while (bitsWritten < bitWidth)
            {
                var available = SizeOfCompressedTypeBits - bitOffset;
                var take = Math.Min(bitWidth - bitsWritten, available);
                var chunkMask = LowBitsMaskUInt32(take);
                var chunk = (uint)((value >> bitsWritten) & chunkMask);
                var maskInWord = chunkMask << bitOffset;
                words[wordIndex] = (words[wordIndex] & ~maskInWord) | (chunk << bitOffset);
                bitsWritten += take;
                wordIndex++;
                bitOffset = 0;
            }
        }

        private static unsafe UInt128 ValueToUInt128<T>(T value) where T : unmanaged
        {
            UInt128 result = UInt128.Zero;
            var size = Unsafe.SizeOf<T>();
            if (size > 16)
                throw new NotSupportedException($"Bitpacking value size {size} exceeds 128 bits.");

            var src = (byte*)&value;
            for (var i = 0; i < size; i++)
                result |= (UInt128)src[i] << (8 * i);
            return result;
        }

        private static unsafe T UInt128ToValue<T>(UInt128 value) where T : unmanaged
        {
            T result = default;
            var size = Unsafe.SizeOf<T>();
            if (size > 16)
                throw new NotSupportedException($"Bitpacking value size {size} exceeds 128 bits.");

            var dst = (byte*)&result;
            for (var i = 0; i < size; i++)
                dst[i] = (byte)(value >> (8 * i));
            return result;
        }
    }
}
