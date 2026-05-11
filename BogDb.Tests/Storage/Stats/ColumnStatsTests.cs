using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.ExpressionEvaluator;
using BogDb.Core.Storage.Stats;
using Xunit;

namespace BogDb.Tests.Storage.Stats;

public class ColumnStatsTests
{
    [Fact]
    public void ColumnStats_Int32DistinctCount_TracksValueHashes()
    {
        var stats = new ColumnStats(PhysicalTypeID.INT32);
        using var vector = new ValueVector(LogicalTypeID.INT32, capacity: 8);
        vector.SetValue(0, 10);
        vector.SetValue(1, 10);
        vector.SetValue(2, 20);
        vector.SetValue(3, 30);
        vector.State.GetSelVector().SetSelSize(4);

        stats.Update(vector);
        var distinct = stats.GetNumDistinctValues();

        Assert.InRange(distinct, 2UL, 4UL);
    }

    [Fact]
    public void ColumnStats_StringDistinctCount_HashesByContent()
    {
        var stats = new ColumnStats(PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        StringFunctionEvaluator.SetKuString(vector, 0, "alpha");
        StringFunctionEvaluator.SetKuString(vector, 1, "alpha");
        StringFunctionEvaluator.SetKuString(vector, 2, "beta");
        vector.State.GetSelVector().SetSelSize(3);

        stats.Update(vector);
        var distinct = stats.GetNumDistinctValues();

        Assert.InRange(distinct, 2UL, 3UL);
    }

    [Fact]
    public void ColumnStats_NestedTypes_DoNotTrackDistinct()
    {
        var stats = new ColumnStats(PhysicalTypeID.LIST);
        using var vector = new ValueVector(LogicalTypeID.LIST, capacity: 8);
        vector.SetAuxValue(0, new List<object?> { 1L, 2L });
        vector.SetNull(0, false);
        vector.State.GetSelVector().SetSelSize(1);

        stats.Update(vector);

        Assert.Equal(0UL, stats.GetNumDistinctValues());
    }
}
