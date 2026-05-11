using System.Collections.Generic;
using BogDb.Core.Binder;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.Scan;

/// <summary>
/// Source operator for pure-expression queries (e.g., RETURN 1+2, 'hello').
/// Evaluates a list of expressions once and emits a single projection row.
/// C++ parity: expressions_scan.h
/// </summary>
public sealed class PhysicalExpressionsScan : PhysicalOperator
{
    private readonly IReadOnlyList<Expression> _expressions;
    private bool _emitted;

    public PhysicalExpressionsScan(IReadOnlyList<Expression> expressions, uint id)
        : base(PhysicalOperatorType.EXPRESSIONS_SCAN, id)
    {
        _expressions = expressions;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_emitted) return false;
        _emitted = true;

        var values = new object?[_expressions.Count];
        for (int i = 0; i < _expressions.Count; i++)
        {
            values[i] = TypeCoercionHelper.Normalize(
                ExpressionExecutionHelper.Evaluate(_expressions[i], context));
        }
        context.CurrentProjectionRow = values;
        return true;
    }
}
