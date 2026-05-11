using System.Collections.Generic;
using BogDb.Core.Parser;

namespace BogDb.Core.Binder;

/// <summary>
/// A logically bound table function invocation (CALL func() YIELD a).
/// Mirrors C++ BoundInQueryCall.
/// </summary>
public sealed class BoundInQueryCall : BoundReadingClause
{
    public Expression BoundFunctionExpression { get; }
    public IReadOnlyList<Expression> OutVariables { get; }
    public Expression? WherePredicate { get; }

    public BoundInQueryCall(Expression boundFunctionExpression, IReadOnlyList<Expression> outVariables, Expression? wherePredicate = null)
        : base(ClauseType.IN_QUERY_CALL)
    {
        BoundFunctionExpression = boundFunctionExpression;
        OutVariables = outVariables;
        WherePredicate = wherePredicate;
    }
}
