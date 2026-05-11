using System.Collections.Generic;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical COPY TO sink over a child query plan.
/// </summary>
public sealed class LogicalCopyTo : LogicalOperator
{
    public string FilePath { get; }
    public IReadOnlyList<string> ColumnNames { get; }

    public LogicalCopyTo(string filePath, IReadOnlyList<string> columnNames, LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_COPY_TO, child)
    {
        FilePath = filePath;
        ColumnNames = columnNames;
    }

    public override string GetExpressionsForPrinting()
        => $"COPY TO '{FilePath}'";
}
