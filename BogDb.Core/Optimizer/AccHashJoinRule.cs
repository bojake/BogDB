using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Optimizer;

/// <summary>
/// Inserts a LogicalAccumulate node (REGULAR type) before the build side of every
/// hash join to create a pipeline-breaker: the build side is fully materialized
/// before the probe side begins.
///
/// C++ parity: acc_hash_join_optimizer.cpp (simplified — no SemiMasker injection)
///
/// Before:
///   HashJoin
///     child[0] = probe pipeline
///     child[1] = build pipeline
///
/// After:
///   HashJoin
///     child[0] = probe pipeline
///     child[1] = Accumulate(REGULAR) → build pipeline
///
/// This ensures correctness in BogDB's single-threaded pull-based executor
/// where probing and building cannot safely interleave.
/// </summary>
public sealed class AccHashJoinRule : LogicalRule
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

        if (op.OperatorType == LogicalOperatorType.LOGICAL_HASH_JOIN)
            WrapBuildSide(op, ref changed);
    }

    private static void WrapBuildSide(LogicalOperator hashJoin, ref bool changed)
    {
        if (hashJoin.GetNumChildren() < 2) return;

        var buildSide = hashJoin.GetChild(1);
        // Already wrapped — don't double-wrap
        if (buildSide.OperatorType == LogicalOperatorType.LOGICAL_ACCUMULATE) return;

        hashJoin.Children[1] = new LogicalAccumulate(
            Common.AccumulateType.REGULAR,
            new System.Collections.Generic.List<Binder.Expression>(),
            null,
            buildSide);

        changed = true;
    }
}
