using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical operator for executing Table Functions mapping `CALL func() YIELD a, b` bounds.
/// Mirrors C++ LogicalTableFunctionCall.
/// </summary>
public sealed class LogicalTableFunctionCall : LogicalOperator
{
    public Expression FunctionExpression { get; }
    public IReadOnlyList<Expression> OutVariables { get; }

    public LogicalTableFunctionCall(Expression functionExpression, IReadOnlyList<Expression> outVariables, LogicalOperator? child = null)
        : base(LogicalOperatorType.TABLE_FUNCTION_CALL)
    {
        FunctionExpression = functionExpression;
        OutVariables = outVariables;
        // Only add child when it is truly non-null — the single-arg base ctor sets Children=[].
        // Passing null to the two-arg ctor stores null in Children[0], causing MapOperator(null) downstream.
        if (child != null) Children.Add(child);
    }

    public override string GetExpressionsForPrinting() => FunctionExpression.ToString();
}
