using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Optimizer;

/// <summary>
/// Reorders sequences of HashJoins, CrossProducts, and Intersects recursively 
/// utilizing Cartesian Cost Model algorithms and Cardinality dimensions.
/// </summary>
public sealed class JoinOrderOptimizer : LogicalRule
{
    public override bool Rewrite(LogicalPlan plan)
    {
        bool changed = false;
        if (plan.LastOperator != null)
        {
            plan.LastOperator = ReorderJoins(plan.LastOperator, out changed);
        }
        
        return changed; // Emits true when the Physical operator tree map sequence has been structurally shifted.
    }

    private LogicalOperator ReorderJoins(LogicalOperator op, out bool changed)
    {
        changed = false;
        
        for (int i = 0; i < op.Children.Count; i++)
        {
            op.Children[i] = ReorderJoins(op.Children[i], out bool childChanged);
            changed |= childChanged;
        }

        // Simple Greedy CBO Phase 11: 
        // Force the smaller cardinality subtree to the Right (Build side) of CrossProducts/Joins
        if (op.OperatorType == LogicalOperatorType.LOGICAL_CROSS_PRODUCT ||
            op.OperatorType == LogicalOperatorType.LOGICAL_HASH_JOIN)
        {
            var left = op.GetChild(0);
            var right = op.GetChild(1);

            // In BogDb, HashJoin builds on the Right child. Memory footprints are lower when Build side is smaller.
            if (left.EstCardinality < right.EstCardinality)
            {
                op.Children[0] = right;
                op.Children[1] = left;
                changed = true;
            }
        }

        return op;
    }
}
