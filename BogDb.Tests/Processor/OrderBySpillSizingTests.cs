using System;
using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Processor.Operator.OrderBy;
using BogDb.Core.Processor;
using Xunit;

namespace BogDb.Tests.Processor;

public sealed class OrderBySpillSizingTests
{
    [Fact]
    public void EstimateSerializedStringSizeForTests_UsesUtf8PayloadAnd7BitLengthPrefix()
    {
        Assert.Equal(4L, OrderBy.EstimateSerializedStringSizeForTests("abc"));
        Assert.Equal(3L, OrderBy.EstimateSerializedStringSizeForTests("é"));
        Assert.Equal(322L, OrderBy.EstimateSerializedStringSizeForTests(new string('a', 320)));
    }

    [Fact]
    public void EstimateSerializedRecordSizeForTests_MatchesActualSerializedBytes_ForNestedState()
    {
        var keys = new object?[]
        {
            "résumé",
            new List<object?> { 42, "東京", new Dictionary<string, object?> { ["k"] = new byte[] { 1, 2, 3 } } }
        };

        var state = new ExecutionState
        {
            CurrentNodeId = 99L,
            ParameterBindings = new Dictionary<string, object?>
            {
                ["limit"] = 5,
                ["payload"] = new List<object?> { "é", 7L, null }
            },
            CurrentNodeProperties = new Dictionary<string, object>
            {
                ["name"] = "zoe",
                ["born"] = new DateOnly(2024, 2, 29)
            },
            CurrentScalarBindings = new Dictionary<string, object?>
            {
                ["score"] = 1.25m,
                ["window"] = new BogDbInterval(3, 4, 5000)
            },
            CurrentProjectionRow = new object[]
            {
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                new Dictionary<string, object?> { ["nested"] = "value", ["active"] = true }
            },
            CurrentVariableIds = new Dictionary<string, object>
            {
                ["p"] = 123L
            },
            CurrentVariableProperties = new Dictionary<string, Dictionary<string, object>>
            {
                ["p"] = new()
                {
                    ["name"] = "Ada",
                    ["tags"] = new List<object?> { "math", "logic" }
                }
            },
            AggregateValues = new Dictionary<string, object?>
            {
                ["count(*)"] = 2L,
                ["maxScore"] = 9.5
            },
            SemiMasks = new Dictionary<ulong, HashSet<object>>
            {
                [7UL] = new() { 1L, "node-7" }
            }
        };

        var estimated = OrderBy.EstimateSerializedRecordSizeForTests(keys, state);
        var serialized = OrderBy.SerializeRecordForTests(keys, state);

        Assert.Equal(serialized.LongLength, estimated);
    }

    [Fact]
    public void ShouldSpillBeforeAppendForTests_PrespillsPopulatedChunk_ButNotEmptyChunk()
    {
        Assert.True(OrderBy.ShouldSpillBeforeAppendForTests(
            currentRowCount: 1,
            currentChunkBytes: 350,
            nextRecordBytes: 200,
            chunkByteLimit: 512));

        Assert.False(OrderBy.ShouldSpillBeforeAppendForTests(
            currentRowCount: 0,
            currentChunkBytes: 0,
            nextRecordBytes: 700,
            chunkByteLimit: 512));
    }

    [Fact]
    public void ResolveChunkCapacityForTests_AdaptsToObservedRecordWidth()
    {
        var originalChunkLimit = OrderBy.ChunkRowLimit;
        OrderBy.ChunkRowLimit = 2048;

        try
        {
            Assert.Equal(64, OrderBy.ResolveChunkCapacityForTests(
                chunkByteLimit: 4096,
                averageRecordBytes: 64));

            Assert.Equal(2048, OrderBy.ResolveChunkCapacityForTests(
                chunkByteLimit: 4096,
                averageRecordBytes: 0));

            Assert.Equal(1, OrderBy.ResolveChunkCapacityForTests(
                chunkByteLimit: 4096,
                averageRecordBytes: 8192));
        }
        finally
        {
            OrderBy.ChunkRowLimit = originalChunkLimit;
        }
    }

    [Fact]
    public void CompareKeysForTests_UsesPrimitiveFastPath_WithStableOrdering()
    {
        Assert.True(OrderBy.CompareKeysForTests(
            new[] { true, false },
            new object?[] { 1L, "beta" },
            new object?[] { 2L, "alpha" }) < 0);

        Assert.True(OrderBy.CompareKeysForTests(
            new[] { true },
            new object?[] { new BogDbInterval(0, 1, 0) },
            new object?[] { new BogDbInterval(0, 2, 0) }) < 0);
    }

    [Fact]
    public void CompareKeysForTests_FallsBackToStructuralOrdering_ForMixedTypes()
    {
        var left = new object?[] { 1 };
        var right = new object?[] { 2L };

        Assert.Equal(
            StructuralValueOrderComparer.CompareValues(left[0], right[0]),
            OrderBy.CompareKeysForTests(new[] { true }, left, right));
    }
}
