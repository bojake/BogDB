using System;
using System.Collections.Generic;
using BogDb.Core.Main;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Maps CALL function names to algorithm factory delegates.
/// C++ parity: <c>GDSFunctionCollection</c> in <c>gds_function_collection.h</c>.
///
/// Routing:
/// - When <see cref="GdsCallOptions.IsParallel"/> is true (default), uses the
///   parallel algorithm variants (Parallel{PageRank,Wcc,Sssp}).
/// - When <see cref="GdsCallOptions.MaxDegreeOfParallelism"/> == 1, uses the
///   single-threaded sequential variants for deterministic results / testing.
///
/// Usage:
/// <code>
///   var algo = GdsRegistry.Create("pagerank", graph, options);
///   algo.Execute(options);
///   foreach (var row in algo.GetResults()) { ... }
/// </code>
/// </summary>
public static class GdsRegistry
{
    // ── Parallel factories ────────────────────────────────────────────────────
    private static readonly Dictionary<string, Func<IGraph, GdsCallOptions, GdsAlgorithm>> _parallelFactories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pagerank"]                      = (g, o) => new ParallelPageRankAlgorithm(g, o.MaxDegreeOfParallelism),
            ["weakly_connected_components"]   = (g, o) => new ParallelWccAlgorithm(g, o.MaxDegreeOfParallelism),
            ["wcc"]                           = (g, o) => new ParallelWccAlgorithm(g, o.MaxDegreeOfParallelism),
            ["single_source_shortest_paths"]  = (g, o) => new ParallelSsspAlgorithm(g, o.MaxDegreeOfParallelism),
            ["sssp"]                          = (g, o) => new ParallelSsspAlgorithm(g, o.MaxDegreeOfParallelism),
            ["shortest_path"]                 = (g, o) => new ParallelSsspAlgorithm(g, o.MaxDegreeOfParallelism),
            ["all_shortest_paths"]            = (g, o) => new ParallelSsspAlgorithm(g, o.MaxDegreeOfParallelism),
            ["k_hop"]                         = (g, o) => new ParallelKHopAlgorithm(g, o.MaxDegreeOfParallelism),
            ["k_hop_neighbors"]               = (g, o) => new ParallelKHopAlgorithm(g, o.MaxDegreeOfParallelism),
            ["variable_length_path"]          = (g, o) => new ParallelVariableLengthPathAlgorithm(g, 1, o.MaxDegreeOfParallelism),
            ["var_length_path"]               = (g, o) => new ParallelVariableLengthPathAlgorithm(g, 1, o.MaxDegreeOfParallelism),
            ["sssp_delta"]                    = (g, o) => new DeltaSteppingSsspAlgorithm(g, o.MaxDegreeOfParallelism),
            ["delta_stepping"]               = (g, o) => new DeltaSteppingSsspAlgorithm(g, o.MaxDegreeOfParallelism),
            // Path reconstruction variants (C++ parity: ssp_paths, asp_paths, wsp_paths)
            ["sssp_paths"]                    = (g, o) => new SsspPathsAlgorithm(g, o.MaxDegreeOfParallelism),
            ["shortest_paths"]                = (g, o) => new SsspPathsAlgorithm(g, o.MaxDegreeOfParallelism),
            ["single_sp_paths"]               = (g, o) => new SsspPathsAlgorithm(g, o.MaxDegreeOfParallelism),
        };

    // ── Sequential (single-threaded) factories ────────────────────────────────
    private static readonly Dictionary<string, Func<IGraph, GdsAlgorithm>> _sequentialFactories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pagerank"]                      = g => new PageRankAlgorithm(g),
            ["weakly_connected_components"]   = g => new WccAlgorithm(g),
            ["wcc"]                           = g => new WccAlgorithm(g),
            ["single_source_shortest_paths"]  = g => new SsspAlgorithm(g),
            ["sssp"]                          = g => new SsspAlgorithm(g),
            ["shortest_path"]                 = g => new SsspAlgorithm(g),
            ["all_shortest_paths"]            = g => new SsspAlgorithm(g),
            ["k_hop"]                         = g => new KHopAlgorithm(g),
            ["k_hop_neighbors"]               = g => new KHopAlgorithm(g),
            ["variable_length_path"]          = g => new VariableLengthPathAlgorithm(g),
            ["var_length_path"]               = g => new VariableLengthPathAlgorithm(g),
            ["sssp_delta"]                    = g => new DeltaSteppingSsspAlgorithm(g),
            ["delta_stepping"]               = g => new DeltaSteppingSsspAlgorithm(g),
            // Path reconstruction variants
            ["sssp_paths"]                    = g => new SsspPathsAlgorithm(g, 1),
            ["shortest_paths"]                = g => new SsspPathsAlgorithm(g, 1),
            ["single_sp_paths"]               = g => new SsspPathsAlgorithm(g, 1),
        };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns true if <paramref name="name"/> is a registered GDS algorithm.</summary>
    public static bool IsGdsFunction(string name)
        => _parallelFactories.ContainsKey(name);

    /// <summary>
    /// Creates a GDS algorithm instance with auto-selected parallel/sequential variant.
    /// Returns null for unknown names.
    /// </summary>
    public static GdsAlgorithm? Create(string name, IGraph graph,
        GdsCallOptions? options = null)
    {
        options ??= GdsCallOptions.Defaults;
        if (options.IsParallel)
            return _parallelFactories.TryGetValue(name, out var pf) ? pf(graph, options) : null;
        return _sequentialFactories.TryGetValue(name, out var sf) ? sf(graph) : null;
    }

    /// <summary>All registered GDS algorithm names.</summary>
    public static IEnumerable<string> RegisteredNames => _parallelFactories.Keys;

    /// <summary>
    /// Convenience: build a <see cref="GraphAdapter"/> from a <see cref="BogDatabase"/>
    /// and create the named algorithm in one call.
    /// </summary>
    public static GdsAlgorithm? CreateFromDb(string name, BogDatabase db,
        GdsCallOptions? options = null, string? weightProperty = null)
    {
        var graph = new GraphAdapter(db.NodeTables, db.RelTables, tx: null, weightProperty);
        return Create(name, graph, options);
    }
}
