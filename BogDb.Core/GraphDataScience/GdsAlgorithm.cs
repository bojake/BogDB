using System;
using System.Collections.Generic;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Abstract base class for all GDS algorithms.
/// C++ parity: <c>src/include/function/gds/gds.h</c> — <c>GDSFunction</c>.
///
/// Subclasses implement <see cref="Execute"/> which mutates an internal
/// result table keyed by <see cref="NodeId"/>.  Callers iterate the result
/// via <see cref="GetResults"/>.
/// </summary>
public abstract class GdsAlgorithm
{
    protected IGraph Graph { get; }

    /// <summary>Human-readable algorithm name (e.g. "pagerank", "wcc").</summary>
    public abstract string AlgorithmName { get; }

    /// <summary>Output column names produced by this algorithm.</summary>
    public abstract IReadOnlyList<string> OutputColumns { get; }

    /// <summary>
    /// Per-node result storage: NodeId → column-name → value.
    /// Populated by <see cref="Execute"/>.
    /// </summary>
    protected readonly Dictionary<NodeId, Dictionary<string, object?>> Results = new();

    protected GdsAlgorithm(IGraph graph)
    {
        Graph = graph;
    }

    /// <summary>Runs the algorithm to completion. Must be called before <see cref="GetResults"/>.</summary>
    public abstract void Execute(GdsCallOptions options);

    /// <summary>Streams all output rows after <see cref="Execute"/> completes.</summary>
    public IEnumerable<GdsResultRow> GetResults()
    {
        foreach (var (nid, vals) in Results)
            yield return new GdsResultRow(nid, vals);
    }

    /// <summary>Returns the scalar value of the first output column for a given node.</summary>
    public object? GetScalar(NodeId nodeId)
    {
        if (!Results.TryGetValue(nodeId, out var row)) return null;
        return OutputColumns.Count > 0 && row.TryGetValue(OutputColumns[0], out var v) ? v : null;
    }
}

/// <summary>Options passed to <see cref="GdsAlgorithm.Execute"/>.</summary>
public sealed class GdsCallOptions
{
    /// <summary>Maximum BFS/iteration depth for algorithms that support it.</summary>
    public int MaxIterations { get; init; } = 20;

    /// <summary>Damping factor for PageRank (default: 0.85).</summary>
    public double DampingFactor { get; init; } = 0.85;

    /// <summary>Convergence tolerance for rank-based algorithms.</summary>
    public double Tolerance { get; init; } = 1e-6;

    /// <summary>Source node for SSSP and k-hop; null = all nodes (for WCC).</summary>
    public NodeId? SourceNode { get; init; }

    /// <summary>Optional edge weight property used by weighted algorithms.</summary>
    public string? WeightProperty { get; init; }

    /// <summary>Direction: "OUT" | "IN" | "BOTH"</summary>
    public string Direction { get; init; } = "OUT";

    /// <summary>Maximum hop count for k-hop / variable-length path algorithms.</summary>
    public int MaxHops { get; init; } = int.MaxValue;

    /// <summary>
    /// Maximum degree of parallelism for computations that support it.
    /// 0 = use all available processors (<see cref="Environment.ProcessorCount"/>).
    /// 1 = force single-threaded execution (useful for deterministic testing).
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = 0;

    /// <summary>Returns true if this options set enables parallel execution.</summary>
    public bool IsParallel => MaxDegreeOfParallelism != 1;

    public static GdsCallOptions Defaults => new();

    /// <summary>Sequential (single-threaded) variant — useful for tests that need deterministic order.</summary>
    public static GdsCallOptions Sequential => new() { MaxDegreeOfParallelism = 1 };
}
