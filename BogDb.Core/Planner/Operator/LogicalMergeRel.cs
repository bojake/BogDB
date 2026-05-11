using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalMergeRel : LogicalOperator
{
    public QueryRel MergeRel { get; }
    public IReadOnlyList<BoundMergeAction> Actions { get; }

    public LogicalMergeRel(QueryRel mergeRel, IReadOnlyList<BoundMergeAction> actions, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_MERGE_REL, child)
    {
        MergeRel = mergeRel;
        Actions = actions;
    }

    public LogicalMergeRel(QueryRel mergeRel, IReadOnlyList<BoundMergeAction> actions)
        : base(LogicalOperatorType.LOGICAL_MERGE_REL)
    {
        MergeRel = mergeRel;
        Actions = actions;
    }

    public override string GetExpressionsForPrinting()
        => $"MERGE_REL({MergeRel.VariableName})";
}
