using System.Collections.Generic;
using BogDb.Core.Parser;

namespace BogDb.Core.Parser.Query;

/// <summary>
/// Represents a `CALL func() YIELD a, b` table function invocation.
/// Mirrors C++ InQueryCallClause.
/// </summary>
public sealed class InQueryCallClause : ReadingClause
{
    public ParsedFunctionExpression FunctionExpression { get; }
    public IReadOnlyList<ParsedExpression> YieldVariables { get; }
    public ParsedExpression? WherePredicate { get; private set; }

    public InQueryCallClause(ParsedFunctionExpression functionExpression, IReadOnlyList<ParsedExpression> yieldVariables)
        : base(ClauseType.IN_QUERY_CALL)
    {
        FunctionExpression = functionExpression;
        YieldVariables = yieldVariables ?? new List<ParsedExpression>();
    }

    public void SetWherePredicate(ParsedExpression wherePredicate)
    {
        WherePredicate = wherePredicate;
    }
}
