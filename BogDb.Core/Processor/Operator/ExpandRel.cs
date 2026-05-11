using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Binder;
using BogDb.Core.Main;
using BogDb.Core.Parser;

namespace BogDb.Core.Processor.Operator;

/// <summary>
/// Physical operator for relationship traversal: given a bound source node, iterates
/// all edges in each named relationship table that match the source binding and direction,
/// yielding one output tuple per matching edge, across ALL matched relationship types.
///
/// C++ parity: ScanRelTableColumns / RelTableScan + AdjListScan + scan_multi_rel_tables.
///
/// Multi-table support: when a query pattern like MATCH (a)-[r:KNOWS|LIKES]->(b) is
/// bound, QueryRel.TableNames contains {"KNOWS","LIKES"}.  For each input (source) row
/// we cycle through every table in sequence. For undirected traversal (BOTH), each table
/// is scanned forward then reversed before moving to the next table.
/// </summary>
public sealed class ExpandRel : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly QueryRel _queryRel;

    // Per-input-row state
    private ExecutionState? _inputState;
    private Queue<string>? _pendingTableNames;   // tables still to scan for current input row
    private IEnumerator<KeyValuePair<Main.RelRowRef, Dictionary<string, object>>>? _iterator;
    private string _currentRelTableName = "";
    private bool _doingReverseForCurrentTable;   // BOTH-direction: true while doing reverse pass

    public ExpandRel(Main.BogDatabase database, QueryRel queryRel, PhysicalOperator child, uint id)
        : base(PhysicalOperatorType.SCAN_REL_PROPERTY, child, id)
    {
        _database = database;
        _queryRel = queryRel;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        while (true)
        {
            // ── Phase 1: need a new input row ────────────────────────────────
            if (_inputState == null)
            {
                if (!Children[0].GetNextTuple(context))
                    return false;

                _inputState = context.CaptureState();
                _doingReverseForCurrentTable = false;

                // Build the ordered queue of rel-table names to scan for this input row.
                // If the query specifies relationship types, use them; otherwise scan every
                // rel table registered in the database (untyped MATCH (a)-[r]->(b)).
                var tableList = _queryRel.TableNames.Count > 0
                    ? _queryRel.TableNames
                    : (IReadOnlyList<string>)new List<string>(_database.RelTables.Keys);

                _pendingTableNames = new Queue<string>(tableList);
                _iterator = null;
            }

            // ── Phase 2: advance to next table if no current iterator ─────────
            if (_iterator == null)
            {
                if (_pendingTableNames!.Count == 0)
                {
                    // All tables exhausted for this input row → next input row
                    _inputState = null;
                    continue;
                }

                _currentRelTableName = _pendingTableNames.Dequeue();
                _doingReverseForCurrentTable = false;
                _iterator = OpenIterator(_currentRelTableName, context.Transaction);
            }

            context.RestoreState(_inputState!);

            // ── Phase 3: iterate edges from current table ─────────────────────
            while (_iterator.MoveNext())
            {
                var relRef = _iterator.Current.Key;
                var edge = relRef.EdgeKey;
                var edgeProperties = _database.NormalizeRelPropertiesForRead(_currentRelTableName, _iterator.Current.Value);

                if (!TryMatchEdge(edge, edgeProperties, context,
                        out var dstId, out var dstProps, out var actualSrcId, out var actualDstId,
                        out var srcProps))
                    continue;

                // Populate context for downstream operators
                context.CurrentNodeId = dstId;
                context.CurrentNodeProperties = dstProps;
                context.CurrentVariableIds ??= new Dictionary<string, object>();
                context.CurrentVariableProperties ??= new Dictionary<string, Dictionary<string, object>>();
                context.CurrentVariableIds[_queryRel.SrcNode.VariableName] = actualSrcId;
                context.CurrentVariableIds[_queryRel.DstNode.VariableName] = actualDstId;
                context.CurrentVariableProperties[_queryRel.SrcNode.VariableName] = srcProps;
                context.CurrentVariableProperties[_queryRel.DstNode.VariableName] = dstProps;
                if (!string.IsNullOrEmpty(_queryRel.VariableName))
                {
                    context.CurrentVariableIds[_queryRel.VariableName] = relRef;
                    // Include the rel type so type(r) works even with multi-table
                    var enriched = new Dictionary<string, object>(edgeProperties)
                        { ["_label"] = _currentRelTableName, ["_type"] = _currentRelTableName };
                    context.CurrentVariableProperties[_queryRel.VariableName] = enriched;
                }

                if (!string.IsNullOrEmpty(_queryRel.PathVariableName))
                {
                    var pathDict = BuildSingleHopPath(actualSrcId, actualDstId, relRef);
                    context.CurrentScalarBindings ??= new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
                    context.CurrentScalarBindings[_queryRel.PathVariableName] = pathDict;
                    context.CurrentVariableProperties[_queryRel.PathVariableName] = pathDict
                        .ToDictionary(kv => kv.Key, kv => kv.Value, System.StringComparer.OrdinalIgnoreCase);
                }

                context.CurrentProjectionRow = null;
                return true;
            }

            // ── Phase 4: current iterator exhausted ───────────────────────────
            if (_queryRel.Direction == ArrowDirection.BOTH && !_doingReverseForCurrentTable)
            {
                // Replay same table in reverse direction
                _doingReverseForCurrentTable = true;
                _iterator = OpenIterator(_currentRelTableName, context.Transaction);
                continue;
            }

            // Done with this table; move to next
            _iterator = null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerator<KeyValuePair<Main.RelRowRef, Dictionary<string, object>>> OpenIterator(
        string relTableName,
        BogDb.Core.Transaction.Transaction tx)
    {
        if (_database.RelTables.TryGetValue(relTableName, out var tbl))
        {
            // ── Fast path: adjacency-indexed O(degree) lookup ────────────
            // When the source node is bound, use the adjacency index instead
            // of scanning all edges. This is the CSR-equivalent optimization.
            object? boundSrcId = null;
            if (_inputState != null)
            {
                _inputState.CurrentVariableIds?.TryGetValue(_queryRel.SrcNode.VariableName, out boundSrcId);
            }

            if (boundSrcId != null)
            {
                if (_queryRel.Direction == ArrowDirection.LEFT)
                {
                    // LEFT arrow: source is the dst in storage, so look up incoming to source
                    return tbl.EnumerateIncomingRowsWithRefs(relTableName, boundSrcId, tx).GetEnumerator();
                }
                else if (_queryRel.Direction == ArrowDirection.RIGHT || !_doingReverseForCurrentTable)
                {
                    // RIGHT arrow or first pass of BOTH: outgoing from source
                    return tbl.EnumerateOutgoingRowsWithRefs(relTableName, boundSrcId, tx).GetEnumerator();
                }
                else
                {
                    // Reverse pass of BOTH: incoming to source
                    return tbl.EnumerateIncomingRowsWithRefs(relTableName, boundSrcId, tx).GetEnumerator();
                }
            }

            // ── Fallback: full table scan (unbound source, cartesian product) ──
            return new List<KeyValuePair<Main.RelRowRef, Dictionary<string, object>>>(
                tbl.EnumerateRowsWithRefs(relTableName, tx)).GetEnumerator();
        }

        // Fall back to GraphStore if not in in-memory dict
        var list = _database.GraphStore.EnumerateRels(relTableName)
            .Select(kvp => new KeyValuePair<Main.RelRowRef, Dictionary<string, object>>(
                new Main.RelRowRef(relTableName, -1, kvp.Key), kvp.Value))
            .ToList();
        return list.GetEnumerator();
    }

    private bool TryMatchEdge(
        Main.EdgeKey edge,
        Dictionary<string, object> edgeProperties,
        ExecutionContext context,
        out object dstId,
        out Dictionary<string, object> dstProps,
        out object actualSrcId,
        out object actualDstId,
        out Dictionary<string, object> srcProps)
    {
        // Resolve canonical (from, to) based on direction and reverse flag
        object fromId, toId;
        if (_queryRel.Direction == ArrowDirection.LEFT ||
            (_queryRel.Direction == ArrowDirection.BOTH && _doingReverseForCurrentTable))
        {
            fromId = edge.To;
            toId   = edge.From;
        }
        else
        {
            fromId = edge.From;
            toId   = edge.To;
        }

        dstId     = toId;
        actualSrcId = fromId;
        actualDstId = toId;
        dstProps  = null!;
        srcProps  = null!;

        // Validate bound source node matches
        object? boundSrcId = null;
        bool hasSrc = context.CurrentVariableIds != null &&
                      context.CurrentVariableIds.TryGetValue(_queryRel.SrcNode.VariableName, out boundSrcId);
        if (hasSrc && !Equals(boundSrcId, fromId))
            return false;

        if (!TryResolveNode(fromId, _queryRel.SrcNode.TableNames, context,
                out var actualSrcTableName, out var resolvedSrcProps))
        {
            return false;
        }

        if (!TryResolveNode(toId, _queryRel.DstNode.TableNames, context,
                out var actualDstTableName, out var resolvedDstProps))
        {
            return false;
        }

        if (!_queryRel.IsConnectionAllowed(_currentRelTableName, actualSrcTableName, actualDstTableName))
            return false;

        srcProps = resolvedSrcProps;
        dstProps = resolvedDstProps;
        return true;
    }

    private bool TryResolveNode(
        object nodeId,
        IReadOnlyList<string> candidateTableNames,
        ExecutionContext context,
        out string actualTableName,
        out Dictionary<string, object> properties)
    {
        var tableNames = candidateTableNames.Count > 0
            ? candidateTableNames
            : (IReadOnlyList<string>)new List<string>(_database.NodeTables.Keys);
        var sawConfiguredCandidate = false;

        foreach (var tableName in tableNames)
        {
            if (!_database.NodeTables.TryGetValue(tableName, out var table))
                continue;
            sawConfiguredCandidate = true;
            if (!table.TryGetProperties(context.Transaction, nodeId, out var props) || props is null)
                continue;

            properties = _database.NormalizeNodePropertiesForRead(tableName, props);
            if (!properties.ContainsKey("id"))
                properties["id"] = nodeId;
            actualTableName = tableName;
            return true;
        }

        if (candidateTableNames.Count > 0 && !sawConfiguredCandidate)
        {
            actualTableName = candidateTableNames[0];
            properties = new Dictionary<string, object> { ["id"] = nodeId };
            return true;
        }

        actualTableName = string.Empty;
        properties = null!;
        return false;
    }

    private static Dictionary<string, object?> BuildSingleHopPath(object srcId, object dstId, Main.RelRowRef relRef)
    {
        var nodeIds = new List<object?> { srcId, dstId };
        var rels = new List<object?> { relRef };
        return new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["_nodes"] = nodeIds,
            ["_rels"] = rels,
            ["_length"] = 1L,
            ["nodes"] = nodeIds,
            ["rels"] = rels
        };
    }
}
