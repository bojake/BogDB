namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical operator that produces exactly one empty tuple as input to a pure-scalar RETURN clause.
/// Used for bare `RETURN expr` queries that have no MATCH or other reading source.
/// C++ parity: equivalent to the virtual "DUAL" / single-row source in bogdb-cpp.
/// </summary>
public sealed class LogicalSingleRow : LogicalOperator
{
    public LogicalSingleRow() : base(LogicalOperatorType.LOGICAL_SINGLE_ROW) { }

    public override string GetExpressionsForPrinting() => string.Empty;
}
