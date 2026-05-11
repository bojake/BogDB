using System.Collections.Generic;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical mark join operator for semi-join (EXISTS) and anti-join (NOT EXISTS)
/// lowering. The build side is the subquery plan; the probe side is the outer
/// query plan. The mark variable is injected into the output as a boolean.
///
/// C++ parity: LogicalHashJoin with JoinType::MARK.
/// </summary>
public sealed class LogicalMarkJoin : LogicalOperator
{
    public IReadOnlyList<string> SharedVariables { get; }
    public string MarkVariableName { get; }

    public LogicalMarkJoin(
        LogicalOperator probeSide,
        LogicalOperator buildSide,
        IReadOnlyList<string> sharedVariables,
        string markVariableName)
        : base(LogicalOperatorType.LOGICAL_MARK_JOIN, probeSide, buildSide)
    {
        SharedVariables = sharedVariables;
        MarkVariableName = markVariableName;
    }

    public override string GetExpressionsForPrinting()
        => $"MARK_JOIN ({string.Join(", ", SharedVariables)}) -> {MarkVariableName}";
}
