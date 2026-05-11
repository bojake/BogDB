using System.Collections.Generic;
using BogDb.Core.GraphDataScience;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical operator that scans the active frontier of a GDS algorithm iteration.
/// Produces one tuple per active node in the current frontier, feeding it into
/// downstream EXTEND or algorithm-specific compute operators.
///
/// C++ parity: <c>src/include/processor/operator/gds_call.h</c> — the C++ pipeline
/// uses an internal frontier scan within the GDS task. BogDB exposes this as an
/// explicit logical operator so the full pipeline is visible via EXPLAIN.
///
/// Usage context:
///   SCAN_FRONTIER → EXTEND → algorithm compute → result collect
/// </summary>
public sealed class LogicalScanFrontier : LogicalOperator
{
    /// <summary>The node table(s) being scanned from the frontier.</summary>
    public IReadOnlyList<string> TableNames { get; }

    /// <summary>Variable name for the frontier node (e.g. "n").</summary>
    public string VariableName { get; }

    /// <summary>
    /// Optional external frontier reference. When null, the physical operator
    /// creates its own frontier from the algorithm's seed nodes.
    /// </summary>
    public GdsFrontier? Frontier { get; }

    public LogicalScanFrontier(
        string variableName,
        IReadOnlyList<string> tableNames,
        GdsFrontier? frontier = null)
        : base(LogicalOperatorType.LOGICAL_SCAN_FRONTIER)
    {
        VariableName = variableName;
        TableNames = tableNames;
        Frontier = frontier;
    }

    public LogicalScanFrontier(string variableName, string tableName, GdsFrontier? frontier = null)
        : this(variableName, new List<string> { tableName }, frontier)
    {
    }

    public override string GetExpressionsForPrinting()
        => $"SCAN_FRONTIER {VariableName} FROM [{string.Join("|", TableNames)}]";
}
