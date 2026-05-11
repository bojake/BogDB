namespace BogDb.Core.Processor.Operator;

/// <summary>
/// A no-op physical operator that immediately reports no tuples.
/// Used as a placeholder until real physical operators are implemented (Phase 9).
/// Prevents NullReferenceExceptions when PlanMapper stubs return null.
/// </summary>
public sealed class NoOpPhysicalOperator : PhysicalOperator
{
    public NoOpPhysicalOperator(uint id)
        : base(PhysicalOperatorType.RESULT_COLLECTOR, id) { }

    public override bool GetNextTuple(ExecutionContext context) => false;
}
