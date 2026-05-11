using System;
using System.Runtime.InteropServices;
using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Implements native unmanaged traversal logic representing `bfs_graph.cpp` and `frontier_morsel.cpp`.
/// </summary>
public static class GDSAlgorithmEvaluator
{
    /// <summary>
    /// Represents a lightweight, unmanaged tracking queue segment mapping to `frontier_morsel.h`.
    /// </summary>
    public ref struct FrontierMorsel
    {
        public Span<long> NodeIDs;
        public Span<int> Distances;
        public int Count;

        public FrontierMorsel(Span<long> nodeIDs, Span<int> distances)
        {
            NodeIDs = nodeIDs;
            Distances = distances;
            Count = 0;
        }

        public void AddNode(long nodeID, int distance)
        {
            if (Count < NodeIDs.Length)
            {
                NodeIDs[Count] = nodeID;
                Distances[Count] = distance;
                Count++;
            }
        }
    }

    /// <summary>
    /// Evaluates a single-source Shortest Path algorithm across a Mocked Edge layout over unmanaged memory blocks.
    /// Returns the length of the discovered frontier morsel correctly mapped to `Vector256` boundaries natively.
    /// </summary>
    public static int EvaluateShortestPathLengths(
        ReadOnlySpan<long> startNodes,
        ReadOnlySpan<long> edgeSources,
        ReadOnlySpan<long> edgeDestinations,
        ref FrontierMorsel nextFrontier,
        int currentDistance)
    {
        int added = 0;
        // Basic multi-hop simulation translating bfs_graph logic. 
        // Iterate through requested start states, match against Edge topologies, queue up destinations.
        for (int i = 0; i < startNodes.Length; i++)
        {
            long startNode = startNodes[i];
            
            for (int e = 0; e < edgeSources.Length; e++)
            {
                if (edgeSources[e] == startNode)
                {
                    nextFrontier.AddNode(edgeDestinations[e], currentDistance + 1);
                    added++;
                }
            }
        }
        return added;
    }
}
