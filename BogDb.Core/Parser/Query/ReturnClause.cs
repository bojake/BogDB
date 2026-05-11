using System.Collections.Generic;

namespace BogDb.Core.Parser;

/// <summary>
/// Parsed RETURN clause wrapping a ProjectionBody.
/// Mirrors C++ ReturnClause from parser/query/return_with_clause/return_clause.h
/// </summary>
public class ReturnClause
{
    public ProjectionBody ProjectionBody { get; }
    public ReturnClause(ProjectionBody body) { ProjectionBody = body; }
}

public class OrderByElement
{
    public ParsedExpression Expression { get; }
    public bool IsAscending { get; }

    public OrderByElement(ParsedExpression expression, bool isAscending)
    {
        Expression = expression;
        IsAscending = isAscending;
    }
}

/// <summary>
/// Holds the list of projection items plus optional DISTINCT, ORDER BY, SKIP, LIMIT.
/// Mirrors C++ ProjectionBody.
/// </summary>
public class ProjectionBody
{
    public bool IsDistinct { get; }
    public IReadOnlyList<ParsedExpression> ProjectionExpressions { get; }
    public IReadOnlyList<OrderByElement> OrderByElements { get; }
    public ParsedExpression? SkipExpression { get; }
    public ParsedExpression? LimitExpression { get; }

    public ProjectionBody(
        bool isDistinct, 
        List<ParsedExpression> expressions,
        List<OrderByElement> orderByElements,
        ParsedExpression? skipExpression,
        ParsedExpression? limitExpression)
    {
        IsDistinct = isDistinct;
        ProjectionExpressions = expressions;
        OrderByElements = orderByElements;
        SkipExpression = skipExpression;
        LimitExpression = limitExpression;
    }
}

public class WithClause : ReadingClause
{
    public ProjectionBody ProjectionBody { get; }
    public ParsedExpression? WherePredicate { get; }

    public WithClause(ProjectionBody body, ParsedExpression? wherePredicate) : base(ClauseType.WITH)
    {
        ProjectionBody = body;
        WherePredicate = wherePredicate;
    }
}
