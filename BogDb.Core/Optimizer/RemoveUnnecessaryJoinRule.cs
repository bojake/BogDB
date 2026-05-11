using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Optimizer;

/// <summary>
/// Removes hash-join nodes whose build or probe side is a trivial (property-less)
/// node scan. In those cases the join degenerates to a redundant ID-existence check
/// that produces no additional columns, so the join can be replaced by the other side.
///
/// C++ parity: remove_unnecessary_join_optimizer.cpp
///
/// Example plan:
///   HashJoin(probe=TraverseRel, build=ScanNodeProperty[0 properties])
///   → TraverseRel
/// </summary>
public sealed class RemoveUnnecessaryJoinRule : LogicalRule
{
    public override bool Rewrite(LogicalPlan plan)
    {
        if (plan.LastOperator == null) return false;
        bool changed = false;
        plan.LastOperator = RewriteBottomUp(plan.LastOperator, ref changed);
        return changed;
    }

    private static LogicalOperator RewriteBottomUp(LogicalOperator op, ref bool changed)
    {
        // Bottom-up: rewrite children first
        for (int i = 0; i < op.GetNumChildren(); i++)
            op.Children[i] = RewriteBottomUp(op.GetChild(i), ref changed);

        if (op.OperatorType != LogicalOperatorType.LOGICAL_HASH_JOIN)
            return op;
        if (op.GetNumChildren() < 2)
            return op;

        var probe = op.GetChild(0);
        var build = op.GetChild(1);

        // If the BUILD side is a trivial property-less scan, drop it
        if (IsTrivialNodeScan(build))
        {
            changed = true;
            return probe;
        }

        // If the PROBE side is a trivial property-less scan, drop it
        if (IsTrivialNodeScan(probe))
        {
            changed = true;
            return build;
        }

        return op;
    }

    /// <summary>
    /// A LogicalScanNodeProperty is trivial if it requests no property columns —
    /// it was added only to confirm label membership.
    /// </summary>
    private static bool IsTrivialNodeScan(LogicalOperator op)
    {
        if (op.OperatorType != LogicalOperatorType.LOGICAL_SCAN_NODE)
            return false;
        var scan = (LogicalScanNodeProperty)op;
        return scan.Properties.Count == 0;
    }
}
