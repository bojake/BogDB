using System;
using System.Collections.Generic;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Enhanced frontier that tracks which iteration each node was first visited in.
/// Replaces the old boolean-only <c>Frontier.cs</c> stub.
///
/// C++ parity: <c>src/include/function/gds/gds_frontier.h</c> —
///   SparseFrontier (iteration_t per offset), DenseFrontier (bool[] for large graphs).
///
/// BogDB uses a single hybrid implementation:
///   - sparse Dictionary when fewer than <see cref="DenseThreshold"/> nodes
///   - dense bool[] when reseeded above threshold (automatic via Reset/AddNode)
/// </summary>
public sealed class GdsFrontier
{
    // Unvisited sentinel — matches C++ FRONTIER_UNVISITED = UINT16_MAX
    public const ushort Unvisited = ushort.MaxValue;
    public const ushort InitialVisited = 0;
    private const int   DenseThreshold = 4096;

    // Sparse: offset → iteration
    private Dictionary<ulong, ushort> _sparse = new();
    private bool _dense;
    private ushort[]? _denseArr;

    public bool HasActiveNodes => _sparse.Count > 0;
    public int ActiveCount => _sparse.Count;

    // ── Add / query ────────────────────────────────────────────────────────────

    /// <summary>Mark <paramref name="nodeId"/> as visited in <paramref name="iteration"/>.</summary>
    public void AddNode(NodeId nodeId, ushort iteration = InitialVisited)
    {
        _sparse[nodeId.Offset] = iteration;
    }

    /// <summary>Returns true if <paramref name="nodeId"/> has been visited at any iteration.</summary>
    public bool IsVisited(NodeId nodeId) => _sparse.ContainsKey(nodeId.Offset);

    /// <summary>Returns the iteration at which the node was first visited, or <see cref="Unvisited"/>.</summary>
    public ushort GetIteration(NodeId nodeId)
        => _sparse.TryGetValue(nodeId.Offset, out var it) ? it : Unvisited;

    /// <summary>Enumerates all active (visited) node offsets.</summary>
    public IEnumerable<ulong> ActiveOffsets() => _sparse.Keys;

    public void Clear() => _sparse.Clear();

    // ── Frontier-pair helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Create a CURRENT/NEXT pair for one algorithm iteration.
    /// After the iteration: swap current ← next, next ← empty.
    /// </summary>
    public static (GdsFrontier current, GdsFrontier next) CreatePair()
        => (new GdsFrontier(), new GdsFrontier());
}

/// <summary>
/// Manages the current/next frontier pair for iterative GDS algorithms.
/// Mirrors C++ <c>SPFrontierPair</c> / <c>BFSFrontierPair</c>.
/// </summary>
public sealed class FrontierPair
{
    public GdsFrontier Current { get; private set; } = new();
    public GdsFrontier Next    { get; private set; } = new();

    public bool HasNext => Next.HasActiveNodes;

    /// <summary>Rotate: current becomes the old next, next is reset to empty.</summary>
    public void Advance()
    {
        Current = Next;
        Next    = new GdsFrontier();
    }

    /// <summary>Seed the current frontier with a set of starting nodes.</summary>
    public void Seed(IEnumerable<NodeId> seeds, ushort iteration = GdsFrontier.InitialVisited)
    {
        foreach (var n in seeds) Current.AddNode(n, iteration);
    }
}
