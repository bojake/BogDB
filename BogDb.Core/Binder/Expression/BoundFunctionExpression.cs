using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Binder;

/// <summary>
/// A Bound Expression identifying explicit TableFunctions or Scalar Functions globally explicitly
/// maintaining native parameters mapping original parsed arguments safely gracefully natively.
/// </summary>
public sealed class BoundFunctionExpression : Expression
{
    public string FunctionName { get; }
    public IReadOnlyList<Expression> Arguments { get; }
    public bool IsAggregate { get; init; }
    public bool IsDistinct { get; init; }

    public BoundFunctionExpression(string functionName, IReadOnlyList<Expression> arguments, LogicalTypeID returnType)
        : base(ExpressionType.FUNCTION, returnType)
    {
        FunctionName = functionName;
        Arguments = arguments;
    }

    public override string ToString()
    {
        return $"{FunctionName}(" + string.Join(", ", Arguments) + ")";
    }
}
