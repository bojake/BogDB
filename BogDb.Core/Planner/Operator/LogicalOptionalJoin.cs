using System.Collections.Generic;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalOptionalJoin : LogicalOperator
{
    public IReadOnlyList<string> SharedVariables { get; }

    public LogicalOptionalJoin(LogicalOperator left, LogicalOperator right, IReadOnlyList<string> sharedVariables)
        : base(LogicalOperatorType.LOGICAL_OPTIONAL_JOIN, left, right)
    {
        SharedVariables = sharedVariables;
    }

    public override string GetExpressionsForPrinting()
        => $"OPTIONAL_JOIN ({string.Join(", ", SharedVariables)})";
}
