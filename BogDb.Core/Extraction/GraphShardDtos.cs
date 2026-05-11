namespace BogDb.Core.Extraction;

/// <summary>
/// Transport-safe extracted graph payload independent of mutable storage objects.
/// </summary>
public sealed record class GraphShard
{
    public const string CurrentFormatVersion = "2";
    public const string CurrentExtractorVersion = "2";

    public string FormatVersion { get; init; } = CurrentFormatVersion;
    public string? GraphVersionToken { get; init; }
    public string ExtractorVersion { get; init; } = CurrentExtractorVersion;
    public string ExtractedAtUtc { get; init; } = string.Empty;
    public string ExtractionPolicy { get; init; } = string.Empty;
    public bool IsComplete { get; init; } = true;
    public Dictionary<string, NodeShardTable> NodeTables { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, RelShardTable> RelTables { get; init; } = new(StringComparer.Ordinal);
    public GraphShardAdjacency Adjacency { get; init; } = new();
    public GraphShardSeedProvenance SeedProvenance { get; init; } = new();
    public GraphShardBoundary Boundary { get; init; } = new();
    public GraphShardStats Stats { get; init; } = new();
    public GraphShardExtractionOptions Options { get; init; } = new();
    public Dictionary<string, object?> Metadata { get; init; } = new(StringComparer.Ordinal);
}

public sealed record class NodeShardTable
{
    public string TableName { get; init; } = string.Empty;
    public List<string> PropertyColumns { get; set; } = [];
    public List<NodeShardRow> Rows { get; init; } = [];
}

public sealed record class NodeShardRow
{
    public string ExternalId { get; init; } = string.Empty;
    public Dictionary<string, object?> Properties { get; init; } = new(StringComparer.Ordinal);
}

public sealed record class RelShardTable
{
    public string RelType { get; init; } = string.Empty;
    public string FromTable { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty;
    public List<string> PropertyColumns { get; set; } = [];
    public List<RelShardRow> Rows { get; init; } = [];
}

public sealed record class RelShardRow
{
    public string RelId { get; init; } = string.Empty;
    public string SourceNodeId { get; init; } = string.Empty;
    public string TargetNodeId { get; init; } = string.Empty;
    public Dictionary<string, object?> Properties { get; init; } = new(StringComparer.Ordinal);
}

public sealed record class GraphShardAdjacency
{
    public Dictionary<string, List<ShardEdgeRef>> Outgoing { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<ShardEdgeRef>> Incoming { get; init; } = new(StringComparer.Ordinal);
}

public sealed record class GraphShardSeedProvenance
{
    public int RequestedCount { get; init; }
    public int IncludedCount { get; init; }
    public int ExcludedCount { get; init; }
    public List<GraphShardSeedRecord> RequestedSeeds { get; init; } = [];
}

public sealed record class GraphShardSeedRecord
{
    public string TableName { get; init; } = string.Empty;
    public string RequestedNodeId { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record class ShardEdgeRef
{
    public string RelId { get; init; } = string.Empty;
    public string RelType { get; init; } = string.Empty;
    public string NeighborNodeId { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
}

public sealed record class GraphShardBoundary
{
    public bool IsTruncated { get; init; }
    public bool HasNodeBoundary { get; init; }
    public bool HasEdgeBoundary { get; init; }
    public bool TruncatedByNodeLimit { get; init; }
    public bool TruncatedByEdgeLimit { get; init; }
    public bool TruncatedByDepth { get; init; }
    public List<string> TruncationReasons { get; init; } = [];
    public List<string> BoundaryNodeIds { get; init; } = [];
    public GraphShardBoundaryFetchHints FetchHints { get; init; } = new();
}

public sealed record class GraphShardBoundaryFetchHints
{
    public bool ShouldFetchMore { get; init; }
    public bool CanResumeFromBoundary { get; init; }
    public List<string> RecommendedSeedNodeIds { get; init; } = [];
    public int? SuggestedMaxDepth { get; init; }
    public int? SuggestedMaxNodes { get; init; }
    public int? SuggestedMaxEdges { get; init; }
    public List<string> Reasons { get; init; } = [];
}

public sealed record class GraphShardStats
{
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public int BoundaryNodeCount { get; init; }
    public int NodeTableCount { get; init; }
    public int RelTableCount { get; init; }
}

public sealed record class GraphShardExtractionOptions
{
    public int? MaxDepth { get; init; }
    public int? MaxNodes { get; init; }
    public int? MaxEdges { get; init; }
    public bool IncludeOutgoing { get; init; } = true;
    public bool IncludeIncoming { get; init; } = true;
    public bool IncludeNodeProperties { get; init; } = true;
    public bool IncludeRelProperties { get; init; } = true;
    public bool IncludeAdjacency { get; init; } = true;
    public bool IncludeBoundaryMetadata { get; init; } = true;
    public bool StopAtBoundary { get; init; } = true;
    public List<string> RelTypes { get; init; } = [];
    public List<string> NodeTables { get; init; } = [];
}

public sealed record class GraphNodeSelector
{
    public string TableName { get; init; } = string.Empty;
    public object NodeId { get; init; } = string.Empty;
}
