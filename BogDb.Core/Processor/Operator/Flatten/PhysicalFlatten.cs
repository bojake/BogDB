namespace BogDb.Core.Processor.Operator.Flatten;

/// <summary>
/// Pipeline marker operator: passes tuples through from child unchanged.
/// In the C++ engine, FLATTEN materialises multi-valued results (e.g., from
/// RecursiveExtend) into flat single-output rows. In the C# port, the child
/// operators already emit one row per call, so Flatten is a thin pass-through
/// that preserves plan shape for optimizer correctness.
/// C++ parity: flatten.h
/// </summary>
public sealed class PhysicalFlatten : PhysicalOperator
{
    public PhysicalFlatten(PhysicalOperator child, uint id)
        : base(PhysicalOperatorType.FLATTEN, child, id)
    {
    }

    public override bool GetNextTuple(ExecutionContext context)
        => Children[0].GetNextTuple(context);
}
