using System.Collections.Generic;
using BogDb.Core.Binder;
namespace BogDb.Core.Planner.Operator;

public sealed class LogicalLimit : LogicalOperator
{
    public Expression LimitExpression { get; }

    public LogicalLimit(Expression limitExpression, LogicalOperator child) 
        : base(LogicalOperatorType.LOGICAL_LIMIT, child)
    {
        LimitExpression = limitExpression;
    }

    public override string GetExpressionsForPrinting()
    {
        return string.Empty;
    }
}
