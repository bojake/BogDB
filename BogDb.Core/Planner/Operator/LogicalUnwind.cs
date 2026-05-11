using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical operator that unwinds a collection expression into individual tuple bindings.
/// C++ parity: equivalent to LOGICAL_UNWIND in bogdb-cpp.
/// </summary>
public sealed class LogicalUnwind : LogicalOperator
{
    public Expression CollectionExpression { get; }
    public string Alias { get; }

    public LogicalUnwind(Expression collectionExpression, string alias, LogicalOperator? child)
        : base(LogicalOperatorType.LOGICAL_UNWIND)
    {
        CollectionExpression = collectionExpression;
        Alias = alias;
        if (child != null) Children.Add(child);
    }

    public override string GetExpressionsForPrinting()
        => $"UNWIND {CollectionExpression} AS {Alias}";
}
