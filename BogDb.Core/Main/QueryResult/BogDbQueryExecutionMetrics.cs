namespace BogDb.Core.Main.QueryResult;

/// <summary>
/// Optional host-facing execution metrics for a single query.
/// The first expansion slice intentionally keeps this compact.
/// </summary>
public sealed record BogDbQueryExecutionMetrics
{
    public double TotalMilliseconds { get; init; }
    public ulong RowsProduced { get; init; }
    public long BytesRead { get; init; }
    public long BytesWritten { get; init; }
    public long TempBytesWritten { get; init; }
    public int SpillCount { get; init; }
    public int SpillRunCount { get; init; }
    public int MaxMergeFanIn { get; init; }
    public int NodeReads { get; init; }
    public int RelReads { get; init; }
    public int IndexLookups { get; init; }
    public int NodesWritten { get; init; }
    public int RelationshipsWritten { get; init; }
}
