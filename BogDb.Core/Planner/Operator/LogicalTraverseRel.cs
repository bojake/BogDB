using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalTraverseRel : LogicalOperator
{
    public QueryRel QueryRel { get; }

    public LogicalTraverseRel(QueryRel queryRel, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_EXTEND, child)
    {
        QueryRel = queryRel;
    }

    public override string GetExpressionsForPrinting()
        => $"TRAVERSE {QueryRel.SrcNode.VariableName}-[:{(QueryRel.TableNames.Count > 0 ? QueryRel.TableNames[0] : "")}]->{QueryRel.DstNode.VariableName}";
}
