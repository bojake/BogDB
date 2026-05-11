using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical nested-loop join for single-node pattern queries when there are
/// multiple MATCH clauses or multiple nodes in a pattern (Phase 8 minimal impl).
/// </summary>
public sealed class LogicalNestedLoopJoin : LogicalOperator
{
    public LogicalNestedLoopJoin(LogicalOperator left, LogicalOperator right)
        : base(LogicalOperatorType.LOGICAL_CROSS_PRODUCT, left, right) { }

    public override string GetExpressionsForPrinting() => "NESTED_LOOP_JOIN";
}
