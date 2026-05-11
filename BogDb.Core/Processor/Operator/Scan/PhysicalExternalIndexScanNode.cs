using System.Collections.Generic;
using BogDb.Core.Binder;
using BogDb.Core.Extension;

namespace BogDb.Core.Processor.Operator.Scan;

/// <summary>
/// Physical extension-backed index prefilter scan. Resolves the provider at
/// runtime, asks it for candidate row offsets, and then materializes visible
/// rows through the normal node-table visibility path.
/// </summary>
public sealed class PhysicalExternalIndexScanNode : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly ExternalIndexLookup _lookup;
    private readonly string _variableName;

    private IReadOnlyList<long>? _candidateOffsets;
    private int _nextCandidateOffsetIndex;
    private bool _initialized;

    public PhysicalExternalIndexScanNode(
        Main.BogDatabase database,
        ExternalIndexLookup lookup,
        string variableName,
        uint id)
        : base(PhysicalOperatorType.EXTERNAL_INDEX_SCAN, id)
    {
        _database = database;
        _lookup = lookup;
        _variableName = variableName;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!_initialized)
        {
            _initialized = true;
            _candidateOffsets = ResolveCandidateOffsets(context);
            _nextCandidateOffsetIndex = 0;
        }

        if (_candidateOffsets == null || _nextCandidateOffsetIndex >= _candidateOffsets.Count)
            return false;

        while (_nextCandidateOffsetIndex < _candidateOffsets.Count)
        {
            var candidateOffset = _candidateOffsets[_nextCandidateOffsetIndex++];
            Dictionary<string, object>? normalizedProps = null;
            object? nodeId = null;

            if (_database.NodeTables.TryGetValue(_lookup.TableName, out var table))
            {
                if (!table.TryGetByOffset(context.Transaction, candidateOffset, out nodeId, out var nodeProps) ||
                    nodeProps is null)
                {
                    continue;
                }

                normalizedProps = _database.NormalizeNodePropertiesForRead(_lookup.TableName, nodeProps);
            }
            else
            {
                if (!_database.GraphStore.TryGetNodeByOffset(_lookup.TableName, candidateOffset, out nodeId, out var nodeProps) ||
                    nodeProps is null)
                {
                    continue;
                }

                normalizedProps = _database.NormalizeNodePropertiesForRead(_lookup.TableName, nodeProps);
            }

            if (normalizedProps is null || nodeId is null)
                continue;
            if (!normalizedProps.ContainsKey("id"))
                normalizedProps["id"] = nodeId;

            context.CurrentNodeId = nodeId;
            context.CurrentNodeProperties = normalizedProps;
            context.CurrentVariableIds ??= new Dictionary<string, object>();
            context.CurrentVariableProperties ??= new Dictionary<string, Dictionary<string, object>>();
            context.CurrentVariableIds[_variableName] = nodeId;
            context.CurrentVariableProperties[_variableName] = normalizedProps;
            context.CurrentProjectionRow = null;
            return true;
        }

        return false;
    }

    private IReadOnlyList<long>? ResolveCandidateOffsets(ExecutionContext context)
    {
        if (!_database.TryGetExtensionService<IExternalIndexProvider>(ExternalIndexServiceNames.Provider, out var provider))
            return null;

        var lookupKey = ResolveLookupKey(context);
        if (lookupKey is null)
            return null;

        return provider.LookupCandidateNodeOffsets(_lookup, lookupKey, context);
    }

    private object? ResolveLookupKey(ExecutionContext context)
    {
        if (_lookup.LookupKey is Expression expression)
            return Common.TypeCoercionHelper.Normalize(ExpressionExecutionHelper.Evaluate(expression, context));

        return Common.TypeCoercionHelper.Normalize(_lookup.LookupKey);
    }
}
