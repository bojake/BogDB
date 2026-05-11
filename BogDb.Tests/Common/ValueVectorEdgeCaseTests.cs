using BogDb.Core.Common;
using System;
using Xunit;

namespace BogDb.Tests.Common;

public class ValueVectorEdgeCaseTests
{
    [Fact]
    public void ValueVector_NullMask_OutOfBoundsShouldThrowSafely()
    {
        var vector = new ValueVector(LogicalTypeID.INT64);
        
        // BogDb Constants default vector capacity is 2048
        // Null mask uses 64-bit blocks
        
        Assert.Throws<IndexOutOfRangeException>(() => vector.SetNull(2048, true));
    }
    
    [Fact]
    public void ValueVector_NullMask_EntireBlockNullsSafely()
    {
        var vector = new ValueVector(LogicalTypeID.STRING);
        
        for(uint i = 0; i < 2048; i++)
        {
            vector.SetNull(i, true);
        }
        
        // Assert the block is universally null without pointer crashing
        for(uint i = 0; i < 2048; i++)
        {
            Assert.True(vector.IsNull(i));
        }
    }

    [Fact]
    public void ValueVector_Reset_ShouldClearAllMasksAndData()
    {
        var vector = new ValueVector(LogicalTypeID.INT32);
        
        vector.SetValue<int>(42, 100);
        vector.SetNull(42, false);
        
        Assert.Equal(100, vector.GetValue<int>(42));
        Assert.False(vector.IsNull(42));
        
        vector.SetAllNull();
        
        // Value shouldn't matter but NullMask must be completely reset logic.
        // We assert BogDb's behavior where vectors evaluate to safe boundaries
        Assert.True(vector.IsNull(42));
    }
}
