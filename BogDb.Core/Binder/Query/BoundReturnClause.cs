using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Binder;

/// <summary>
/// Bound RETURN clause: list of typed projection items with column names.
/// </summary>
public class BoundOrderByElement
{
    public Expression Expression { get; }
    public bool IsAscending { get; }

    public BoundOrderByElement(Expression expression, bool isAscending)
    {
        Expression = expression;
        IsAscending = isAscending;
    }
}

public class BoundProjectionBody
{
    public bool IsDistinct { get; }
    public IReadOnlyList<BoundProjectionItem> ProjectionItems { get; }
    public IReadOnlyList<BoundOrderByElement> OrderByElements { get; }
    public Expression? SkipExpression { get; }
    public Expression? LimitExpression { get; }

    public BoundProjectionBody(
        bool isDistinct,
        List<BoundProjectionItem> items,
        List<BoundOrderByElement> orderByElements,
        Expression? skipExpression,
        Expression? limitExpression)
    {
        IsDistinct = isDistinct;
        ProjectionItems = items;
        OrderByElements = orderByElements;
        SkipExpression = skipExpression;
        LimitExpression = limitExpression;
    }
}

public class BoundReturnClause
{
    public BoundProjectionBody ProjectionBody { get; }

    public BoundReturnClause(BoundProjectionBody body)
    {
        ProjectionBody = body;
    }

    public IReadOnlyList<BoundProjectionItem> Items => ProjectionBody.ProjectionItems;

    public BoundReturnClause(List<BoundProjectionItem> items)
    {
        ProjectionBody = new BoundProjectionBody(false, items, new List<BoundOrderByElement>(), null, null);
    }
}

/// <summary>
/// A single projected column: bound expression + output column alias.
/// </summary>
public class BoundProjectionItem
{
    public Expression Expression { get; }
    public string ColumnName { get; }

    public BoundProjectionItem(Expression expression, string columnName)
    {
        Expression = expression;
        ColumnName = columnName;
    }
}
