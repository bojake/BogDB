using BogDb.Core.Storage.Compression;
using Xunit;

namespace BogDb.Tests.Storage;

public class BitpackingUtilsTests
{
    [Fact]
    public unsafe void PackUnpack_UInt64BitWidth64_PreservesValues()
    {
        var values = new ulong[]
        {
            0UL,
            1UL,
            0xF0F0_F0F0_F0F0_F0F0UL,
            ulong.MaxValue
        };

        var buffer = new byte[128];
        fixed (byte* ptr = buffer)
        {
            for (var i = 0; i < values.Length; i++)
                BitpackingUtils.PackSingle(values[i], ptr, bitWidth: 64, dstOffset: i);

            for (var i = 0; i < values.Length; i++)
            {
                BitpackingUtils.UnpackSingle(ptr, out ulong unpacked, bitWidth: 64, srcOffset: i);
                Assert.Equal(values[i], unpacked);
            }
        }
    }

    [Fact]
    public unsafe void PackUnpack_UInt128BitWidth96_PreservesValues()
    {
        var values = new UInt128[]
        {
            (UInt128.One << 95) | 1234u,
            (UInt128.One << 80) | 0xDEADBEEFu,
            ((UInt128)0x1234_5678_9ABC_DEF0UL << 16) | 0x55AAu
        };

        var buffer = new byte[256];
        fixed (byte* ptr = buffer)
        {
            for (var i = 0; i < values.Length; i++)
                BitpackingUtils.PackSingle(values[i], ptr, bitWidth: 96, dstOffset: i);

            for (var i = 0; i < values.Length; i++)
            {
                BitpackingUtils.UnpackSingle(ptr, out UInt128 unpacked, bitWidth: 96, srcOffset: i);
                Assert.Equal(values[i], unpacked);
            }
        }
    }

    [Fact]
    public unsafe void PackUnpack_UInt128BitWidth100_WithBitShift_PreservesValues()
    {
        var values = new UInt128[]
        {
            (UInt128.One << 99) | 0xA55Au,
            (UInt128.One << 75) | 0x1234_5678u
        };

        var buffer = new byte[256];
        fixed (byte* ptr = buffer)
        {
            for (var i = 0; i < values.Length; i++)
                BitpackingUtils.PackSingle(values[i], ptr, bitWidth: 100, dstOffset: i + 1);

            for (var i = 0; i < values.Length; i++)
            {
                BitpackingUtils.UnpackSingle(ptr, out UInt128 unpacked, bitWidth: 100, srcOffset: i + 1);
                Assert.Equal(values[i], unpacked);
            }
        }
    }
}
