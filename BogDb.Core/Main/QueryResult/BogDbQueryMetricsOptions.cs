namespace BogDb.Core.Main.QueryResult;

/// <summary>
/// Controls optional runtime metrics capture for a single query execution.
/// </summary>
public sealed record BogDbQueryMetricsOptions
{
    public bool CaptureExecutionMetrics { get; init; }
}
