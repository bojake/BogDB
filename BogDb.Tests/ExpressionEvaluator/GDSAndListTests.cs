using System;
using System.Runtime.InteropServices;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.ExpressionEvaluator;

namespace BogDb.Tests.ExpressionEvaluator;

public class GDSAndListTests
{
    [Fact]
    public void GDS_FrontierMorsel_ComputesShortestPath()
    {
        // Simulate a tiny graph: Node 0 -> Node 1, Node 1 -> Node 2, Node 0 -> Node 3
        long[] edgeSources = { 0, 1, 0 };
        long[] edgeDestinations = { 1, 2, 3 };

        long[] startNodes = { 0 };

        Span<long> frontierNodes = new long[10];
        Span<int> frontierDistances = new int[10];

        var frontier = new GDSAlgorithmEvaluator.FrontierMorsel(frontierNodes, frontierDistances);

        // Act: Evaluate paths from 0 at depth 0
        int added = GDSAlgorithmEvaluator.EvaluateShortestPathLengths(
            startNodes.AsSpan(),
            edgeSources.AsSpan(),
            edgeDestinations.AsSpan(),
            ref frontier,
            0
        );

        // Assert
        Assert.Equal(2, added);
        Assert.Equal(2, frontier.Count);
        // Node 1 and Node 3 should be added at distance 1
        Assert.Equal(1, frontier.NodeIDs[0]);
        Assert.Equal(1, frontier.Distances[0]);
        Assert.Equal(3, frontier.NodeIDs[1]);
        Assert.Equal(1, frontier.Distances[1]);

        // Evaluate next hop from Node 1
        long[] nextStartNodes = { 1, 3 };
        var nextFrontierNodes = new long[10];
        var nextFrontierDistances = new int[10];
        var nextFrontier = new GDSAlgorithmEvaluator.FrontierMorsel(nextFrontierNodes, nextFrontierDistances);

        int addedHop2 = GDSAlgorithmEvaluator.EvaluateShortestPathLengths(
            nextStartNodes.AsSpan(),
            edgeSources.AsSpan(),
            edgeDestinations.AsSpan(),
            ref nextFrontier,
            1
        );

        Assert.Equal(1, addedHop2);
        Assert.Equal(2, nextFrontier.NodeIDs[0]);
        Assert.Equal(2, nextFrontier.Distances[0]); // distance 2 from source 0
    }

    [Fact]
    public void ListEvaluator_ExtractsCorrectly()
    {
        // 2 List Entries
        // Entry 0: [100, 200, 300] (offset 0, size 3)
        // Entry 1: [400, 500] (offset 3, size 2)
        var entries = new ListFunctionEvaluator.ListEntry[2];
        entries[0] = new ListFunctionEvaluator.ListEntry { Offset = 0, Size = 3 };
        entries[1] = new ListFunctionEvaluator.ListEntry { Offset = 3, Size = 2 };

        long[] dataBuffer = { 100, 200, 300, 400, 500 };

        using var resultVector = new ValueVector(LogicalTypeID.INT64, 2);

        // Extract index 1 (second element) from each list
        ListFunctionEvaluator.ListExtractInt64(
            entries, 
            dataBuffer, 
            1, 
            resultVector);

        Assert.Equal(200, resultVector.GetValue<long>(0));
        Assert.Equal(500, resultVector.GetValue<long>(1));

        // Extract index 2 (out of bounds for list 2)
        ListFunctionEvaluator.ListExtractInt64(
            entries, 
            dataBuffer, 
            2, 
            resultVector);

        Assert.Equal(300, resultVector.GetValue<long>(0));
        Assert.True(resultVector.IsNull(1));
    }

    [Fact]
    public void ListEvaluator_SlicesCorrectly()
    {
        var srcEntries = new ListFunctionEvaluator.ListEntry[2];
        srcEntries[0] = new ListFunctionEvaluator.ListEntry { Offset = 5, Size = 4 };
        srcEntries[1] = new ListFunctionEvaluator.ListEntry { Offset = 9, Size = 2 };

        var destEntries = new ListFunctionEvaluator.ListEntry[2];

        // Slice starting at index 1, length 2
        ListFunctionEvaluator.ListSlice(srcEntries, destEntries, 1, 2);

        // Assert list 0: offset 5+1 = 6, length min(2, 4-1) = 2
        Assert.Equal(6u, destEntries[0].Offset);
        Assert.Equal(2u, destEntries[0].Size);

        // Assert list 1: offset 9+1 = 10, length min(2, 2-1) = 1
        Assert.Equal(10u, destEntries[1].Offset);
        Assert.Equal(1u, destEntries[1].Size);

        // Out of bounds slice
        ListFunctionEvaluator.ListSlice(srcEntries, destEntries, 3, 5);
        Assert.Equal(8u, destEntries[0].Offset);
        Assert.Equal(1u, destEntries[0].Size);
        Assert.Equal(9u, destEntries[1].Offset);
        Assert.Equal(0u, destEntries[1].Size);
    }

    [Fact]
    public void ListEvaluator_Slices_BoundaryCases_MirrorsCypher()
    {
        // Testing extreme bounds as done in tck/expressions/list/List11.test
        // E.g., list[100:105], list[-5:1], list[2:0]
        var srcEntries = new ListFunctionEvaluator.ListEntry[1];
        srcEntries[0] = new ListFunctionEvaluator.ListEntry { Offset = 0, Size = 5 }; // List of size 5
        
        var destEntries = new ListFunctionEvaluator.ListEntry[1];

        // 1. Slice far beyond end (index 100) -> should be empty
        ListFunctionEvaluator.ListSlice(srcEntries, destEntries, 100, 5);
        Assert.Equal(0u, destEntries[0].Size);

        // 2. Slice with zero length
        ListFunctionEvaluator.ListSlice(srcEntries, destEntries, 2, 0);
        Assert.Equal(0u, destEntries[0].Size);

        // 3. Negative Indexing is usually normalized before slicing in BogDb execution 
        // (the frontend parser wraps negative indices into size - index). 
        // But if negative indices reach the offset calculator as raw C# ints, 
        // they are treated as unsigned logic jumps or caught by size clamps.
        // We simulate the normalized negative index `list[size-2 : size]` -> `list[3:5]`
        ListFunctionEvaluator.ListSlice(srcEntries, destEntries, 3, 2);
        Assert.Equal(3u, destEntries[0].Offset);
        Assert.Equal(2u, destEntries[0].Size);
    }
}
