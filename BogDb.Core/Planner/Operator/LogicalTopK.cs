using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical operator fusing ORDER BY + LIMIT into a single top-K selection.
/// Produced by <see cref="BogDb.Core.Optimizer.TopKOptimizerRule"/> when it detects
/// a <see cref="LogicalOrderBy"/> immediately followed by a <see cref="LogicalLimit"/>.
///
/// C++ parity: src/optimizer/top_k_optimizer.cpp → top_k_optimizer rule
/// </summary>
public sealed class LogicalTopK : LogicalOperator
{
    public IReadOnlyList<Expression> Expressions { get; }
    public IReadOnlyList<bool>        IsAscending  { get; }
    public Expression                LimitExpression { get; }

    public LogicalTopK(
        IReadOnlyList<Expression> expressions,
        IReadOnlyList<bool>       isAscending,
        Expression                limitExpression,
        LogicalOperator           child)
        : base(LogicalOperatorType.LOGICAL_TOP_K, child)
    {
        Expressions     = expressions;
        IsAscending     = isAscending;
        LimitExpression = limitExpression;
    }

    public override string GetExpressionsForPrinting() => $"TOP_K({LimitExpression})";
}
