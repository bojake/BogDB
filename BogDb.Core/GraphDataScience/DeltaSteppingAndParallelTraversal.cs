using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Parallel Δ-stepping Single-Source Shortest Paths for weighted graphs.
///
/// Δ-stepping (Meyer &amp; Sanders 2003) partitions tentative distances into
/// fixed-width "buckets" of width Δ. Light edges (weight ≤ Δ) are relaxed
/// in parallel within the current bucket; heavy edges (weight &gt; Δ) are
/// deferred and relaxed once the bucket is finalized. This creates natural
/// parallelism within each bucket-level pass.
///
/// C++ context: C++ BogDb uses sequential Dijkstra within its GDS morsel
/// tasks for weighted graphs.  BogDB goes one step further with true
/// parallel Δ-stepping.
///
/// Algorithm outline:
/// <code>
///   Δ = auto-computed as average edge weight / max per-node degree
///   B[i] = set of nodes u with dist[u] ∈ [i*Δ, (i+1)*Δ)
///
///   while any B[i] non-empty:
///       i = index of smallest non-empty bucket
///       R = {}    // nodes settled from this bucket
///       while B[i] non-empty:
///           parallel relax light edges from B[i] → updates B[i] and B[j] (j≥i)
///           R ← R ∪ B[i]; B[i] = {}
///       parallel relax heavy edges from R → updates B[j] (j>i)
///       i++
/// </code>
/// </summary>
public sealed class DeltaSteppingSsspAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "sssp_delta";
    public override IReadOnlyList<string> OutputColumns { get; } = new[] { "node", "distance" };

    private readonly int _maxThreads;

    public DeltaSteppingSsspAlgorithm(IGraph graph, int maxThreads = 0) : base(graph)
    {
        _maxThreads = maxThreads <= 0 ? Environment.ProcessorCount : maxThreads;
    }

    public override void Execute(GdsCallOptions options)
    {
        var nl  = NodeList.From(Graph);
        int n   = nl.Count;
        if (n == 0) return;

        var idx = new Dictionary<NodeId, int>(n);
        for (int i = 0; i < n; i++) idx[nl[i]] = i;

        var src = options.SourceNode ?? nl[0];
        if (!idx.TryGetValue(src, out int si)) si = 0;

        // ── Choose Δ ──────────────────────────────────────────────────────────
        // Heuristic: Δ = average_weight / max_degree (Meyer & Sanders recommend Δ = 1/max_degree)
        double delta = ComputeDelta(nl, idx, options.Direction);

        // ── Distance array ────────────────────────────────────────────────────
        var dist = new double[n];
        for (int i = 0; i < n; i++) dist[i] = double.PositiveInfinity;
        dist[si] = 0.0;

        int  maxBuckets = (int)(n * 1.5) + 1;
        // Buckets: ConcurrentBag per bucket index
        var buckets = new ConcurrentBag<int>[maxBuckets];
        for (int i = 0; i < maxBuckets; i++) buckets[i] = new ConcurrentBag<int>();
        buckets[0].Add(si);

        int maxHops = options.MaxHops == int.MaxValue ? n : options.MaxHops;

        // ── Main Δ-stepping loop ──────────────────────────────────────────────
        int bucketIdx = 0;
        while (bucketIdx < maxBuckets)
        {
            // Find next non-empty bucket
            while (bucketIdx < maxBuckets && buckets[bucketIdx].IsEmpty)
                bucketIdx++;
            if (bucketIdx >= maxBuckets) break;

            // R = set of nodes settled this bucket phase
            var settled = new ConcurrentBag<int>();

            // Inner loop: keep relaxing light edges within this bucket until stable
            while (!buckets[bucketIdx].IsEmpty)
            {
                // Snapshot and clear bucket
                var toRelax = buckets[bucketIdx].ToArray();
                buckets[bucketIdx] = new ConcurrentBag<int>();

                foreach (var x in toRelax) settled.Add(x);

                // Parallel light-edge relaxation
                var partitions = Partition(toRelax, _maxThreads);
                var tasks = partitions.Select(part => System.Threading.Tasks.Task.Run(() =>
                {
                    foreach (int ui in part)
                    {
                        var u = nl[ui];
                        foreach (var e in GetEdges(u, options.Direction))
                        {
                            if (e.Weight > delta) continue;  // heavy edge — skip
                            var v = e.Source.Equals(u) ? e.Target : e.Source;
                            if (!idx.TryGetValue(v, out int vi)) continue;
                            TryRelax(dist, buckets, maxBuckets, delta, ui, vi, dist[ui] + e.Weight);
                        }
                    }
                })).ToArray();
                System.Threading.Tasks.Task.WaitAll(tasks);
            }

            // Parallel heavy-edge relaxation from settled set
            var settledArr = settled.ToArray();
            var heavyTasks = Partition(settledArr, _maxThreads).Select(part =>
                System.Threading.Tasks.Task.Run(() =>
                {
                    foreach (int ui in part)
                    {
                        var u = nl[ui];
                        foreach (var e in GetEdges(u, options.Direction))
                        {
                            if (e.Weight <= delta) continue;  // light edge — already done
                            var v = e.Source.Equals(u) ? e.Target : e.Source;
                            if (!idx.TryGetValue(v, out int vi)) continue;
                            TryRelax(dist, buckets, maxBuckets, delta, ui, vi, dist[ui] + e.Weight);
                        }
                    }
                })).ToArray();
            System.Threading.Tasks.Task.WaitAll(heavyTasks);

            bucketIdx++;
        }

        // ── Materialise results ────────────────────────────────────────────────
        for (int i = 0; i < n; i++)
        {
            Results[nl[i]] = new Dictionary<string, object?>
            {
                ["node"]     = nl[i].ToString(),
                ["distance"] = double.IsPositiveInfinity(dist[i]) ? null : (object?)dist[i],
            };
        }
    }

    // ── Thread-safe relaxation ─────────────────────────────────────────────────

    private static void TryRelax(double[] dist, ConcurrentBag<int>[] buckets,
        int maxBuckets, double delta, int srcIdx, int dstIdx, double newDist)
    {
        // CAS loop: update dist[dstIdx] if newDist < current
        while (true)
        {
            double cur = Volatile.Read(ref dist[dstIdx]);
            if (newDist >= cur) return;  // no improvement
            if (Interlocked.CompareExchange(ref dist[dstIdx], newDist, cur) == cur)
            {
                // Successfully updated — insert into the correct bucket
                int bi = Math.Min((int)(newDist / delta), maxBuckets - 1);
                buckets[bi].Add(dstIdx);
                return;
            }
            // Another thread updated dist concurrently — retry
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private double ComputeDelta(NodeList nl, Dictionary<NodeId, int> idx, string dir)
    {
        double totalWeight = 0;
        long   totalEdges  = 0;
        long   maxDeg      = 1;

        foreach (var n in nl.All())
        {
            long deg = 0;
            foreach (var e in GetEdges(n, dir))
            {
                totalWeight += e.Weight;
                totalEdges++;
                deg++;
            }
            if (deg > maxDeg) maxDeg = deg;
        }

        double avgWeight = totalEdges > 0 ? totalWeight / totalEdges : 1.0;
        return Math.Max(avgWeight / maxDeg, 1e-9);
    }

    private static IEnumerable<int[]> Partition(int[] arr, int parts)
    {
        int size = Math.Max(1, (int)Math.Ceiling((double)arr.Length / parts));
        for (int i = 0; i < arr.Length; i += size)
            yield return arr[i..Math.Min(i + size, arr.Length)];
    }

    private IEnumerable<GdsEdge> GetEdges(NodeId n, string dir) => dir switch
    {
        "IN"   => Graph.GetInEdges(n),
        "BOTH" => Graph.GetBothEdges(n),
        _      => Graph.GetOutEdges(n),
    };
}

/// <summary>
/// Parallel K-Hop Reachability: each source node's BFS is independent,
/// so all seeds are dispatched concurrently via <c>Parallel.ForEach</c>.
///
/// Results are written into a <see cref="ConcurrentDictionary"/> keyed by
/// a synthetic row offset to avoid cross-thread contention.
/// </summary>
public sealed class ParallelKHopAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "k_hop";
    public override IReadOnlyList<string> OutputColumns { get; } = new[] { "node", "source", "hops" };

    private readonly int _maxThreads;

    public ParallelKHopAlgorithm(IGraph graph, int maxThreads = 0) : base(graph)
    {
        _maxThreads = maxThreads <= 0 ? Environment.ProcessorCount : maxThreads;
    }

    public override void Execute(GdsCallOptions options)
    {
        int maxHops = options.MaxHops == int.MaxValue ? 3 : options.MaxHops;
        var seeds   = options.SourceNode.HasValue
            ? (IReadOnlyList<NodeId>)new[] { options.SourceNode.Value }
            : Graph.AllNodes().ToArray();
        string dir  = options.Direction;

        // Thread-safe result accumulator: synthetic row key → result dict
        var concurrent = new ConcurrentDictionary<ulong, Dictionary<string, object?>>();
        long rowCounter = 0;

        var parallelOpts = new System.Threading.Tasks.ParallelOptions
        {
            MaxDegreeOfParallelism = _maxThreads
        };

        System.Threading.Tasks.Parallel.ForEach(seeds, parallelOpts, src =>
        {
            // BFS from this source (fully independent)
            var visited = new Dictionary<NodeId, int> { [src] = 0 };
            var queue   = new Queue<NodeId>();
            queue.Enqueue(src);

            while (queue.Count > 0)
            {
                var cur  = queue.Dequeue();
                int hops = visited[cur];
                if (hops >= maxHops) continue;

                foreach (var e in GetEdges(cur, dir))
                {
                    var nbr = e.Source.Equals(cur) ? e.Target : e.Source;
                    if (visited.ContainsKey(nbr)) continue;
                    visited[nbr] = hops + 1;
                    queue.Enqueue(nbr);
                }
            }

            // Write results — each thread uses Interlocked.Increment for unique row keys
            foreach (var (target, hops) in visited)
            {
                if (target.Equals(src)) continue;
                ulong rowKey = (ulong)Interlocked.Increment(ref rowCounter);
                concurrent[rowKey] = new Dictionary<string, object?>
                {
                    ["node"]   = target.ToString(),
                    ["source"] = src.ToString(),
                    ["hops"]   = (long)hops,
                };
            }
        });

        // Flush into base Results
        foreach (var (k, v) in concurrent)
            Results[new NodeId(k, uint.MaxValue)] = v;
    }

    private IEnumerable<GdsEdge> GetEdges(NodeId n, string dir) => dir switch
    {
        "IN"   => Graph.GetInEdges(n),
        "BOTH" => Graph.GetBothEdges(n),
        _      => Graph.GetOutEdges(n),
    };
}

/// <summary>
/// Parallel Variable-Length Path enumeration: each source node's DFS path
/// enumeration is fully independent, making this embarrassingly parallel.
///
/// Results from all seeds are merged via a <see cref="ConcurrentDictionary"/>.
/// </summary>
public sealed class ParallelVariableLengthPathAlgorithm : GdsAlgorithm
{
    public override string AlgorithmName => "variable_length_path";
    public override IReadOnlyList<string> OutputColumns { get; } =
        new[] { "source", "target", "length", "path" };

    private readonly int _minLen;
    private readonly int _maxThreads;

    public ParallelVariableLengthPathAlgorithm(IGraph graph, int minLen = 1, int maxThreads = 0)
        : base(graph)
    {
        _minLen     = minLen;
        _maxThreads = maxThreads <= 0 ? Environment.ProcessorCount : maxThreads;
    }

    public override void Execute(GdsCallOptions options)
    {
        int maxHops = options.MaxHops == int.MaxValue ? 3 : options.MaxHops;
        var seeds   = options.SourceNode.HasValue
            ? (IReadOnlyList<NodeId>)new[] { options.SourceNode.Value }
            : Graph.AllNodes().ToArray();
        string dir  = options.Direction;

        var concurrent = new ConcurrentDictionary<ulong, Dictionary<string, object?>>();
        long rowCounter = 0;

        var parallelOpts = new System.Threading.Tasks.ParallelOptions
        {
            MaxDegreeOfParallelism = _maxThreads
        };

        System.Threading.Tasks.Parallel.ForEach(seeds, parallelOpts, src =>
        {
            // Thread-local path list (avoids allocations in DFS stack)
            var path = new List<NodeId> { src };
            DfsEnumerate(src, src, path, 0, maxHops, dir, concurrent, ref rowCounter);
        });

        foreach (var (k, v) in concurrent)
            Results[new NodeId(k, uint.MaxValue)] = v;
    }

    private void DfsEnumerate(NodeId src, NodeId cur, List<NodeId> path,
        int depth, int maxDepth, string dir,
        ConcurrentDictionary<ulong, Dictionary<string, object?>> sink,
        ref long rowCounter)
    {
        if (depth >= _minLen && depth <= maxDepth)
        {
            var target = path[^1];
            ulong rowKey = (ulong)Interlocked.Increment(ref rowCounter);
            sink[rowKey] = new Dictionary<string, object?>
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
            if (path.Contains(nbr)) continue;  // simple path semantics
            path.Add(nbr);
            DfsEnumerate(src, nbr, path, depth + 1, maxDepth, dir, sink, ref rowCounter);
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
