using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalMergeGraph : LogicalOperator
{
    public QueryGraph MergeGraph { get; }
    public IReadOnlyList<BoundMergeAction> Actions { get; }

    public LogicalMergeGraph(QueryGraph mergeGraph, IReadOnlyList<BoundMergeAction> actions, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_MERGE_GRAPH, child)
    {
        MergeGraph = mergeGraph;
        Actions = actions;
    }

    public LogicalMergeGraph(QueryGraph mergeGraph, IReadOnlyList<BoundMergeAction> actions)
        : base(LogicalOperatorType.LOGICAL_MERGE_GRAPH)
    {
        MergeGraph = mergeGraph;
        Actions = actions;
    }

    public override string GetExpressionsForPrinting()
        => $"MERGE_GRAPH(nodes={MergeGraph.GetNumQueryNodes()}, rels={MergeGraph.GetNumQueryRels()})";
}
