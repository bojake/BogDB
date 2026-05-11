using BogDb.Core.Storage.Compression;
using Xunit;

namespace BogDb.Tests.Storage;

public class FloatCompressionTests
{
    [Fact]
    public unsafe void FloatCompression_FloatRoundTrip_PreservesBitPatterns()
    {
        var src = new[] { 0.0f, -0.0f, 1.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        var compressed = new byte[src.Length * sizeof(uint)];
        var decompressed = new byte[src.Length * sizeof(float)];
        var metadata = new CompressionMetadata(0f, 0f, CompressionType.ALP);

        fixed (float* srcVals = src)
        fixed (byte* dst = compressed)
        fixed (byte* outBuf = decompressed)
        {
            byte* srcPtr = (byte*)srcVals;
            ulong exceptionCount = 0;
            var exceptions = new EncodeExceptionView<float> { Bytes = null };
            var codec = new FloatCompression<float>();

            var bytesWritten = codec.CompressNextPageWithExceptions(
                ref srcPtr, srcOffset: 0, numValuesRemaining: (ulong)src.Length,
                dst, (ulong)compressed.Length, exceptions, 0, ref exceptionCount, metadata);

            Assert.Equal((ulong)compressed.Length, bytesWritten);
            codec.DecompressFromPage(dst, srcOffset: 0, outBuf, dstOffset: 0, (ulong)src.Length, metadata);

            var outVals = (float*)outBuf;
            for (var i = 0; i < src.Length; i++)
            {
                Assert.Equal(BitConverter.SingleToInt32Bits(src[i]), BitConverter.SingleToInt32Bits(outVals[i]));
            }
        }
    }

    [Fact]
    public unsafe void FloatCompression_DoubleRoundTrip_RespectsOffsets()
    {
        var src = new[] { 99.0, 1.25, -4.5, 777.0 };
        var compressed = new byte[2 * sizeof(ulong)];
        var decompressed = new byte[4 * sizeof(double)];
        var metadata = new CompressionMetadata(0d, 0d, CompressionType.ALP);

        fixed (double* srcVals = src)
        fixed (byte* dst = compressed)
        fixed (byte* outBuf = decompressed)
        {
            byte* srcPtr = (byte*)srcVals;
            var srcBase = srcPtr;
            ulong exceptionCount = 0;
            var exceptions = new EncodeExceptionView<double> { Bytes = null };
            var codec = new FloatCompression<double>();

            var bytesWritten = codec.CompressNextPageWithExceptions(
                ref srcPtr, srcOffset: 1, numValuesRemaining: 2,
                dst, (ulong)compressed.Length, exceptions, 0, ref exceptionCount, metadata);

            Assert.Equal((ulong)compressed.Length, bytesWritten);
            Assert.Equal((nint)(srcBase + 3 * sizeof(double)), (nint)srcPtr);

            codec.DecompressFromPage(dst, srcOffset: 0, outBuf, dstOffset: 1, numValues: 2, metadata);
            var outVals = (double*)outBuf;
            Assert.Equal(1.25, outVals[1]);
            Assert.Equal(-4.5, outVals[2]);
        }
    }

    [Fact]
    public void FloatCompression_NumValues_UsesEncodedIntegerWidths()
    {
        var metadata = new CompressionMetadata(0f, 0f, CompressionType.ALP);
        Assert.Equal(4ul, FloatCompression<float>.NumValues(16, metadata));
        Assert.Equal(2ul, FloatCompression<double>.NumValues(16, metadata));
    }
}
