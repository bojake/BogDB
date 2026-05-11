using BogDb.Core.Common;
using System.Runtime.InteropServices;
using Xunit;

namespace BogDb.Tests.Common;

public class VectorAndMaskTests
{
    [Fact]
    public void NullMask_ShouldAlignTo64BitBoundaries()
    {
        var mask = new NullMask(100); // Needs two 64-bit entries
        Assert.Equal(2, mask.Data.Length);
        
        mask.SetNull(65, true);
        Assert.True(mask.IsNull(65));
        Assert.False(mask.IsNull(64));
        Assert.True(mask.MayContainNulls);
    }

    [Fact]
    public void ValueVector_ShouldWriteAndReadZeroAlloc()
    {
        using var vector = new ValueVector(LogicalTypeID.INT64, 10);
        
        // Write utilizing refs
        vector.SetValue<long>(0, 100);
        vector.SetValue<long>(1, 255);
        vector.SetValue<long>(5, 42);

        // Read utilizing refs
        Assert.Equal(100, vector.GetValue<long>(0));
        Assert.Equal(255, vector.GetValue<long>(1));
        Assert.Equal(42, vector.GetValue<long>(5));

        // Ensure struct updates in place
        ref long valRef = ref vector.GetValue<long>(1);
        valRef = 999;
        
        Assert.Equal(999, vector.GetValue<long>(1));
    }

    [Fact]
    public void ValueVector_NullMaskIntegration()
    {
        using var vector = new ValueVector(LogicalTypeID.DOUBLE, 5);
        
        vector.SetValue<double>(0, 1.1);
        vector.SetNull(1, true); // Null at index 1
        vector.SetValue<double>(2, 3.3);
        
        Assert.False(vector.IsNull(0));
        Assert.True(vector.IsNull(1));
        Assert.False(vector.IsNull(2));
    }
}
