namespace BogDb.Core.Planner.Operator;

public sealed class LogicalUnionAll : LogicalOperator
{
    public LogicalUnionAll(LogicalOperator left, LogicalOperator right)
        : base(LogicalOperatorType.LOGICAL_UNION_ALL, left, right)
    {
    }

    public override string GetExpressionsForPrinting()
    {
        return string.Empty;
    }
}
