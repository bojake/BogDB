using System.Diagnostics;
using BogDb.Core.Common.Profiler;

namespace BogDb.Core.Processor.Operator.Profile;

/// <summary>
/// Lightweight profile operator that measures total execution time of its child.
/// </summary>
public sealed class PhysicalProfile : PhysicalOperator
{
    private readonly PhysicalOperator _child;
    private readonly Profiler _profiler;
    private bool _started;
    private bool _completed;

    public PhysicalProfile(PhysicalOperator child, uint id)
        : base(PhysicalOperatorType.PROFILE, id)
    {
        _child = child;
        _profiler = new Profiler();
        Children.Add(child);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_completed) return false;

        if (!_started)
        {
            _started = true;
            _profiler.Start();
        }

        var hasTuple = _child.GetNextTuple(context);
        if (!hasTuple)
        {
            _profiler.Stop();
            if (context.CurrentScalarBindings == null)
                context.CurrentScalarBindings = new System.Collections.Generic.Dictionary<string, object?>();
            context.CurrentScalarBindings["__profile_ms"] = _profiler.GetElapsedTimeMs();
            _completed = true;
        }
        return hasTuple;
    }
}
