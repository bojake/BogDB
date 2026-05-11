using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Binder;
using BogDb.Core.Common;
using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Optimizer;

/// <summary>
/// Detects GROUP BY key sets that contain both a primary-key-style property and other
/// properties of the same variable that are functionally determined by it.
/// Non-PK same-variable properties are moved to DependentKeyItems, shrinking the
/// hash-table key from N dimensions to M &lt; N.
///
/// C++ parity: agg_key_dependency_optimizer.cpp
///
/// "Primary key" heuristic (without full catalog integration):
///   A property is treated as PK proxy if its name is "id", "key", or ends with "_id".
///
/// Example:
///   RETURN n.id, n.name, n.age, COUNT(*) GROUP BY n.id, n.name, n.age
///   → AggKeyItems=[n.id]   DependentKeyItems=[n.name, n.age]
/// </summary>
public sealed class AggKeyDependencyRule : LogicalRule
{
    public override bool Rewrite(LogicalPlan plan)
    {
        if (plan.LastOperator == null) return false;
        bool changed = false;
        VisitBottomUp(plan.LastOperator, ref changed);
        return changed;
    }

    private static void VisitBottomUp(LogicalOperator op, ref bool changed)
    {
        for (int i = 0; i < op.GetNumChildren(); i++)
            VisitBottomUp(op.GetChild(i), ref changed);

        if (op.OperatorType == LogicalOperatorType.LOGICAL_AGGREGATE)
            OptimizeAggregate((LogicalAggregate)op, ref changed);
    }

    private static void OptimizeAggregate(LogicalAggregate agg, ref bool changed)
    {
        if (agg.KeyItems.Count < 2) return;

        // Collect variables for which a PK-proxy key exists in the GROUP BY list
        var pkVars = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var item in agg.KeyItems)
        {
            if (item.Expression is PropertyExpression prop && IsPkProxy(prop.PropertyName))
                pkVars.Add(prop.NodeVariableName);
        }

        if (pkVars.Count == 0) return;

        var mainKeys  = new List<BoundProjectionItem>();
        var dependent = new List<BoundProjectionItem>();
        foreach (var item in agg.KeyItems)
        {
            if (item.Expression is PropertyExpression p
                && !IsPkProxy(p.PropertyName)
                && pkVars.Contains(p.NodeVariableName))
                dependent.Add(item);
            else
                mainKeys.Add(item);
        }

        if (dependent.Count == 0) return;

        agg.SetDependentKeys(mainKeys, dependent);
        changed = true;
    }

    private static bool IsPkProxy(string propName)
    {
        var lower = propName.ToLowerInvariant();
        return lower == "id" || lower == "key" || lower.EndsWith("_id", System.StringComparison.Ordinal);
    }
}
