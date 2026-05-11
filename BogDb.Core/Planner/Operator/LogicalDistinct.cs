using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalDistinct : LogicalOperator
{
    public LogicalDistinct(LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_DISTINCT, child)
    {
    }

    public override string GetExpressionsForPrinting()
    {
        return string.Empty;
    }
}
