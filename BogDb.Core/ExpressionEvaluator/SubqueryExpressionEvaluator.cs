using System;
using BogDb.Core.Binder;
using BogDb.Core.Processor;
using BogDb.Core.Parser;
using BogDb.Core.Processor.Operator;
using BogDb.Core.Common;

using ExecutionContext = BogDb.Core.Processor.ExecutionContext;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Evaluates EXISTS { MATCH ... } or COUNT { MATCH ... } dynamically by initializing a subquery pipeline execution.
/// </summary>
public sealed class SubqueryExpressionEvaluator : ExpressionEvaluator
{
    private readonly PhysicalOperator _subqueryPlan;
    private readonly SubqueryType _subqueryType;
    private ExecutionContext? _context; // Required by physical engine

    public SubqueryExpressionEvaluator(PhysicalOperator subqueryPlan, SubqueryType subqueryType)
        : base(true)
    {
        _subqueryPlan = subqueryPlan;
        _subqueryType = subqueryType;
    }

    public void InitContext(ExecutionContext context)
    {
        _context = context;
        // In physical planning, native subqueries are parameterized.
    }

    public override bool Evaluate()
    {
        if (_context == null) throw new InvalidOperationException("Subqueries require ExecutionContext during evaluation.");
        
        long count = 0;
        while (_subqueryPlan.GetNextTuple(_context))
        {
            count++;
            if (_subqueryType == SubqueryType.EXISTS)
            {
                // Found at least one tuple, EXISTS is true.
                break;
            }
        }

        // Initialize missing ResultVector payload sizes mimicking evaluate loops seamlessly natively.
        if (ResultVector == null)
            ResultVector = new ValueVector(_subqueryType == SubqueryType.EXISTS ? LogicalTypeID.BOOL : LogicalTypeID.INT64);

        if (_subqueryType == SubqueryType.EXISTS)
            ResultVector.SetValue(0, count > 0);
        else
            ResultVector.SetValue(0, count);

        return true;
    }

    public override bool Select(ref SelectionVector selVector)
    {
        Evaluate();
        return _subqueryType == SubqueryType.EXISTS ? ResultVector!.GetValue<bool>(0) : ResultVector!.GetValue<long>(0) > 0;
    }
}
