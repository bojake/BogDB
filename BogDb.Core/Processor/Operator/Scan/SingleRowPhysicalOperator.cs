namespace BogDb.Core.Processor.Operator.Scan;

/// <summary>
/// Physical operator that emits exactly one empty tuple.
/// Used as the source for bare `RETURN expr` queries with no MATCH or reading clause.
/// C++ parity: equivalent to bogdb-cpp's EXPRESSIONS_SCAN dual-table source.
/// </summary>
public sealed class SingleRowPhysicalOperator : PhysicalOperator
{
    private bool _emitted;

    public SingleRowPhysicalOperator(uint id)
        : base(PhysicalOperatorType.EXPRESSIONS_SCAN, id)
    {
        _emitted = false;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_emitted) return false;
        _emitted = true;
        // Emit a single empty tuple — downstream PROJECTION evaluates literal expressions
        // against this empty context (no CurrentNodeProperties needed for scalar RETURN).
        return true;
    }
}
