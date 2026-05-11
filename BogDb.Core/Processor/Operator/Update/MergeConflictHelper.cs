using System;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.Update;

internal static class MergeConflictHelper
{
    internal static bool TryFindExistingNodeIdOrThrow(
        Main.NodeTableData table,
        Dictionary<string, object> properties,
        ExecutionContext context,
        Func<Dictionary<string, object>, Dictionary<string, object>, object, bool> matchesProperties,
        out object? nodeId,
        out Dictionary<string, object>? existingProps)
    {
        existingProps = null;
        nodeId = null;

        if (properties.TryGetValue("id", out var explicitId) &&
            table.TryGetProperties(context.Transaction, explicitId, out var propsById) &&
            propsById != null)
        {
            if (!matchesProperties(propsById, properties, explicitId))
                throw new InvalidOperationException(
                    $"Found duplicated primary key value {TypeCoercionHelper.Normalize(explicitId)}, which violates the uniqueness constraint of the primary key column.");

            existingProps = new Dictionary<string, object>(propsById, StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = explicitId
            };
            nodeId = explicitId;
            return true;
        }

        foreach (var (candidateNodeId, props) in table.EnumerateRows(context.Transaction))
        {
            if (!matchesProperties(props, properties, candidateNodeId))
                continue;

            existingProps = new Dictionary<string, object>(props, StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = candidateNodeId
            };
            nodeId = candidateNodeId;
            return true;
        }

        return false;
    }
}
