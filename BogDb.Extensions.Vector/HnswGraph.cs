using System;
using System.Collections.Generic;
using System.Linq;

namespace BogDb.Extensions.Vector;

/// <summary>
/// Hierarchical Navigable Small World (HNSW) graph index for approximate nearest neighbor search.
/// C++ parity: extension/vector/src/index/hnsw_graph.cpp + hnsw_index.cpp
///
/// Algorithm reference: Malkov & Yashunin, "Efficient and robust approximate nearest neighbor
/// using Hierarchical Navigable Small World graphs" (2018).
///
/// Key parameters:
///   M  — max connections per node per layer (default 16)
///   Mmax0 — max connections at layer 0 (default 2*M = 32)
///   efConstruction — search width during construction (default 200)
///   mL — level generation factor = 1/ln(M)
/// </summary>
internal sealed class HnswGraph
{
    private readonly int _M;           // max connections per layer > 0
    private readonly int _Mmax0;       // max connections at layer 0
    private readonly int _efConstruction;
    private readonly double _mL;       // level multiplier = 1/ln(M)
    private readonly Func<float[], float[], double> _distanceFunc;
    private readonly Random _rng = new();

    // Node storage: nodeId → HnswNode
    private readonly Dictionary<int, HnswNode> _nodes = new();

    // Entry point (highest-layer node)
    private int _entryPointId = -1;
    private int _maxLevel;

    private int _nextNodeId;

    public HnswGraph(
        int m = 16,
        int efConstruction = 200,
        Func<float[], float[], double>? distanceFunc = null)
    {
        _M = m;
        _Mmax0 = 2 * m;
        _efConstruction = efConstruction;
        _mL = 1.0 / Math.Log(m);
        _distanceFunc = distanceFunc ?? DefaultL2Distance;
    }

    public int Count => _nodes.Count;

    /// <summary>
    /// Insert a vector into the HNSW graph. Returns the internal node ID assigned.
    /// </summary>
    public int Insert(float[] vector, object? externalId = null)
    {
        var id = _nextNodeId++;
        var level = RandomLevel();
        var node = new HnswNode(id, vector, level, externalId);
        _nodes[id] = node;

        if (_entryPointId < 0)
        {
            // First node
            _entryPointId = id;
            _maxLevel = level;
            return id;
        }

        var ep = _entryPointId;

        // Phase 1: Greedily descend from top to node's level + 1
        for (var lc = _maxLevel; lc > level; lc--)
        {
            var nearest = GreedyClosest(vector, ep, lc);
            ep = nearest;
        }

        // Phase 2: Insert at each layer from min(level, _maxLevel) down to 0
        for (var lc = Math.Min(level, _maxLevel); lc >= 0; lc--)
        {
            var candidates = SearchLayer(vector, ep, _efConstruction, lc);
            var neighbors = SelectNeighbors(vector, candidates, lc == 0 ? _Mmax0 : _M);

            // Connect node → neighbors
            node.SetNeighbors(lc, neighbors.Select(n => n.NodeId).ToList());

            // Connect neighbors → node (bidirectional)
            var maxConn = lc == 0 ? _Mmax0 : _M;
            foreach (var neighbor in neighbors)
            {
                var nNode = _nodes[neighbor.NodeId];
                var nNeighbors = nNode.GetNeighbors(lc);
                nNeighbors.Add(id);

                if (nNeighbors.Count > maxConn)
                {
                    // Shrink the neighbor list by selecting best connections
                    var nCandidates = nNeighbors
                        .Select(nid => new SearchCandidate(nid, _distanceFunc(nNode.Vector, _nodes[nid].Vector)))
                        .ToList();
                    var selected = SelectNeighbors(nNode.Vector, nCandidates, maxConn);
                    nNode.SetNeighbors(lc, selected.Select(s => s.NodeId).ToList());
                }
            }

            if (candidates.Count > 0)
                ep = candidates.OrderBy(c => c.Distance).First().NodeId;
        }

        // Update entry point if new node has higher level
        if (level > _maxLevel)
        {
            _entryPointId = id;
            _maxLevel = level;
        }

        return id;
    }

    /// <summary>
    /// Search for the k nearest neighbors to the query vector.
    /// </summary>
    /// <param name="query">Query vector.</param>
    /// <param name="k">Number of results.</param>
    /// <param name="efSearch">Search beam width (higher = more accurate, slower). Defaults to efConstruction.</param>
    public List<SearchResult> Search(float[] query, int k, int efSearch = 0)
    {
        if (_entryPointId < 0 || _nodes.Count == 0)
            return new List<SearchResult>();

        if (efSearch <= 0) efSearch = Math.Max(k, _efConstruction);

        var ep = _entryPointId;

        // Greedily descend from top layer
        for (var lc = _maxLevel; lc > 0; lc--)
        {
            ep = GreedyClosest(query, ep, lc);
        }

        // Search at layer 0 with beam width efSearch
        var candidates = SearchLayer(query, ep, efSearch, 0);

        return candidates
            .OrderBy(c => c.Distance)
            .Take(k)
            .Select(c => new SearchResult(c.NodeId, c.Distance, _nodes[c.NodeId].ExternalId, _nodes[c.NodeId].Vector))
            .ToList();
    }

    // ── Core HNSW Algorithms ─────────────────────────────────────────────────

    /// <summary>
    /// Greedy search within a single layer to find the closest node to the query.
    /// </summary>
    private int GreedyClosest(float[] query, int entryNodeId, int layer)
    {
        var currentId = entryNodeId;
        var currentDist = _distanceFunc(query, _nodes[currentId].Vector);

        while (true)
        {
            var improved = false;
            foreach (var neighborId in _nodes[currentId].GetNeighbors(layer))
            {
                if (!_nodes.ContainsKey(neighborId)) continue;
                var dist = _distanceFunc(query, _nodes[neighborId].Vector);
                if (dist < currentDist)
                {
                    currentDist = dist;
                    currentId = neighborId;
                    improved = true;
                }
            }
            if (!improved) break;
        }

        return currentId;
    }

    /// <summary>
    /// Beam search within a layer. Returns the ef closest candidates found.
    /// </summary>
    private List<SearchCandidate> SearchLayer(float[] query, int entryNodeId, int ef, int layer)
    {
        var visited = new HashSet<int> { entryNodeId };
        var entryDist = _distanceFunc(query, _nodes[entryNodeId].Vector);

        // candidates: min-heap (closest first) for exploration
        var candidates = new SortedSet<SearchCandidate>(SearchCandidate.ByDistance)
        {
            new(entryNodeId, entryDist)
        };

        // results: max-heap (farthest first) for result set management
        var results = new SortedSet<SearchCandidate>(SearchCandidate.ByDistanceDescending)
        {
            new(entryNodeId, entryDist)
        };

        while (candidates.Count > 0)
        {
            var closest = candidates.Min!;
            var farthestResult = results.Max!;

            if (closest.Distance > farthestResult.Distance)
                break;

            candidates.Remove(closest);

            foreach (var neighborId in _nodes[closest.NodeId].GetNeighbors(layer))
            {
                if (!visited.Add(neighborId)) continue;
                if (!_nodes.ContainsKey(neighborId)) continue;

                var dist = _distanceFunc(query, _nodes[neighborId].Vector);
                farthestResult = results.Max!;

                if (dist < farthestResult.Distance || results.Count < ef)
                {
                    var candidate = new SearchCandidate(neighborId, dist);
                    candidates.Add(candidate);
                    results.Add(candidate);

                    if (results.Count > ef)
                        results.Remove(results.Max!);
                }
            }
        }

        return results.ToList();
    }

    /// <summary>
    /// Simple greedy neighbor selection (equivalent to SELECT-NEIGHBORS-SIMPLE in the paper).
    /// </summary>
    private static List<SearchCandidate> SelectNeighbors(
        float[] nodeVector,
        List<SearchCandidate> candidates,
        int maxCount)
    {
        return candidates
            .OrderBy(c => c.Distance)
            .Take(maxCount)
            .ToList();
    }

    private int RandomLevel()
    {
        var r = _rng.NextDouble();
        return (int)Math.Floor(-Math.Log(r) * _mL);
    }

    private static double DefaultL2Distance(float[] a, float[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }
        return sum; // squared L2 for performance (monotonic, skips sqrt)
    }

    // ── Inner Types ──────────────────────────────────────────────────────────

    internal sealed class HnswNode
    {
        public int Id { get; }
        public float[] Vector { get; }
        public int Level { get; }
        public object? ExternalId { get; }

        // Adjacency lists per layer
        private readonly List<int>[] _neighbors;

        public HnswNode(int id, float[] vector, int level, object? externalId)
        {
            Id = id;
            Vector = vector;
            Level = level;
            ExternalId = externalId;
            _neighbors = new List<int>[level + 1];
            for (var i = 0; i <= level; i++)
                _neighbors[i] = new List<int>();
        }

        public List<int> GetNeighbors(int layer)
            => layer < _neighbors.Length ? _neighbors[layer] : new List<int>();

        public void SetNeighbors(int layer, List<int> neighbors)
        {
            if (layer < _neighbors.Length)
                _neighbors[layer] = neighbors;
        }
    }

    internal readonly record struct SearchCandidate(int NodeId, double Distance)
    {
        public static readonly IComparer<SearchCandidate> ByDistance =
            Comparer<SearchCandidate>.Create((a, b) =>
            {
                var cmp = a.Distance.CompareTo(b.Distance);
                return cmp != 0 ? cmp : a.NodeId.CompareTo(b.NodeId);
            });

        public static readonly IComparer<SearchCandidate> ByDistanceDescending =
            Comparer<SearchCandidate>.Create((a, b) =>
            {
                var cmp = b.Distance.CompareTo(a.Distance);
                return cmp != 0 ? cmp : b.NodeId.CompareTo(a.NodeId);
            });
    }

    internal sealed record SearchResult(int NodeId, double Distance, object? ExternalId, float[] Vector);
}
