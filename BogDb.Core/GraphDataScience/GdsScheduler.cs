using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Drives parallel execution of a GDS algorithm using .NET Task parallelism and
/// morsel-based work distribution.
///
/// C++ parity: <c>GdsTask</c> + <c>FrontierTaskSharedState</c> in <c>gds_task.h</c>.
///
/// Architecture:
/// <code>
///   GdsScheduler
///     ├── FrontierMorselDispatcher  — atomically hands offset ranges to workers
///     ├── worker Task[maxThreads]   — each runs a tight morsel-consume loop
///     │     └── EdgeComputeDelegate — caller-supplied per-morsel compute function
///     └── barrier after each BFS level / iteration
/// </code>
///
/// Usage:
/// <code>
///   var sched = new GdsScheduler(graph.NodeCount, maxDegreeOfParallelism: 4);
///   sched.RunParallel((morsel, nodes) =>
///   {
///       for (ulong off = morsel.BeginOffset; off &lt; morsel.EndOffset; off++)
///       {
///           var nid = nodes[(int)off];
///           // compute and write to concurrent result store
///       }
///   });
/// </code>
/// </summary>
public sealed class GdsScheduler
{
    private readonly int   _maxThreads;
    private readonly ulong _nodeCount;

    /// <summary>Effective degree of parallelism (capped to available processors).</summary>
    public int MaxThreads => _maxThreads;

    public GdsScheduler(ulong nodeCount, int maxDegreeOfParallelism = 0)
    {
        _nodeCount = nodeCount;
        _maxThreads = maxDegreeOfParallelism <= 0
            ? Environment.ProcessorCount
            : Math.Min(maxDegreeOfParallelism, Environment.ProcessorCount);
    }

    // ── Primary parallel entry-point ───────────────────────────────────────────

    /// <summary>
    /// Distributes all node offsets [0, nodeCount) across worker tasks.
    /// <paramref name="computeAction"/> is called on each morsel from multiple threads concurrently.
    /// The method returns only after all workers finish.
    /// </summary>
    /// <param name="computeAction">
    /// (morsel, nodeList) → void.  Must be thread-safe for concurrent invocations.
    /// <paramref name="nodeList"/> is the full ordered node list (index = offset).
    /// </param>
    public void RunParallel(NodeList nodeList, Action<FrontierMorsel, NodeList> computeAction)
    {
        if (_nodeCount == 0) return;
        var dispatcher = new FrontierMorselDispatcher(_nodeCount, _maxThreads);

        var tasks = new Task[_maxThreads];
        for (int t = 0; t < _maxThreads; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                while (dispatcher.TryGetNext(out var morsel))
                    computeAction(morsel, nodeList);
            });
        }
        Task.WaitAll(tasks);
    }

    /// <summary>
    /// Runs one BFS level in parallel: for each active node in <paramref name="current"/>,
    /// expands edges and populates <paramref name="next"/> with newly discovered neighbours.
    /// Thread-safe via ConcurrentBag for next-frontier staging.
    ///
    /// Returns the number of new nodes added to the next frontier.
    /// </summary>
    public int RunBfsLevel(
        NodeList            nodeList,
        IGraph              graph,
        GdsFrontier         current,
        GdsFrontier         next,
        string              direction,
        Func<NodeId, NodeId, bool> shouldVisit,   // (src, nbr) → true if nbr should be added
        Action<NodeId, NodeId, int>  onVisit)      // (src, nbr, curIter)
    {
        var newlyFound = new ConcurrentBag<NodeId>();

        var dispatcher = new FrontierMorselDispatcher(_nodeCount, _maxThreads);
        var activeOffsets = current.ActiveOffsets().ToHashSet();

        var tasks = new Task[_maxThreads];
        for (int t = 0; t < _maxThreads; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                while (dispatcher.TryGetNext(out var morsel))
                {
                    for (ulong off = morsel.BeginOffset; off < morsel.EndOffset; off++)
                    {
                        if (!activeOffsets.Contains(off)) continue;
                        if (off >= (ulong)nodeList.Count)  continue;

                        var src = nodeList[(int)off];
                        foreach (var e in GetEdges(graph, src, direction))
                        {
                            var nbr = e.Source.Equals(src) ? e.Target : e.Source;
                            if (!shouldVisit(src, nbr)) continue;
                            onVisit(src, nbr, 0);
                            newlyFound.Add(nbr);
                        }
                    }
                }
            });
        }
        Task.WaitAll(tasks);
        return newlyFound.Count;
    }

    private static IEnumerable<GdsEdge> GetEdges(IGraph g, NodeId n, string dir) => dir switch
    {
        "IN"   => g.GetInEdges(n),
        "BOTH" => g.GetBothEdges(n),
        _      => g.GetOutEdges(n),
    };
}

/// <summary>
/// Indexed list of all graph nodes (index = storage offset within their table).
/// Provides O(1) access by offset, required by the morsel-based dispatcher.
/// </summary>
public sealed class NodeList
{
    private readonly NodeId[] _nodes;

    public int Count => _nodes.Length;

    public NodeId this[int index] => _nodes[index];

    /// <summary>
    /// Builds a <see cref="NodeList"/> by enumerating all nodes from the graph.
    /// The order matches the enumeration order of <see cref="IGraph.AllNodes"/>.
    /// </summary>
    public static NodeList From(IGraph graph) => new(graph.AllNodes().ToArray());

    private NodeList(NodeId[] nodes) => _nodes = nodes;

    public IEnumerable<NodeId> All() => _nodes;
}
