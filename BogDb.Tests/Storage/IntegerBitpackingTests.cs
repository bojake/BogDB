using BogDb.Core.Common;
using BogDb.Core.Storage.Compression;
using Xunit;

namespace BogDb.Tests.Storage;

public class IntegerBitpackingTests
{
    [Fact]
    public unsafe void IntegerBitpacking_Int_RoundTrip_WithNegativeRange()
    {
        var src = new[] { -10, -3, 0, 4, 10 };
        var metadata = new CompressionMetadata(min: -10, max: 10, compression: CompressionType.INTEGER_BITPACKING);
        var compressed = new byte[64];
        var decompressed = new int[src.Length];

        fixed (int* srcVals = src)
        fixed (byte* dst = compressed)
        fixed (int* outVals = decompressed)
        {
            byte* srcPtr = (byte*)srcVals;
            var bytesWritten = IntegerBitpacking.CompressNextPage<int>(
                ref srcPtr, (ulong)src.Length, dst, (ulong)compressed.Length, metadata);
            Assert.True(bytesWritten > 0);

            IntegerBitpacking.DecompressFromPage<int>(
                dst, srcOffset: 0, (byte*)outVals, dstOffset: 0, (ulong)src.Length, metadata);
        }

        Assert.Equal(src, decompressed);
    }

    [Fact]
    public unsafe void IntegerBitpacking_ULong_RoundTrip_WithLargeOffsetRange()
    {
        var src = new[] { 1_000_000_000_000UL, 1_000_000_000_003UL, 1_000_000_000_007UL };
        var metadata = new CompressionMetadata(
            min: 1_000_000_000_000UL,
            max: 1_000_000_000_007UL,
            compression: CompressionType.INTEGER_BITPACKING);
        var compressed = new byte[64];
        var decompressed = new ulong[src.Length];

        fixed (ulong* srcVals = src)
        fixed (byte* dst = compressed)
        fixed (ulong* outVals = decompressed)
        {
            byte* srcPtr = (byte*)srcVals;
            var bytesWritten = IntegerBitpacking.CompressNextPage<ulong>(
                ref srcPtr, (ulong)src.Length, dst, (ulong)compressed.Length, metadata);
            Assert.True(bytesWritten > 0);

            IntegerBitpacking.DecompressFromPage<ulong>(
                dst, srcOffset: 0, (byte*)outVals, dstOffset: 0, (ulong)src.Length, metadata);
        }

        Assert.Equal(src, decompressed);
    }

    [Fact]
    public void IntegerBitpacking_CanUpdateInPlace_RejectsOutOfRangeNonNullValues()
    {
        var metadata = new CompressionMetadata(min: 0, max: 10, compression: CompressionType.INTEGER_BITPACKING);
        var values = new[] { 1, 11, 2 };
        var nullMask = new NullMask((ulong)values.Length);
        nullMask.SetAllNonNull();

        var canUpdate = IntegerBitpacking.CanUpdateInPlace<int>(values, metadata, nullMask, 0);
        Assert.False(canUpdate);

        nullMask.SetNull(1, true);
        canUpdate = IntegerBitpacking.CanUpdateInPlace<int>(values, metadata, nullMask, 0);
        Assert.True(canUpdate);
    }
}
