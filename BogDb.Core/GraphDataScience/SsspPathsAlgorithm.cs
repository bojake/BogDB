using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Single-Source Shortest Paths with full path reconstruction.
/// C++ parity: <c>ssp_paths.cpp</c>, <c>asp_paths.cpp</c>, <c>wsp_paths.cpp</c>
///
/// For each reachable destination, emits:
///   - node:     destination node ID
///   - distance: shortest distance from source
///   - path:     ordered list of node IDs from source → destination (inclusive)
///   - length:   number of hops (edges) in the path
///
/// Supports both unweighted (parallel BFS) and weighted (sequential Dijkstra)
/// graphs, with parent-pointer tracking for path reconstruction.
/// </summary>
public sealed class SsspPathsAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "sssp_paths";
    public override IReadOnlyList<string> OutputColumns { get; } = new[] { "node", "distance", "path", "length" };

    private readonly int _maxThreads;

    public SsspPathsAlgorithm(IGraph graph, int maxThreads = 0) : base(graph)
    {
        _maxThreads = maxThreads <= 0 ? Environment.ProcessorCount : maxThreads;
    }

    public override void Execute(GdsCallOptions options)
    {
        var nodes = NodeList.From(Graph);
        int n     = nodes.Count;
        if (n == 0) return;

        var idx = new Dictionary<NodeId, int>(n);
        for (int i = 0; i < n; i++) idx[nodes[i]] = i;

        var source = options.SourceNode ?? nodes[0];
        if (!idx.TryGetValue(source, out int srcIdx)) srcIdx = 0;

        bool weighted = Graph.WeightProperty != null;

        // parent[i] stores the predecessor index for path reconstruction
        // -1 = no predecessor (root or unreachable)
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = -1;

        double[] dist;

        if (weighted)
            dist = RunDijkstraWithPaths(nodes, idx, source, srcIdx, parent, options);
        else
            dist = RunBfsWithPaths(nodes, idx, srcIdx, parent, options);

        // Materialise results with reconstructed paths
        for (int i = 0; i < n; i++)
        {
            bool reachable = !double.IsPositiveInfinity(dist[i]) && dist[i] >= 0;
            List<string>? path = null;
            int hops = 0;

            if (reachable)
            {
                path = ReconstructPath(nodes, parent, srcIdx, i);
                hops = path.Count - 1; // edges = nodes - 1
            }

            Results[nodes[i]] = new Dictionary<string, object?>
            {
                ["node"]     = nodes[i].ToString(),
                ["distance"] = reachable ? (object?)dist[i] : null,
                ["path"]     = path != null ? new List<object?>(path.Cast<object?>()) : null,
                ["length"]   = path != null ? (object?)(long)hops : null,
            };
        }
    }

    // ── BFS (unweighted, parallel) ──────────────────────────────────────────────

    private double[] RunBfsWithPaths(
        NodeList nodes, Dictionary<NodeId, int> idx,
        int srcIdx, int[] parent, GdsCallOptions options)
    {
        int n = nodes.Count;

        // Use int[] for distances — enables atomic CAS via Interlocked.CompareExchange
        // -1 = unvisited
        var distInt = new int[n];
        for (int i = 0; i < n; i++) distInt[i] = -1;
        distInt[srcIdx] = 0;

        var frontier = new HashSet<int> { srcIdx };
        int level = 0;
        int maxHops = options.MaxHops == int.MaxValue ? int.MaxValue : options.MaxHops;

        while (frontier.Count > 0 && level < maxHops)
        {
            var nextFrontier = new ConcurrentBag<int>();
            var frontierArr  = frontier.ToArray();
            int fLen         = frontierArr.Length;

            var tasks = new System.Threading.Tasks.Task[_maxThreads];
            int step  = Math.Max(1, (int)Math.Ceiling((double)fLen / _maxThreads));

            for (int t = 0; t < _maxThreads; t++)
            {
                int start = t * step;
                int end   = Math.Min(start + step, fLen);
                if (start >= fLen) { tasks[t] = System.Threading.Tasks.Task.CompletedTask; continue; }

                int ts = start, te = end, lv = level;
                tasks[t] = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int fi = ts; fi < te; fi++)
                    {
                        int curIdx = frontierArr[fi];
                        var src = nodes[curIdx];
                        foreach (var e in GetEdges(src, options.Direction))
                        {
                            var nbr = e.Source.Equals(src) ? e.Target : e.Source;
                            if (!idx.TryGetValue(nbr, out int ni)) continue;

                            // CAS: only visit if still unvisited (-1)
                            if (Interlocked.CompareExchange(ref distInt[ni], lv + 1, -1) == -1)
                            {
                                // Won the race — set parent pointer
                                Volatile.Write(ref parent[ni], curIdx);
                                nextFrontier.Add(ni);
                            }
                        }
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);

            frontier = new HashSet<int>(nextFrontier);
            level++;
        }

        // Convert int distances to double for uniform API
        var dist = new double[n];
        for (int i = 0; i < n; i++)
            dist[i] = distInt[i] < 0 ? double.PositiveInfinity : (double)distInt[i];

        return dist;
    }

    // ── Dijkstra (weighted, sequential) ─────────────────────────────────────────

    private double[] RunDijkstraWithPaths(
        NodeList nodes, Dictionary<NodeId, int> idx,
        NodeId source, int srcIdx, int[] parent, GdsCallOptions options)
    {
        int n = nodes.Count;
        var dist = new double[n];
        for (int i = 0; i < n; i++) dist[i] = double.PositiveInfinity;
        dist[srcIdx] = 0;

        var pq = new SortedSet<(double, int)>(
            Comparer<(double, int)>.Create((a, b) =>
            {
                int c = a.Item1.CompareTo(b.Item1);
                return c != 0 ? c : a.Item2.CompareTo(b.Item2);
            }));
        pq.Add((0.0, srcIdx));

        while (pq.Count > 0)
        {
            var min = pq.Min; pq.Remove(min);
            double curD = min.Item1;
            int    curI = min.Item2;
            if (curD > dist[curI]) continue;

            foreach (var e in GetEdges(nodes[curI], options.Direction))
            {
                var nbr = e.Source.Equals(nodes[curI]) ? e.Target : e.Source;
                if (!idx.TryGetValue(nbr, out int ni)) continue;
                double nd = curD + e.Weight;
                if (nd < dist[ni])
                {
                    dist[ni] = nd;
                    parent[ni] = curI; // Track predecessor for path reconstruction
                    pq.Add((nd, ni));
                }
            }
        }

        return dist;
    }

    // ── Path Reconstruction ─────────────────────────────────────────────────────

    /// <summary>
    /// Reconstruct the shortest path from source to destination by following parent
    /// pointers back from the destination to the source, then reversing.
    /// Returns node IDs as strings in order: [source, ..., destination].
    /// </summary>
    private static List<string> ReconstructPath(NodeList nodes, int[] parent, int srcIdx, int dstIdx)
    {
        var path = new List<string>();
        int current = dstIdx;

        // Safety: max path length = number of nodes (prevents infinite loops on corrupted parent array)
        int maxLen = nodes.Count;
        int steps = 0;

        while (current != srcIdx && current >= 0 && steps++ < maxLen)
        {
            path.Add(nodes[current].ToString());
            current = parent[current];
        }

        // Add source
        if (current == srcIdx)
            path.Add(nodes[srcIdx].ToString());

        path.Reverse();
        return path;
    }

    private IEnumerable<GdsEdge> GetEdges(NodeId n, string dir) => dir switch
    {
        "IN"   => Graph.GetInEdges(n),
        "BOTH" => Graph.GetBothEdges(n),
        _      => Graph.GetOutEdges(n),
    };
}
