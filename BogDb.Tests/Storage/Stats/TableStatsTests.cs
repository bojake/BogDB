using System;
using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Storage.Stats;
using Xunit;

namespace BogDb.Tests.Storage.Stats;

public class TableStatsTests
{
    [Fact]
    public void TableStats_CopyConstructor_DeepCopiesColumnStats()
    {
        var tableStats = new TableStats(new List<PhysicalTypeID> { PhysicalTypeID.INT32 });
        using var v1 = new ValueVector(LogicalTypeID.INT32, capacity: 8);
        v1.SetValue(0, 1);
        v1.SetValue(1, 2);
        v1.State.GetSelVector().SetSelSize(2);
        tableStats.Update(new List<ValueVector> { v1 }, numColumns: 1);

        var copy = new TableStats(tableStats);
        var copiedCard = copy.GetTableCard();
        var copiedDistinct = copy.GetNumDistinctValues(0);

        using var v2 = new ValueVector(LogicalTypeID.INT32, capacity: 8);
        v2.SetValue(0, 3);
        v2.SetValue(1, 4);
        v2.State.GetSelVector().SetSelSize(2);
        tableStats.Update(new List<ValueVector> { v2 }, numColumns: 1);

        Assert.Equal(copiedCard, copy.GetTableCard());
        Assert.Equal(copiedDistinct, copy.GetNumDistinctValues(0));
        Assert.True(tableStats.GetTableCard() > copy.GetTableCard());
    }

    [Fact]
    public void TableStats_Update_ThrowsOnSelectionSizeMismatch()
    {
        var stats = new TableStats(new List<PhysicalTypeID> { PhysicalTypeID.INT32, PhysicalTypeID.INT32 });
        using var v1 = new ValueVector(LogicalTypeID.INT32, capacity: 8);
        using var v2 = new ValueVector(LogicalTypeID.INT32, capacity: 8);
        v1.SetValue(0, 1);
        v1.SetValue(1, 2);
        v2.SetValue(0, 1);
        v1.State.GetSelVector().SetSelSize(2);
        v2.State.GetSelVector().SetSelSize(1);

        Assert.Throws<InvalidOperationException>(() =>
            stats.Update(new List<uint> { 0, 1 }, new List<ValueVector> { v1, v2 }, numColumns: 2));
    }

    [Fact]
    public void TableStats_Update_ThrowsOnColumnVectorCountMismatch()
    {
        var stats = new TableStats(new List<PhysicalTypeID> { PhysicalTypeID.INT32 });
        using var v1 = new ValueVector(LogicalTypeID.INT32, capacity: 8);
        v1.SetValue(0, 1);
        v1.State.GetSelVector().SetSelSize(1);

        Assert.Throws<ArgumentException>(() =>
            stats.Update(new List<uint> { 0, 1 }, new List<ValueVector> { v1 }, numColumns: 1));
    }

    [Fact]
    public void TableStats_Merge_ThrowsWhenColumnMappingSizeMismatchesSource()
    {
        var left = new TableStats(new List<PhysicalTypeID> { PhysicalTypeID.INT32, PhysicalTypeID.INT32 });
        var right = new TableStats(new List<PhysicalTypeID> { PhysicalTypeID.INT32 });

        Assert.Throws<ArgumentException>(() => left.Merge(new List<uint> { 0, 1 }, right));
    }
}
