using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.Accumulate;

/// <summary>
/// Materializes child tuples into memory and replays them on demand.
/// This is a simplified analogue of BogDb's Accumulate + FTableScan pipeline.
/// </summary>
public sealed class PhysicalAccumulate : PhysicalOperator
{
    private readonly PhysicalOperator _child;
    private readonly AccumulateType _accumulateType;
    private readonly List<ExecutionState> _buffer;
    private bool _initialized;
    private int _index;

    public PhysicalAccumulate(
        AccumulateType accumulateType,
        PhysicalOperator child,
        uint id)
        : base(PhysicalOperatorType.ACCUMULATE, id)
    {
        _accumulateType = accumulateType;
        _child = child;
        _buffer = new List<ExecutionState>();
        Children.Add(child);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!_initialized)
        {
            _initialized = true;
            while (_child.GetNextTuple(context))
            {
                _buffer.Add(context.CaptureState());
            }

            if (_accumulateType == AccumulateType.OPTIONAL && _buffer.Count == 0)
            {
                _buffer.Add(new ExecutionState());
            }
        }

        if (_index >= _buffer.Count)
            return false;

        context.RestoreState(_buffer[_index++]);
        return true;
    }
}
