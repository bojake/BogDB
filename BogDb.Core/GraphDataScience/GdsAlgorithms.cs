using System;
using System.Collections.Generic;
using System.Linq;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Iterative PageRank algorithm.
/// C++ parity: <c>src/extension/algo/pagerank.cpp</c>
///
/// Formula (per iteration):
///   rank[v] = (1 - d) / N + d * Σ(rank[u] / out_degree[u])
///   for each u → v
///
/// Convergence: stops when max rank delta &lt; <see cref="GdsCallOptions.Tolerance"/>,
/// or when <see cref="GdsCallOptions.MaxIterations"/> is reached.
/// </summary>
public sealed class PageRankAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "pagerank";
    public override IReadOnlyList<string> OutputColumns { get; } =
        new[] { "node", "rank" };

    public PageRankAlgorithm(IGraph graph) : base(graph) { }

    public override void Execute(GdsCallOptions options)
    {
        var nodes = Graph.AllNodes().ToList();
        int n = nodes.Count;
        if (n == 0) return;

        double d    = options.DampingFactor;
        double base_ = (1.0 - d) / n;

        // Initial uniform rank
        var rank = new Dictionary<NodeId, double>(n);
        foreach (var nid in nodes) rank[nid] = 1.0 / n;

        // Out-degree pre-cache
        var outDeg = new Dictionary<NodeId, long>(n);
        foreach (var nid in nodes) outDeg[nid] = Graph.OutDegree(nid);

        // Iterative update
        for (int iter = 0; iter < options.MaxIterations; iter++)
        {
            var newRank = new Dictionary<NodeId, double>(n);
            // Dangling node mass (nodes with out-degree 0 distribute evenly)
            double dangling = nodes
                .Where(v => outDeg[v] == 0)
                .Sum(v => rank[v]) / n;

            foreach (var v in nodes)
            {
                double inSum = 0.0;
                foreach (var e in Graph.GetInEdges(v))
                {
                    if (outDeg.TryGetValue(e.Source, out var od) && od > 0)
                        inSum += rank[e.Source] / od;
                }
                newRank[v] = base_ + d * (inSum + dangling);
            }

            // Check convergence
            double maxDelta = 0;
            foreach (var v in nodes)
                maxDelta = Math.Max(maxDelta, Math.Abs(newRank[v] - rank[v]));

            rank = newRank;
            if (maxDelta < options.Tolerance) break;
        }

        // Materialise results
        foreach (var (nid, r) in rank)
        {
            Results[nid] = new Dictionary<string, object?>
            {
                ["node"] = nid.ToString(),
                ["rank"] = r,
            };
        }
    }
}

/// <summary>
/// Weakly Connected Components via BFS label propagation.
/// C++ parity: <c>src/extension/algo/weakly_connected_components.cpp</c>
///
/// Each connected component is assigned the minimum NodeId offset of any node
/// it contains.  Nodes in the same component share the same component label.
/// </summary>
public sealed class WccAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "wcc";
    public override IReadOnlyList<string> OutputColumns { get; } =
        new[] { "node", "component_id" };

    public WccAlgorithm(IGraph graph) : base(graph) { }

    public override void Execute(GdsCallOptions options)
    {
        var nodes = Graph.AllNodes().ToList();
        if (nodes.Count == 0) return;

        // Union-Find (path-compressed)
        var parent = new Dictionary<NodeId, NodeId>();
        foreach (var n in nodes) parent[n] = n;

        NodeId Find(NodeId x)
        {
            while (!parent[x].Equals(x))
            {
                // Path compression
                if (parent.TryGetValue(parent[x], out var gp)) parent[x] = gp;
                x = parent[x];
            }
            return x;
        }

        void Union(NodeId a, NodeId b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra.Equals(rb)) return;
            // Merge smaller component offset into larger to keep label minimal
            if (ra.Offset < rb.Offset)
                parent[rb] = ra;
            else
                parent[ra] = rb;
        }

        // Process all edges in both directions (weakly connected = ignore directionality)
        foreach (var n in nodes)
        {
            foreach (var e in Graph.GetOutEdges(n)) Union(e.Source, e.Target);
            foreach (var e in Graph.GetInEdges(n))  Union(e.Source, e.Target);
        }

        // Materialise: component label = root's offset
        foreach (var n in nodes)
        {
            var root = Find(n);
            Results[n] = new Dictionary<string, object?>
            {
                ["node"]         = n.ToString(),
                ["component_id"] = (long)root.Offset,
            };
        }
    }
}

/// <summary>
/// Single-Source Shortest Paths via Dijkstra (weighted) or BFS (unweighted).
/// C++ parity: <c>src/function/gds/ssp_destinations.cpp</c> / <c>ssp_paths.cpp</c>
///
/// If the graph has no weight property, distances are hop counts (BFS).
/// If a weight property is specified, Dijkstra's algorithm is used with a min-heap.
/// </summary>
public sealed class SsspAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "sssp";
    public override IReadOnlyList<string> OutputColumns { get; } =
        new[] { "node", "distance" };

    public SsspAlgorithm(IGraph graph) : base(graph) { }

    public override void Execute(GdsCallOptions options)
    {
        var nodes = Graph.AllNodes().ToList();
        if (nodes.Count == 0) return;

        var source = options.SourceNode ?? nodes[0];
        bool weighted = Graph.WeightProperty != null;

        var dist = new Dictionary<NodeId, double>();
        foreach (var n in nodes) dist[n] = double.PositiveInfinity;
        dist[source] = 0.0;

        if (weighted)
            RunDijkstra(source, dist, options);
        else
            RunBfs(source, dist, options);

        foreach (var (nid, d) in dist)
        {
            Results[nid] = new Dictionary<string, object?>
            {
                ["node"]     = nid.ToString(),
                ["distance"] = double.IsPositiveInfinity(d) ? null : (object?)d,
            };
        }
    }

    private void RunBfs(NodeId source, Dictionary<NodeId, double> dist, GdsCallOptions opts)
    {
        var queue = new Queue<NodeId>();
        queue.Enqueue(source);
        int maxHops = opts.MaxHops == int.MaxValue ? int.MaxValue : opts.MaxHops;

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var curDist = dist[cur];
            if (curDist >= maxHops) continue;

            foreach (var e in GetEdges(cur, opts.Direction))
            {
                var nbr = GetNeighbour(cur, e);
                if (dist[nbr] == double.PositiveInfinity)
                {
                    dist[nbr] = curDist + 1;
                    queue.Enqueue(nbr);
                }
            }
        }
    }

    private void RunDijkstra(NodeId source, Dictionary<NodeId, double> dist, GdsCallOptions opts)
    {
        // Simple priority queue via SortedSet
        var pq = new SortedSet<(double d, ulong off, uint tid)>(
            Comparer<(double, ulong, uint)>.Create((a, b) =>
            {
                int c = a.Item1.CompareTo(b.Item1);
                if (c != 0) return c;
                c = a.Item2.CompareTo(b.Item2);
                return c != 0 ? c : a.Item3.CompareTo(b.Item3);
            }));
        pq.Add((0.0, source.Offset, source.TableId));
        int maxHops = opts.MaxHops == int.MaxValue ? int.MaxValue : opts.MaxHops;

        while (pq.Count > 0)
        {
            var (curD, off, tid) = pq.Min;
            pq.Remove(pq.Min);
            var cur = new NodeId(off, tid);

            if (curD > dist[cur]) continue;
            if ((int)curD >= maxHops) continue;

            foreach (var e in GetEdges(cur, opts.Direction))
            {
                var nbr = GetNeighbour(cur, e);
                double newDist = curD + e.Weight;
                if (newDist < dist[nbr])
                {
                    dist[nbr] = newDist;
                    pq.Add((newDist, nbr.Offset, nbr.TableId));
                }
            }
        }
    }

    private IEnumerable<GdsEdge> GetEdges(NodeId n, string dir) => dir switch
    {
        "IN"   => Graph.GetInEdges(n),
        "BOTH" => Graph.GetBothEdges(n),
        _      => Graph.GetOutEdges(n),
    };

    private static NodeId GetNeighbour(NodeId src, GdsEdge e)
        => e.Source.Equals(src) ? e.Target : e.Source;
}

/// <summary>
/// K-Hop Reachability: finds all nodes reachable within exactly [1, maxHops] hops.
/// C++ parity: variable_length_path.cpp / asp_destinations.cpp
///
/// Output: one row per (source, target) pair that is reachable within maxHops hops,
/// with the minimum hop count in the distance column.
/// </summary>
public sealed class KHopAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "k_hop";
    public override IReadOnlyList<string> OutputColumns { get; } =
        new[] { "node", "source", "hops" };

    public KHopAlgorithm(IGraph graph) : base(graph) { }

    public override void Execute(GdsCallOptions options)
    {
        int maxHops = options.MaxHops == int.MaxValue ? 3 : options.MaxHops;
        var seeds   = options.SourceNode.HasValue
            ? (IEnumerable<NodeId>)new[] { options.SourceNode.Value }
            : Graph.AllNodes();

        uint resultOffset = 0;
        foreach (var src in seeds)
        {
            // BFS from src
            var visited = new Dictionary<NodeId, int> { [src] = 0 };
            var queue   = new Queue<NodeId>();
            queue.Enqueue(src);

            while (queue.Count > 0)
            {
                var cur  = queue.Dequeue();
                int hops = visited[cur];
                if (hops >= maxHops) continue;

                foreach (var e in GetEdges(cur, options.Direction))
                {
                    var nbr = e.Source.Equals(cur) ? e.Target : e.Source;
                    if (!visited.ContainsKey(nbr))
                    {
                        visited[nbr] = hops + 1;
                        queue.Enqueue(nbr);
                    }
                }
            }

            foreach (var (target, hops) in visited)
            {
                if (target.Equals(src)) continue;
                // Use a synthetic NodeId for the result row key
                var rowKey = new NodeId(resultOffset++, 0);
                Results[rowKey] = new Dictionary<string, object?>
                {
                    ["node"]   = target.ToString(),
                    ["source"] = src.ToString(),
                    ["hops"]   = (long)hops,
                };
            }
        }
    }

    private IEnumerable<GdsEdge> GetEdges(NodeId n, string dir) => dir switch
    {
        "IN"   => Graph.GetInEdges(n),
        "BOTH" => Graph.GetBothEdges(n),
        _      => Graph.GetOutEdges(n),
    };
}

/// <summary>
/// Variable-Length Paths: enumerates all paths of length [minLen, maxLen] between node pairs.
/// C++ parity: <c>src/function/gds/variable_length_path.cpp</c>
///
/// Output: one row per path with columns: source, target, length, path (node ID list).
/// </summary>
public sealed class VariableLengthPathAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "variable_length_path";
    public override IReadOnlyList<string> OutputColumns { get; } =
        new[] { "source", "target", "length", "path" };

    private readonly int _minLen;

    public VariableLengthPathAlgorithm(IGraph graph, int minLen = 1) : base(graph)
    {
        _minLen = minLen;
    }

    public override void Execute(GdsCallOptions options)
    {
        int maxHops = options.MaxHops == int.MaxValue ? 3 : options.MaxHops;
        var seeds   = options.SourceNode.HasValue
            ? new[] { options.SourceNode.Value }
            : Graph.AllNodes().ToArray();

        uint resultOffset = 0;
        foreach (var src in seeds)
        {
            DfsEnumerate(src, src, new List<NodeId> { src }, 0, maxHops, ref resultOffset, options.Direction);
        }
    }

    private void DfsEnumerate(NodeId src, NodeId cur, List<NodeId> path,
        int depth, int maxDepth, ref uint resultOffset, string dir)
    {
        if (depth >= _minLen && depth <= maxDepth)
        {
            var target = path[^1];
            var rowKey = new NodeId(resultOffset++, 0);
            Results[rowKey] = new Dictionary<string, object?>
            {
                ["source"] = src.ToString(),
                ["target"] = target.ToString(),
                ["length"] = (long)depth,
                ["path"]   = string.Join("→", path.Select(n => n.ToString())),
            };
        }
        if (depth >= maxDepth) return;

        foreach (var e in GetEdges(cur, dir))
        {
            var nbr = e.Source.Equals(cur) ? e.Target : e.Source;
            // Avoid cycles (simple path semantics)
            if (path.Contains(nbr)) continue;
            path.Add(nbr);
            DfsEnumerate(src, nbr, path, depth + 1, maxDepth, ref resultOffset, dir);
            path.RemoveAt(path.Count - 1);
        }
    }

    private IEnumerable<GdsEdge> GetEdges(NodeId n, string dir) => dir switch
    {
        "IN"   => Graph.GetInEdges(n),
        "BOTH" => Graph.GetBothEdges(n),
        _      => Graph.GetOutEdges(n),
    };
}
