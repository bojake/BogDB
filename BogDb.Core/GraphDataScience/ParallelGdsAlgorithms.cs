using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Parallel PageRank algorithm using morsel-based work distribution.
///
/// C++ parity: <c>src/function/gds/</c> (generic vertex compute pattern)
///
/// Each iteration is split into two parallel phases:
///   1. <b>Scatter</b>: divide all nodes into morsels; workers compute their node's
///      contribution (rank/outdegree) and scatter it into a per-node atomic accumulator.
///   2. <b>Gather</b>: divide nodes into morsels; workers read the accumulator and
///      write the final new rank for each node.
///
/// Convergence check runs sequentially after gather (max-delta over all nodes).
/// </summary>
public sealed class ParallelPageRankAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "pagerank";
    public override IReadOnlyList<string> OutputColumns { get; } = new[] { "node", "rank" };

    private readonly int _maxThreads;

    public ParallelPageRankAlgorithm(IGraph graph, int maxThreads = 0) : base(graph)
    {
        _maxThreads = maxThreads <= 0 ? Environment.ProcessorCount : maxThreads;
    }

    public override void Execute(GdsCallOptions options)
    {
        var nodes  = NodeList.From(Graph);
        int n      = nodes.Count;
        if (n == 0) return;

        double d     = options.DampingFactor;
        double baseR = (1.0 - d) / n;

        // Initial uniform rank array (index = node list position)
        var rank    = new double[n];
        var newRank = new double[n];
        for (int i = 0; i < n; i++) rank[i] = 1.0 / n;

        // Pre-cache in-degree arrays from node list indices
        var outDeg = new long[n];
        for (int i = 0; i < n; i++)
            outDeg[i] = Graph.OutDegree(nodes[i]);

        // Build idx lookup: NodeId → position in nodes array
        var idx = new Dictionary<NodeId, int>(n);
        for (int i = 0; i < n; i++) idx[nodes[i]] = i;

        var sched = new GdsScheduler((ulong)n, _maxThreads);

        for (int iter = 0; iter < options.MaxIterations; iter++)
        {
            // ── Phase 1: gather inbound contributions into atomicInSum (as long bits) ───
            var inSum = new double[n];  // written atomically via Interlocked

            sched.RunParallel(nodes, (morsel, nl) =>
            {
                for (ulong off = morsel.BeginOffset; off < morsel.EndOffset; off++)
                {
                    var v    = nl[(int)off];
                    int vidx = (int)off;

                    foreach (var e in Graph.GetInEdges(v))
                    {
                        if (!idx.TryGetValue(e.Source, out int uidx)) continue;
                        if (outDeg[uidx] == 0) continue;
                        double contrib = rank[uidx] / outDeg[uidx];
                        // Use Interlocked CAS to add atomically into inSum[vidx]
                        AddDouble(ref inSum[vidx], contrib);
                    }
                }
            });

            // Dangling mass (out-degree 0 → distribute evenly)
            double danglingMass = 0;
            for (int i = 0; i < n; i++)
                if (outDeg[i] == 0) danglingMass += rank[i];
            double danglingPerNode = d * danglingMass / n;

            // ── Phase 2: compute new ranks in parallel ─────────────────────────────────
            double[] delta = new double[_maxThreads];
            int threadIdx  = -1;

            sched.RunParallel(nodes, (morsel, _) =>
            {
                int myThread = Math.Abs(Interlocked.Increment(ref threadIdx) % _maxThreads);
                double localDelta = 0;
                for (ulong off = morsel.BeginOffset; off < morsel.EndOffset; off++)
                {
                    int i       = (int)off;
                    double nr   = baseR + d * inSum[i] + danglingPerNode;
                    localDelta  = Math.Max(localDelta, Math.Abs(nr - rank[i]));
                    newRank[i]  = nr;
                }
                // fold into thread-local delta array (avoid Interlocked on double)
                if (localDelta > delta[myThread]) delta[myThread] = localDelta;
            });

            // Reset thread index for next iteration
            Interlocked.Exchange(ref threadIdx, -1);

            Array.Copy(newRank, rank, n);
            if (delta.Max() < options.Tolerance) break;
        }

        // Materialise results
        for (int i = 0; i < n; i++)
        {
            Results[nodes[i]] = new Dictionary<string, object?>
            {
                ["node"] = nodes[i].ToString(),
                ["rank"] = rank[i],
            };
        }
    }

    // Thread-safe double addition via Compare-And-Swap loop
    private static void AddDouble(ref double location, double value)
    {
        double current;
        do
        {
            current = location;
        } while (Interlocked.CompareExchange(
                     ref location,
                     current + value,
                     current) != current);
    }
}

/// <summary>
/// Parallel Weakly-Connected-Components via concurrent union-find.
///
/// Each worker claims node-range morsels and calls Union(src, dst) over all
/// outgoing + incoming edges. `Find` uses path compression with
/// <c>Interlocked.CompareExchange</c> to safely race-merge parent pointers.
///
/// C++ parity: parallel label-propagation WCC pattern from <c>gds_task.h</c>.
/// </summary>
public sealed class ParallelWccAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "wcc";
    public override IReadOnlyList<string> OutputColumns { get; } = new[] { "node", "component_id" };

    private readonly int _maxThreads;

    public ParallelWccAlgorithm(IGraph graph, int maxThreads = 0) : base(graph)
    {
        _maxThreads = maxThreads <= 0 ? Environment.ProcessorCount : maxThreads;
    }

    public override void Execute(GdsCallOptions options)
    {
        var nodes = NodeList.From(Graph);
        int n     = nodes.Count;
        if (n == 0) return;

        // parent[i] = i initially (each node is its own root)
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        // Build idx map
        var idx = new Dictionary<NodeId, int>(n);
        for (int i = 0; i < n; i++) idx[nodes[i]] = i;

        int Find(int x)
        {
            while (true)
            {
                int p = Volatile.Read(ref parent[x]);
                if (p == x) return x;
                // Path halving (safe without locks)
                int gp = Volatile.Read(ref parent[p]);
                Interlocked.CompareExchange(ref parent[x], gp, p);
                x = gp;
            }
        }

        void Union(int a, int b)
        {
            while (true)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra == rb) return;
                // Always merge larger index into smaller (keeps min-label)
                int lo = Math.Min(ra, rb);
                int hi = Math.Max(ra, rb);
                if (Interlocked.CompareExchange(ref parent[hi], lo, hi) == hi) return;
                // CAS failed — another thread changed parent[hi]; retry
            }
        }

        var sched = new GdsScheduler((ulong)n, _maxThreads);

        // Parallel union over all edges
        sched.RunParallel(nodes, (morsel, nl) =>
        {
            for (ulong off = morsel.BeginOffset; off < morsel.EndOffset; off++)
            {
                var src = nl[(int)off];
                if (!idx.TryGetValue(src, out int si)) continue;

                foreach (var e in Graph.GetOutEdges(src))
                {
                    if (idx.TryGetValue(e.Target, out int di)) Union(si, di);
                }
                foreach (var e in Graph.GetInEdges(src))
                {
                    if (idx.TryGetValue(e.Source, out int di)) Union(si, di);
                }
            }
        });

        // Materialise: compress all parent pointers and emit result rows
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            Results[nodes[i]] = new Dictionary<string, object?>
            {
                ["node"]         = nodes[i].ToString(),
                ["component_id"] = (long)root,
            };
        }
    }
}

/// <summary>
/// Parallel BFS-based Single-Source Shortest Paths (unweighted) / Dijkstra (weighted).
///
/// For <b>unweighted</b> graphs: level-synchronous parallel BFS.
///   Each BFS level expands the current frontier in parallel using morsel dispatch.
///   The next frontier is accumulated via a <see cref="ConcurrentDictionary"/>.
///
/// For <b>weighted</b> graphs: falls back to sequential Dijkstra (SSSP with a
/// priority queue is difficult to parallelise correctly without delta-stepping;
/// a correct parallel Δ-stepping implementation would be a separate extension).
///
/// C++ parity: <c>ssp_destinations.cpp</c> parallel frontier expansion pattern.
/// </summary>
public sealed class ParallelSsspAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "sssp";
    public override IReadOnlyList<string> OutputColumns { get; } = new[] { "node", "distance" };

    private readonly int _maxThreads;

    public ParallelSsspAlgorithm(IGraph graph, int maxThreads = 0) : base(graph)
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

        if (weighted)
            RunSequentialDijkstra(nodes, idx, source, options);
        else
            RunParallelBfs(nodes, idx, srcIdx, options);
    }

    private void RunParallelBfs(NodeList nodes, Dictionary<NodeId, int> idx,
        int srcIdx, GdsCallOptions options)
    {
        int n     = nodes.Count;
        var dist  = new int[n];
        for (int i = 0; i < n; i++) dist[i] = -1;   // -1 = unvisited
        dist[srcIdx] = 0;

        // Current frontier: set of node list indices
        var frontier = new HashSet<int> { srcIdx };
        int level    = 0;
        int maxHops  = options.MaxHops == int.MaxValue ? int.MaxValue : options.MaxHops;

        var sched = new GdsScheduler((ulong)frontier.Count, _maxThreads);

        while (frontier.Count > 0 && level < maxHops)
        {
            var nextFrontier = new ConcurrentBag<int>();
            var frontierArr  = frontier.ToArray();  // snapshot for parallel iteration
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
                        var src = nodes[frontierArr[fi]];
                        foreach (var e in GetEdges(src, options.Direction))
                        {
                            var nbr = e.Source.Equals(src) ? e.Target : e.Source;
                            if (!idx.TryGetValue(nbr, out int ni)) continue;
                            // CAS: only visit if still unvisited
                            if (Interlocked.CompareExchange(ref dist[ni], lv + 1, -1) == -1)
                                nextFrontier.Add(ni);
                        }
                    }
                });
            }
            System.Threading.Tasks.Task.WaitAll(tasks);

            frontier = new HashSet<int>(nextFrontier);
            level++;
        }

        // Materialise
        for (int i = 0; i < n; i++)
        {
            Results[nodes[i]] = new Dictionary<string, object?>
            {
                ["node"]     = nodes[i].ToString(),
                ["distance"] = dist[i] < 0 ? null : (object?)(double)dist[i],
            };
        }
    }

    private void RunSequentialDijkstra(NodeList nodes, Dictionary<NodeId, int> idx,
        NodeId source, GdsCallOptions options)
    {
        int n    = nodes.Count;
        var dist = new double[n];
        for (int i = 0; i < n; i++) dist[i] = double.PositiveInfinity;
        if (idx.TryGetValue(source, out int si)) dist[si] = 0;

        var pq = new SortedSet<(double, int)>(
            Comparer<(double, int)>.Create((a, b) =>
            {
                int c = a.Item1.CompareTo(b.Item1);
                return c != 0 ? c : a.Item2.CompareTo(b.Item2);
            }));
        pq.Add((0.0, si));

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
                if (nd < dist[ni]) { dist[ni] = nd; pq.Add((nd, ni)); }
            }
        }

        for (int i = 0; i < n; i++)
        {
            Results[nodes[i]] = new Dictionary<string, object?>
            {
                ["node"]     = nodes[i].ToString(),
                ["distance"] = double.IsPositiveInfinity(dist[i]) ? null : (object?)dist[i],
            };
        }
    }

    private IEnumerable<GdsEdge> GetEdges(NodeId n, string dir) => dir switch
    {
        "IN"   => Graph.GetInEdges(n),
        "BOTH" => Graph.GetBothEdges(n),
        _      => Graph.GetOutEdges(n),
    };
}
