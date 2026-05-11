using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Main;
using BogDb.Core.GraphDataScience;

namespace BogDb.Core.Function.Gds;

/// <summary>
/// C++ parity: <c>src/function/gds/</c>
///
/// GDS scalar functions — these are the scalar RETURN expressions that read back
/// results from the last GDS pipeline run triggered via <c>CALL algo() YIELD *</c>.
///
/// After running:
///   CALL pagerank() YIELD *
/// the results are cached in <see cref="PhysicalGdsCall"/>'s static result store,
/// and these scalar functions can retrieve per-node values from that cache:
///   RETURN pagerank_score('0:0')
///
/// node_degree/has_path/graph_density/k_hop_count additionally use a thread-local
/// <see cref="BogDatabase"/> reference set by <see cref="SetDatabaseContext"/>
/// (called by <see cref="BogConnection.Query"/> alongside TableFunctions.SetCatalogContext).
///
/// Functions:
///   gds_version()                               → STRING: GDS module version
///   node_degree(id, dir?)                       → INT64: out/in/both degree from live graph
///   has_path(a, b, hops?)                       → BOOL: reads k_hop cache
///   shortest_path_length(a, b, maxHops?)        → DOUBLE | null: from last sssp run
///   graph_density()                             → DOUBLE: |E| / (|V| * (|V|-1))
///   pagerank_score(nodeId)                      → DOUBLE: from last pagerank run
///   wcc_component(nodeId)                       → INT64: from last wcc run
///   sssp_distance(src, tgt)                     → DOUBLE | null: from last sssp run
///   k_hop_count(nodeId, k?)                     → INT64: from last k_hop run
///   clear_gds_cache()                           → STRING: "OK"
/// </summary>
public static class GdsFunctions
{
    // Thread-local database reference — set by BogConnection.Query (same pattern as TableFunctions)
    [ThreadStatic] private static BogDatabase? _db;

    /// <summary>
    /// Set the active database for this thread so GDS scalar functions can access
    /// the live graph (node_degree, graph_density, k_hop_count, has_path).
    /// </summary>
    public static void SetDatabaseContext(BogDatabase db) => _db = db;

    public static void Register(Dictionary<string, Func<object?[], object?>> funcs)
    {
        // gds_version() — module version string
        funcs["gds_version"] = _ => Table.TableFunctions.BogDbNgVersion;

        // ── node_degree(id, direction?) ────────────────────────────────────────
        // direction: "out" (default), "in", "both"
        funcs["node_degree"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            var nid = ParseNodeId(args[0]);
            if (nid == null) return null;
            var dir = args.Length > 1 ? (args[1]?.ToString() ?? "out").ToLowerInvariant() : "out";
            if (_db == null) return 0L;

            var graph = BuildGraph();
            return dir switch
            {
                "in"   => graph.InDegree(nid.Value),
                "both" => graph.OutDegree(nid.Value) + graph.InDegree(nid.Value),
                _      => graph.OutDegree(nid.Value)  // "out" or any other value
            };
        };

        // ── has_path(a, b, maxHops?) ───────────────────────────────────────────
        // Reads the k_hop result cache: if there is a k_hop result row for source
        // node a that contains b, then a path exists.
        // Falls back to a live BFS if no k_hop cache is available.
        funcs["has_path"] = args =>
        {
            if (args.Length < 2) return false;
            var srcId = ParseNodeId(args[0]);
            var dstId = ParseNodeId(args[1]);
            if (srcId == null || dstId == null) return false;

            // Try k_hop cache first (fast path)
            // k_hop stores per-node rows; check if the "reachable" column contains dstId
            // We use a live BFS because the k_hop cache stores summary scalars, not neighbor lists.
            if (_db == null) return false;
            var maxHops = args.Length > 2 && args[2] != null
                ? (int)Common.TypeCoercionHelper.ToInt64(args[2])
                : 10;
            return LiveBfsReachable(BuildGraph(), srcId.Value, dstId.Value, maxHops);
        };

        // ── shortest_path_length(a, b, maxHops?) ──────────────────────────────
        funcs["shortest_path_length"] = args =>
        {
            if (args.Length < 2) return null;
            var tgt = ParseNodeId(args[1]);
            if (tgt == null) return null;
            return PhysicalGdsCall.GetLastScalar("sssp", tgt.Value);
        };

        // ── graph_density() ───────────────────────────────────────────────────
        // |E| / (|V| * (|V|-1))  for directed graphs.
        // Returns 0.0 when |V| < 2.
        funcs["graph_density"] = _ =>
        {
            if (_db == null) return 0.0;
            var graph  = BuildGraph();
            var vCount = graph.NodeCount;
            if (vCount < 2) return 0.0;
            // Count total directed edges
            long eCount = graph.AllNodes().Sum(n => graph.OutDegree(n));
            return (double)eCount / (vCount * (vCount - 1));
        };

        // ── pagerank_score(nodeId) ─────────────────────────────────────────────
        funcs["pagerank_score"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return 0.0;
            var nid = ParseNodeId(args[0]);
            if (nid == null) return 0.0;
            var v = PhysicalGdsCall.GetLastScalar("pagerank", nid.Value);
            return v == null ? 0.0 : Convert.ToDouble(v);
        };

        // ── wcc_component(nodeId) ──────────────────────────────────────────────
        funcs["wcc_component"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            var nid = ParseNodeId(args[0]);
            if (nid == null) return null;
            var v = PhysicalGdsCall.GetLastScalar("wcc", nid.Value);
            return v == null ? null : (object?)Convert.ToInt64(v);
        };

        // ── sssp_distance(sourceNodeId, targetNodeId) ──────────────────────────
        funcs["sssp_distance"] = args =>
        {
            if (args.Length < 2) return null;
            var tgt = ParseNodeId(args[1]);
            if (tgt == null) return null;
            return PhysicalGdsCall.GetLastScalar("sssp", tgt.Value);
        };

        // ── k_hop_count(nodeId, k?) ────────────────────────────────────────────
        // k_hop stores scalar = reachable node count for given source.
        // After CALL k_hop() the cache contains per-(source,target) rows;
        // GetLastScalar returns the count column for the source node id directly
        // if the algorithm stored it, otherwise we do a live BFS count.
        funcs["k_hop_count"] = args =>
        {
            if (args.Length < 1 || args[0] == null) return 0L;
            var srcId = ParseNodeId(args[0]);
            if (srcId == null) return 0L;

            // Try scalar cache (populated if CALL k_hop() was run beforehand)
            var cached = PhysicalGdsCall.GetLastScalar("k_hop", srcId.Value);
            if (cached != null)
                return Convert.ToInt64(cached);

            // Live BFS computation
            if (_db == null) return 0L;
            var maxHops = args.Length > 1 && args[1] != null
                ? (int)Common.TypeCoercionHelper.ToInt64(args[1])
                : 1;
            return LiveBfsCount(BuildGraph(), srcId.Value, maxHops);
        };

        // ── clear_gds_cache() ──────────────────────────────────────────────────
        funcs["clear_gds_cache"] = _ => { PhysicalGdsCall.ClearCache(); return "OK"; };
    }

    // ── Graph context helpers ──────────────────────────────────────────────────

    private static IGraph BuildGraph()
        => new GraphAdapter(_db!.NodeTables, _db.RelTables, tx: null, weightProperty: null);

    private static bool LiveBfsReachable(IGraph graph, NodeId src, NodeId dst, int maxHops)
    {
        if (src == dst) return true;
        var visited = new HashSet<NodeId> { src };
        var frontier = new Queue<(NodeId id, int depth)>();
        frontier.Enqueue((src, 0));
        while (frontier.TryDequeue(out var cur))
        {
            if (cur.depth >= maxHops) continue;
            foreach (var edge in graph.GetOutEdges(cur.id))
            {
                if (edge.Target == dst) return true;
                if (visited.Add(edge.Target))
                    frontier.Enqueue((edge.Target, cur.depth + 1));
            }
        }
        return false;
    }

    private static long LiveBfsCount(IGraph graph, NodeId src, int maxHops)
    {
        var visited = new HashSet<NodeId> { src };
        var frontier = new Queue<(NodeId id, int depth)>();
        frontier.Enqueue((src, 0));
        while (frontier.TryDequeue(out var cur))
        {
            if (cur.depth >= maxHops) continue;
            foreach (var edge in graph.GetOutEdges(cur.id))
            {
                if (visited.Add(edge.Target))
                    frontier.Enqueue((edge.Target, cur.depth + 1));
            }
        }
        return visited.Count - 1; // exclude src
    }

    // ── Node ID parsing ───────────────────────────────────────────────────────

    private static NodeId? ParseNodeId(object? value)
    {
        if (value == null) return null;
        if (value is long l)  return new NodeId((ulong)l, 0);
        if (value is int i)   return new NodeId((ulong)i, 0);
        if (value is NodeId n) return n;
        if (value is string s)
        {
            var colon = s.IndexOf(':');
            if (colon > 0 &&
                uint.TryParse(s[..colon], out var tid) &&
                ulong.TryParse(s[(colon+1)..], out var off))
                return new NodeId(off, tid);
            if (ulong.TryParse(s, out var off2))
                return new NodeId(off2, 0);
        }
        return null;
    }
}
