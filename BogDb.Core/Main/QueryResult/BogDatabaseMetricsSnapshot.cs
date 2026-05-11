namespace BogDb.Core.Main.QueryResult;

/// <summary>
/// Cumulative host-facing runtime metrics snapshot for a database instance.
/// Values are monotonic until <c>ResetMetrics()</c> is called.
/// </summary>
public sealed record BogDatabaseMetricsSnapshot
{
    public long QueryCount { get; init; }
    public long FailedQueryCount { get; init; }
    public long TransactionCommitCount { get; init; }
    public long TransactionRollbackCount { get; init; }
    public long BytesRead { get; init; }
    public long BytesWritten { get; init; }
    public long TempBytesWritten { get; init; }
    public long NodeReadCount { get; init; }
    public long RelReadCount { get; init; }
    public long IndexLookupCount { get; init; }
    public long NodesWritten { get; init; }
    public long RelationshipsWritten { get; init; }
    public long ExtractionCount { get; init; }
    public double TotalQueryMilliseconds { get; init; }
    public double TotalExtractionMilliseconds { get; init; }
}
