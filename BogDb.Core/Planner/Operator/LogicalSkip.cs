using BogDb.Core.Common;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalSkip : LogicalOperator
{
    public Expression SkipExpression { get; }

    public LogicalSkip(Expression skipExpression, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_SKIP, child)
    {
        SkipExpression = skipExpression;
    }

    public override string GetExpressionsForPrinting()
    {
        return string.Empty;
    }
}
