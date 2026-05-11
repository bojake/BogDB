using System.Runtime.InteropServices;
using BogDb.Core.Common;
using Xunit;

namespace BogDb.Tests.Common;

public class TypeSizeAndParityTests
{
    [Fact]
    public unsafe void LogicalTypeID_ShouldBeOneByte()
    {
        Assert.Equal(1, sizeof(LogicalTypeID));
    }

    [Fact]
    public unsafe void PhysicalTypeID_ShouldBeOneByte()
    {
        Assert.Equal(1, sizeof(PhysicalTypeID));
    }

    [Fact]
    public void InternalID_ShouldBeSixteenBytesAndPacked()
    {
        // 8 bytes for offset + 8 bytes for tableID = 16 bytes.
        Assert.Equal(16, Marshal.SizeOf<InternalID>());
    }

    [Fact]
    public void ListEntry_ShouldBeTwelveBytesAndPacked()
    {
        // 8 bytes for offset + 4 bytes for size = 12 bytes.
        // C++ doesn't pad list_entry_t to 16 bytes by default unless aligned in a specific struct.
        Assert.Equal(12, Marshal.SizeOf<ListEntry>());
    }

    [Fact]
    public void EnumValues_ShouldMatchCppExactly()
    {
        Assert.Equal(23, (byte)LogicalTypeID.INT64);
        Assert.Equal(50, (byte)LogicalTypeID.STRING);
        Assert.Equal(52, (byte)LogicalTypeID.LIST);
        Assert.Equal(54, (byte)LogicalTypeID.STRUCT);

        Assert.Equal(20, (byte)PhysicalTypeID.STRING);
    }

    [Fact]
    public void Constants_ShouldMatchCppExactly()
    {
        Assert.Equal(ulong.MaxValue, BogDbConstants.INVALID_OFFSET);
        Assert.Equal(uint.MaxValue, BogDbConstants.INVALID_COLUMN_ID);
        Assert.Equal(uint.MaxValue - 1, BogDbConstants.ROW_IDX_COLUMN_ID);
        Assert.Equal(ulong.MaxValue, BogDbConstants.INVALID_TABLE_ID);
    }
}
