using System.Collections.Generic;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalValueJoin : LogicalOperator
{
    public IReadOnlyList<string> SharedVariables { get; }

    public LogicalValueJoin(LogicalOperator left, LogicalOperator right, IReadOnlyList<string> sharedVariables)
        : base(LogicalOperatorType.LOGICAL_HASH_JOIN, left, right)
    {
        SharedVariables = sharedVariables;
    }

    public override string GetExpressionsForPrinting()
        => $"VALUE_JOIN ({string.Join(", ", SharedVariables)})";
}
