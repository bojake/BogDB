namespace BogDb.Core.Main.QueryResult;

/// <summary>
/// Lightweight host-facing summary metadata for a query result.
/// </summary>
public readonly record struct BogDbQuerySummary(
    bool IsSuccess,
    string ErrorMessage,
    int ColumnCount,
    ulong RowCount,
    double? ProfileMilliseconds,
    double? TotalMilliseconds,
    BogDbQueryExecutionMetrics? ExecutionMetrics);
