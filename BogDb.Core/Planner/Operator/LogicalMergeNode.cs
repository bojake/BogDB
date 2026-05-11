using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalMergeNode : LogicalOperator
{
    public QueryNode MergeNode { get; }
    public IReadOnlyList<BoundMergeAction> Actions { get; }

    public LogicalMergeNode(QueryNode mergeNode, IReadOnlyList<BoundMergeAction> actions, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_MERGE_NODE, child)
    {
        MergeNode = mergeNode;
        Actions = actions;
    }

    public LogicalMergeNode(QueryNode mergeNode, IReadOnlyList<BoundMergeAction> actions)
        : base(LogicalOperatorType.LOGICAL_MERGE_NODE)
    {
        MergeNode = mergeNode;
        Actions = actions;
    }

    public override string GetExpressionsForPrinting()
        => $"MERGE_NODE({MergeNode.VariableName})";
}
