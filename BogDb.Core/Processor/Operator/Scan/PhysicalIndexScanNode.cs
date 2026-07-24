using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Binder;
using BogDb.Core.Processor.Operator;

namespace BogDb.Core.Processor.Operator.Scan;

/// <summary>
/// Performs index lookup via a NodePropertyIndex and emits one tuple per
/// matching node. Supports non-unique indexes — all validated matches are
/// returned across successive GetNextTuple() calls.
/// C++ parity: src/processor/operator/index_scan/index_scan_node.h
/// </summary>
public sealed class PhysicalIndexScanNode : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly string _tableName;
    private readonly string _propertyName;
    private readonly object? _lookupKey;
    private readonly string _variableName;
    private readonly bool _isPrefixScan;

    // Iterator state — lazily initialized on first GetNextTuple()
    private IReadOnlyList<long>? _matchedOffsets;
    private int _nextOffsetIndex;
    private bool _initialized;

    /// <param name="database">The database containing NodeIndexes.</param>
    /// <param name="tableName">Node table to scan (e.g. "Person").</param>
    /// <param name="propertyName">Indexed property name (e.g. "id").</param>
    /// <param name="lookupKey">The equality value or bound expression to look up.</param>
    /// <param name="variableName">Cypher binding variable (e.g. "n").</param>
    /// <param name="id">Operator id for the physical plan.</param>
    /// <param name="isPrefixScan">When true, treats lookupKey as a prefix for STARTS WITH index scan.</param>
    public PhysicalIndexScanNode(
        Main.BogDatabase database,
        string tableName,
        string propertyName,
        object? lookupKey,
        string variableName,
        uint id,
        bool isPrefixScan = false)
        : base(PhysicalOperatorType.INDEX_SCAN, id)
    {
        _database = database;
        _tableName = tableName;
        _propertyName = propertyName;
        _lookupKey = lookupKey;
        _variableName = variableName;
        _isPrefixScan = isPrefixScan;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        // ── Lazy initialization: resolve lookup key and collect all offsets ───
        if (!_initialized)
        {
            _initialized = true;
            _matchedOffsets = ResolveMatchedOffsets(context);
            _nextOffsetIndex = 0;
        }

        if (_matchedOffsets is null || _nextOffsetIndex >= _matchedOffsets.Count)
            return false;

        // For prefix scans, lookupKey is the prefix string (used only for offset resolution)
        var lookupKey = ResolveLookupKey(context);
        if (lookupKey is null) return false;

        // ── Scan forward through offsets to find the next valid match ─────────
        while (_nextOffsetIndex < _matchedOffsets.Count)
        {
            var offset = _matchedOffsets[_nextOffsetIndex];
            _nextOffsetIndex++;

            Dictionary<string, object>? props = null;
            object? nodeId = null;

            if (_database.NodeTables.TryGetValue(_tableName, out var table))
            {
                if (!table.TryGetByOffset(context.Transaction, offset, out nodeId, out var nodeProps))
                    continue;
                if (nodeProps is null)
                    continue;
                var normalized = _database.NormalizeNodePropertiesForRead(_tableName, nodeProps);
                if (!Matches(normalized, lookupKey))
                    continue;
                props = normalized;
            }
            else
            {
                if (!_database.GraphStore.TryGetNodeByOffset(_tableName, offset, out nodeId, out var nodeProps))
                    continue;
                if (nodeProps is null)
                    continue;
                var normalized = _database.NormalizeNodePropertiesForRead(_tableName, nodeProps);
                if (!Matches(normalized, lookupKey))
                    continue;
                props = normalized;
            }

            if (props is null) continue;

            // ── Emit this match ──────────────────────────────────────────────
            context.CurrentNodeId = nodeId;
            context.CurrentNodeProperties = props;
            context.CurrentVariableIds ??= new Dictionary<string, object>();
            context.CurrentVariableProperties ??= new Dictionary<string, Dictionary<string, object>>();
            context.CurrentVariableIds[_variableName] = nodeId!;
            context.CurrentVariableProperties[_variableName] = props;
            context.CurrentProjectionRow = null;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolve the lookup key and collect all candidate offsets from the index.
    /// Returns null if no index exists or the key is not found.
    /// </summary>
    private IReadOnlyList<long>? ResolveMatchedOffsets(ExecutionContext context)
    {
        var lookupKey = ResolveLookupKey(context);
        if (lookupKey is null) return null;

        if (!_database.NodeIndexes.TryGetValue(_tableName, out var nodeIndex))
            return null;

        if (_isPrefixScan)
        {
            var prefix = lookupKey is string s ? s : lookupKey.ToString() ?? "";
            return nodeIndex.TryLookupByPrefix(_propertyName, prefix, out var prefixOffsets)
                ? prefixOffsets
                : null;
        }

        return nodeIndex.TryLookupAll(_propertyName, lookupKey, out var nodeOffsets)
            ? nodeOffsets
            : null;
    }

    private object? ResolveLookupKey(ExecutionContext context)
    {
        if (_lookupKey is Expression expression)
            return TypeCoercionHelper.Normalize(ExpressionExecutionHelper.Evaluate(expression, context));

        return TypeCoercionHelper.Normalize(_lookupKey);
    }

    // Re-validate a candidate against the node's CURRENT property value. Index postings can outlive the
    // value that produced them (a later write may point the offset elsewhere), so every emitted row is
    // checked here — the equality path always did this; the prefix path used to skip it and so could
    // return a row whose value no longer starts with the prefix.
    private bool Matches(Dictionary<string, object> props, object lookupKey)
    {
        if (!props.TryGetValue(_propertyName, out var propertyValue) || propertyValue is null)
            return false;

        if (_isPrefixScan)
        {
            if (propertyValue is not string value)
                return false;
            var prefix = lookupKey as string ?? lookupKey.ToString() ?? "";
            return value.StartsWith(prefix, System.StringComparison.Ordinal);
        }

        return StructuralValueComparer.AreEqual(propertyValue, lookupKey);
    }
}
