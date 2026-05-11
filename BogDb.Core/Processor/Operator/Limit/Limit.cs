using System;
using BogDb.Core.Common;
using BogDb.Core.Binder;
using BogDb.Core.ExpressionEvaluator;

namespace BogDb.Core.Processor.Operator.Limit;

/// <summary>
/// A terminal Physical Operator filtering the resulting graph chunks stream up to a constant count.
/// Supports SKIP and LIMIT pipelines.
/// </summary>
public sealed class Limit : PhysicalOperator
{
    private readonly PhysicalOperator _child;
    private readonly Expression _limitExpression;
    
    private bool _isEvaluated;
    private ulong _limitNumber;
    private ulong _currentYielded;

    public Limit(
        PhysicalOperator child, 
        Expression limitExpression,
        uint id) 
        : base(PhysicalOperatorType.LIMIT, id)
    {
        _child = child;
        _limitExpression = limitExpression;
        
        _isEvaluated = false;
        _currentYielded = 0;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!_isEvaluated)
        {
            var result = ExpressionExecutionHelper.Evaluate(_limitExpression, context);
            long count = result is IConvertible c ? c.ToInt64(null) : 0;
            
            _limitNumber = count > 0 ? (ulong)count : 0;
            _isEvaluated = true;
        }

        if (_currentYielded >= _limitNumber)
        {
            return false;
        }

        if (_child.GetNextTuple(context))
        {
            _currentYielded++;
            return true;
        }

        return false;
    }
}
