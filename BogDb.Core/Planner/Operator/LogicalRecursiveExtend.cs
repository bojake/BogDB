using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalRecursiveExtend : LogicalOperator
{
    public QueryRel QueryRel { get; }
    public int LowerBound { get; }
    public int UpperBound { get; }
    /// <summary>
    /// When set by <see cref="BogDb.Core.Optimizer.LimitPushDownRule"/>, the BFS/DFS
    /// traversal will stop as soon as this many paths have been emitted.
    /// -1 means no early-stop limit.
    /// </summary>
    public long EarlyStopLimit { get; set; } = -1;

    public LogicalRecursiveExtend(QueryRel queryRel, int lowerBound, int upperBound, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_RECURSIVE_EXTEND, child)
    {
        QueryRel = queryRel;
        LowerBound = lowerBound;
        UpperBound = upperBound;
    }

    public override string GetExpressionsForPrinting()
    {
        var ubStr = UpperBound == int.MaxValue ? "*" : UpperBound.ToString();
        return $"RECURSIVE_EXTEND {QueryRel.SrcNode.VariableName}-[:{(QueryRel.TableNames.Count > 0 ? QueryRel.TableNames[0] : "")}*{LowerBound}..{ubStr}]->{QueryRel.DstNode.VariableName}";
    }
}
