using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Main;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Adapts the BogDB in-memory storage model (<see cref="BogDatabase"/>
/// <c>NodeTables</c> / <c>RelTables</c>) to the <see cref="IGraph"/> interface
/// consumed by GDS algorithms.
///
/// C++ parity: <c>src/graph/on_disk_graph.cpp</c> — provides identical
/// adjacency iterators over the in-memory storage instead of the B+-tree pages.
///
/// Thread safety: read-only; safe for concurrent enumeration provided no
/// concurrent writes occur on the underlying tables.
/// </summary>
internal sealed class GraphAdapter : IGraph
{
    // ── Raw storage references ─────────────────────────────────────────────

    private readonly Dictionary<string, NodeTableData> _nodeTables;
    private readonly Dictionary<string, RelTableData>  _relTables;
    private readonly BogDb.Core.Transaction.Transaction? _tx;

    // ── Cached index: nodeId (string "table:offset") → NodeId ───────────────
    private readonly Dictionary<string, NodeId> _nodeIndex;

    // ── Adj list cached on first access ──────────────────────────────────────
    // out-adj: source NodeId → list of GdsEdge
    private readonly Dictionary<NodeId, List<GdsEdge>> _outAdj;
    // in-adj:  destination NodeId → list of GdsEdge
    private readonly Dictionary<NodeId, List<GdsEdge>> _inAdj;

    private bool _adjBuilt;

    /// <inheritdoc/>
    public string? WeightProperty { get; }

    /// <inheritdoc/>
    public long NodeCount => _nodeIndex.Count;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <param name="nodeTables">Read-only node table reference from <see cref="BogDatabase"/>.</param>
    /// <param name="relTables">Read-only relationship table reference.</param>
    /// <param name="weightProperty">Optional edge weight property name (null = unweighted, weight = 1.0).</param>
    public GraphAdapter(
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData>  relTables,
        BogDb.Core.Transaction.Transaction? tx = null,
        string? weightProperty = null)
    {
        _nodeTables    = nodeTables;
        _relTables     = relTables;
        _tx            = tx;
        WeightProperty = weightProperty;
        _nodeIndex     = new Dictionary<string, NodeId>(StringComparer.Ordinal);
        _outAdj        = new Dictionary<NodeId, List<GdsEdge>>();
        _inAdj         = new Dictionary<NodeId, List<GdsEdge>>();

        BuildNodeIndex();
    }

    public GraphAdapter(
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables,
        string? weightProperty)
        : this(nodeTables, relTables, tx: null, weightProperty)
    {
    }

    // ── Node index ────────────────────────────────────────────────────────────

    private void BuildNodeIndex()
    {
        uint tableId = 0;
        foreach (var (_, tableData) in _nodeTables)
        {
            ulong offset = 0;
            var rows = _tx is null ? tableData.EnumerateRows() : tableData.EnumerateRows(_tx);
            foreach (var (_, _) in rows)
            {
                var nid = new NodeId(offset, tableId);
                _nodeIndex[nid.ToString()] = nid;
                offset++;
            }
            tableId++;
        }
    }

    // ── Adjacency (lazy build) ────────────────────────────────────────────────

    private void EnsureAdj()
    {
        if (_adjBuilt) return;
        _adjBuilt = true;

        // Pre-seed all nodes so isolated nodes appear in out/in-adj too
        foreach (var nid in AllNodes())
        {
            if (!_outAdj.ContainsKey(nid)) _outAdj[nid] = new List<GdsEdge>();
            if (!_inAdj.ContainsKey(nid))  _inAdj[nid]  = new List<GdsEdge>();
        }

        // Build table-id → node-offset map to resolve "from"/"to" keys
        var tableNames = _nodeTables.Keys.ToArray();

        foreach (var (_, relTableData) in _relTables)
        {
            var relRows = _tx is null ? relTableData.EnumerateRows() : relTableData.EnumerateRows(_tx);
            foreach (var (edgeKey, props) in relRows)
            {
                var srcId = ResolveNodeId(edgeKey.From?.ToString(), tableNames);
                var dstId = ResolveNodeId(edgeKey.To?.ToString(), tableNames);
                if (srcId == null || dstId == null) continue;

                double weight = 1.0;
                if (WeightProperty != null && props.TryGetValue(WeightProperty, out var wv))
                    weight = TryDouble(wv, out double d) ? d : 1.0;

                var edge = new GdsEdge(srcId.Value, dstId.Value, weight,
                    (IReadOnlyDictionary<string, object?>)props);

                if (!_outAdj.TryGetValue(srcId.Value, out var outList))
                    _outAdj[srcId.Value] = outList = new List<GdsEdge>();
                outList.Add(edge);

                if (!_inAdj.TryGetValue(dstId.Value, out var inList))
                    _inAdj[dstId.Value] = inList = new List<GdsEdge>();
                inList.Add(edge);

                // Ensure isolated-partner nodes are seeded
                if (!_outAdj.ContainsKey(dstId.Value)) _outAdj[dstId.Value] = new List<GdsEdge>();
                if (!_inAdj.ContainsKey(srcId.Value))  _inAdj[srcId.Value]  = new List<GdsEdge>();
            }
        }
    }

    /// <summary>
    /// Resolves a node-id string ("tableName:id" or just an integer) to a <see cref="NodeId"/>.
    /// </summary>
    private NodeId? ResolveNodeId(string? raw, string[] tableNames)
    {
        if (raw == null) return null;

        // Try "tableId:offset" numeric form first
        var colon = raw.IndexOf(':');
        if (colon > 0)
        {
            if (uint.TryParse(raw[..colon],  out uint tid) &&
                ulong.TryParse(raw[(colon+1)..], out ulong off))
                return new NodeId(off, tid);
        }

        // Try lookup by string key directly
        if (_nodeIndex.TryGetValue(raw, out var found)) return found;

        // Try matching as a long offset in table 0
        if (ulong.TryParse(raw, out ulong offset))
            return new NodeId(offset, 0);

        return null;
    }

    // ── IGraph ────────────────────────────────────────────────────────────────

    public IEnumerable<NodeId> AllNodes()
    {
        uint tableId = 0;
        foreach (var (_, tableData) in _nodeTables)
        {
            ulong offset = 0;
            var rows = _tx is null ? tableData.EnumerateRows() : tableData.EnumerateRows(_tx);
            foreach (var (_, _) in rows)
            {
                yield return new NodeId(offset, tableId);
                offset++;
            }
            tableId++;
        }
    }

    public IReadOnlyDictionary<string, object?>? GetNodeProperties(NodeId id)
    {
        uint t = 0;
        foreach (var (_, tableData) in _nodeTables)
        {
            if (t == id.TableId)
            {
                ulong off = 0;
                var rows = _tx is null ? tableData.EnumerateRows() : tableData.EnumerateRows(_tx);
                foreach (var (_, props) in rows)
                {
                    if (off == id.Offset) return props;
                    off++;
                }
                return null;
            }
            t++;
        }
        return null;
    }

    public IEnumerable<GdsEdge> GetOutEdges(NodeId src)
    {
        EnsureAdj();
        return _outAdj.TryGetValue(src, out var list) ? list : Enumerable.Empty<GdsEdge>();
    }

    public IEnumerable<GdsEdge> GetInEdges(NodeId dst)
    {
        EnsureAdj();
        return _inAdj.TryGetValue(dst, out var list) ? list : Enumerable.Empty<GdsEdge>();
    }

    public IEnumerable<GdsEdge> GetBothEdges(NodeId n)
    {
        EnsureAdj();
        var seen = new HashSet<(NodeId, NodeId)>();
        foreach (var e in GetOutEdges(n))
        {
            seen.Add((e.Source, e.Target));
            yield return e;
        }
        foreach (var e in GetInEdges(n))
        {
            if (seen.Add((e.Source, e.Target))) yield return e;
        }
    }

    public long OutDegree(NodeId n)
    {
        EnsureAdj();
        return _outAdj.TryGetValue(n, out var l) ? l.Count : 0;
    }

    public long InDegree(NodeId n)
    {
        EnsureAdj();
        return _inAdj.TryGetValue(n, out var l) ? l.Count : 0;
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static bool TryDouble(object? v, out double d)
    {
        switch (v) {
            case double dv: d = dv; return true;
            case float f:   d = f;  return true;
            case long l:    d = l;  return true;
            case int i:     d = i;  return true;
            default:        d = 0;  return false;
        }
    }
}
