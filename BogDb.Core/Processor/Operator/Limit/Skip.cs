using System;
using BogDb.Core.Common;
using BogDb.Core.Binder;
using BogDb.Core.ExpressionEvaluator;

namespace BogDb.Core.Processor.Operator.Limit;

/// <summary>
/// A Physical Operator filtering the resulting graph chunks stream by skipping a target node count.
/// </summary>
public sealed class Skip : PhysicalOperator
{
    private readonly PhysicalOperator _child;
    private readonly Expression _skipExpression;
    
    private bool _isEvaluated;
    private ulong _skipNumber;
    private ulong _currentSkipped;

    public Skip(
        PhysicalOperator child, 
        Expression skipExpression,
        uint id) 
        : base(PhysicalOperatorType.SKIP, id) // PhysicalOperatorType.SKIP handles routing 
    {
        _child = child;
        _skipExpression = skipExpression;
        
        _isEvaluated = false;
        _currentSkipped = 0;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!_isEvaluated)
        {
            var result = ExpressionExecutionHelper.Evaluate(_skipExpression, context);
            long count = result is IConvertible c ? c.ToInt64(null) : 0;
            
            _skipNumber = count > 0 ? (ulong)count : 0;
            _isEvaluated = true;
        }

        while (_currentSkipped < _skipNumber)
        {
            if (!_child.GetNextTuple(context))
            {
                return false;
            }
            _currentSkipped++;
        }

        return _child.GetNextTuple(context);
    }
}
