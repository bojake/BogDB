using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Evaluates and sets variables mapping Cypher RETURN sets.
/// Phase 8: accepts BoundProjectionItem list from real planner.
/// </summary>
public sealed class LogicalProjection : LogicalOperator
{
    public List<Expression> Expressions { get; }
    public List<BoundProjectionItem> ProjectionItems { get; }

    /// <summary>Phase 8 constructor: real projection items from bound return clause.</summary>
    public LogicalProjection(List<BoundProjectionItem> projectionItems, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_PROJECTION, child)
    {
        ProjectionItems = projectionItems ?? new List<BoundProjectionItem>();
        Expressions = new List<Expression>();
        foreach (var item in ProjectionItems)
            Expressions.Add(item.Expression);
    }

    /// <summary>Backward-compat constructor used by legacy callers.</summary>
    public LogicalProjection(List<Expression>? expressions, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_PROJECTION, child)
    {
        Expressions = expressions ?? new List<Expression>();
        ProjectionItems = new List<BoundProjectionItem>();
    }

    public override string GetExpressionsForPrinting()
        => $"PROJECTION ({Expressions.Count} expressions)";
}
