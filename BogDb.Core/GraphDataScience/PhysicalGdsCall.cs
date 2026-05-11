using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Main;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Physical operator that runs a registered GDS algorithm and streams its
/// output rows as <see cref="Dictionary{string, object?}"/> tuples.
///
/// C++ parity: The C++ GDS pipeline uses <c>GDSFunction</c> bound as a
/// <c>TableFunction</c> with a morsel-based streaming model. BogDB uses a
/// simpler pull-based model where Execute() runs the full algorithm eagerly
/// and then rows are streamed via <see cref="Next"/>.
///
/// Lifecycle:
/// <code>
///   var op = new PhysicalGdsCall(db, "pagerank", options);
///   while (op.Next(out var row)) { /* process row */ }
/// </code>
/// </summary>
public sealed class PhysicalGdsCall
{
    // ── State ──────────────────────────────────────────────────────────────────

    private readonly string             _algorithmName;
    private readonly GdsCallOptions     _options;
    private readonly BogDatabase       _database;
    private readonly BogDb.Core.Transaction.Transaction? _tx;

    private IEnumerator<GdsResultRow>? _cursor;
    private bool _executed;

    // Last-run result cache (keyed by nodeId, value = first output column scalar)
    // This is what GdsFunctions.cs scalar functions read back.
    private static readonly Dictionary<string, Dictionary<NodeId, object?>> _lastResults =
        new(StringComparer.OrdinalIgnoreCase);

    // ── Construction ──────────────────────────────────────────────────────────

    /// <param name="database">Active database providing node/rel tables.</param>
    /// <param name="algorithmName">Registered GDS algorithm name (e.g. "pagerank").</param>
    /// <param name="options">Algorithm configuration.</param>
    public PhysicalGdsCall(
        BogDatabase database,
        string algorithmName,
        GdsCallOptions? options = null,
        BogDb.Core.Transaction.Transaction? tx = null)
    {
        _database      = database;
        _algorithmName = algorithmName;
        _options       = options ?? GdsCallOptions.Defaults;
        _tx            = tx;
    }

    // ── Streaming interface ────────────────────────────────────────────────────

    /// <summary>Returns the output columns of the algorithm.</summary>
    public IReadOnlyList<string> OutputColumns
    {
        get
        {
            var graph = BuildGraph();
            var algo  = GdsRegistry.Create(_algorithmName, graph, _options);
            return algo?.OutputColumns ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Advances to the next result row. Returns false when exhausted.
    /// Executes the algorithm on the first call.
    /// </summary>
    public bool Next(out Dictionary<string, object?> row)
    {
        if (!_executed) Execute();

        if (_cursor!.MoveNext())
        {
            // Convert GdsResultRow to plain dict
            row = new Dictionary<string, object?>(_cursor.Current.Values,
                StringComparer.OrdinalIgnoreCase);
            return true;
        }
        row = new Dictionary<string, object?>();
        return false;
    }

    /// <summary>Reset the stream back to the beginning without re-executing.</summary>
    public void Reset()
    {
        _cursor?.Dispose();
        _cursor   = null;
        _executed = false;
    }

    // ── Execution ─────────────────────────────────────────────────────────────

    private void Execute()
    {
        _executed = true;
        var graph = BuildGraph();
        var algo  = GdsRegistry.Create(_algorithmName, graph, _options);

        if (algo == null)
            throw new InvalidOperationException(
                $"Unknown GDS algorithm: '{_algorithmName}'. " +
                $"Known: {string.Join(", ", GdsRegistry.RegisteredNames)}");

        algo.Execute(_options);

        // Cache scalar results for GdsFunctions.cs lookups
        var scalarCache = new Dictionary<NodeId, object?>();
        foreach (var row in algo.GetResults())
        {
            if (algo.OutputColumns.Count > 1 &&
                row.Values.TryGetValue(algo.OutputColumns[1], out var val))
                scalarCache[row.NodeId] = val;
            else if (algo.OutputColumns.Count > 0 &&
                     row.Values.TryGetValue(algo.OutputColumns[0], out var val2))
                scalarCache[row.NodeId] = val2;
        }
        _lastResults[_algorithmName] = scalarCache;

        _cursor = algo.GetResults().GetEnumerator();
    }

    private IGraph BuildGraph()
        => new GraphAdapter(_database.NodeTables, _database.RelTables, _tx, _options.WeightProperty);

    // ── Static scalar look-up (for GdsFunctions.cs) ──────────────────────────

    /// <summary>
    /// Retrieves the last computed scalar value for <paramref name="algorithmName"/>
    /// and <paramref name="nodeId"/> from the internally cached results.
    /// Returns null if the algorithm has not been run or the node has no result.
    /// </summary>
    public static object? GetLastScalar(string algorithmName, NodeId nodeId)
    {
        return _lastResults.TryGetValue(algorithmName, out var cache)
            ? cache.TryGetValue(nodeId, out var v) ? v : null
            : null;
    }

    /// <summary>Clears the cached results for all algorithms.</summary>
    public static void ClearCache() => _lastResults.Clear();
}

/// <summary>
/// Extension methods for running GDS algorithms from <see cref="BogConnection"/>.
/// </summary>
public static class GdsConnectionExtensions
{
    /// <summary>
    /// Runs a GDS algorithm and returns all rows as a list of dictionaries.
    /// </summary>
    public static List<Dictionary<string, object?>> RunGds(
        this BogConnection conn,
        string algorithmName,
        GdsCallOptions? options = null)
    {
        var op   = new PhysicalGdsCall(conn.Database, algorithmName, options, conn.GetCurrentTransactionOrNull());
        var rows = new List<Dictionary<string, object?>>();
        while (op.Next(out var row)) rows.Add(row);
        return rows;
    }

    /// <summary>
    /// Runs a GDS algorithm and returns the scalar result for a specific node ID.
    /// </summary>
    public static object? RunGdsScalar(
        this BogConnection conn,
        string algorithmName,
        NodeId nodeId,
        GdsCallOptions? options = null)
    {
        var rows = conn.RunGds(algorithmName, options);
        // Result is cached; use static accessor
        return PhysicalGdsCall.GetLastScalar(algorithmName, nodeId);
    }
}
