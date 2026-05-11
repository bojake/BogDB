using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalExpressionsScan : LogicalOperator
{
    public IReadOnlyList<Expression> Expressions { get; }

    public LogicalExpressionsScan(IReadOnlyList<Expression> expressions)
        : base(LogicalOperatorType.LOGICAL_EXPRESSIONS_SCAN)
    {
        Expressions = expressions;
    }

    public override string GetExpressionsForPrinting()
        => $"ExpressionsScan({Expressions.Count})";
}
