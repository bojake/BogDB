using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalOrderBy : LogicalOperator
{
    public IReadOnlyList<Expression> Expressions { get; }
    public IReadOnlyList<bool> IsAscending { get; }

    public LogicalOrderBy(IReadOnlyList<Expression> expressions, IReadOnlyList<bool> isAscending, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_ORDER_BY, child)
    {
        Expressions = expressions;
        IsAscending = isAscending;
    }

    public override string GetExpressionsForPrinting()
    {
        return string.Empty;
    }
}
