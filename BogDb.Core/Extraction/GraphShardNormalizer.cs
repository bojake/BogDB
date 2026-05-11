namespace BogDb.Core.Extraction;

/// <summary>
/// Produces a canonical, deterministic GraphShard shape for transport and runtime reuse.
/// </summary>
public static class GraphShardNormalizer
{
    public static GraphShard Normalize(GraphShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);

        var normalizedNodeTables = shard.NodeTables
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => NormalizeNodeTable(kvp.Key, kvp.Value),
                StringComparer.Ordinal);

        var relLookup = new Dictionary<string, RelShardRow>(StringComparer.Ordinal);
        var relEndpointLookup = new Dictionary<string, (string SourceNodeId, string TargetNodeId)>(StringComparer.Ordinal);
        var normalizedRelTables = shard.RelTables
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToDictionary(
                kvp => kvp.Key,
                kvp =>
                {
                    var normalized = NormalizeRelTable(kvp.Key, kvp.Value);
                    foreach (var row in normalized.Rows)
                    {
                        relLookup[row.RelId] = row;
                        relEndpointLookup[row.RelId] = (row.SourceNodeId, row.TargetNodeId);
                    }

                    return normalized;
                },
                StringComparer.Ordinal);

        var normalizedBoundary = NormalizeBoundary(shard.Boundary);
        var normalized = new GraphShard
        {
            FormatVersion = string.IsNullOrWhiteSpace(shard.FormatVersion) ? GraphShard.CurrentFormatVersion : shard.FormatVersion,
            GraphVersionToken = shard.GraphVersionToken,
            ExtractorVersion = string.IsNullOrWhiteSpace(shard.ExtractorVersion) ? GraphShard.CurrentExtractorVersion : shard.ExtractorVersion,
            ExtractedAtUtc = shard.ExtractedAtUtc,
            ExtractionPolicy = shard.ExtractionPolicy,
            IsComplete = shard.IsComplete,
            NodeTables = normalizedNodeTables,
            RelTables = normalizedRelTables,
            Adjacency = NormalizeAdjacency(shard.Adjacency, relLookup, relEndpointLookup),
            SeedProvenance = NormalizeSeedProvenance(shard.SeedProvenance),
            Boundary = normalizedBoundary,
            Stats = NormalizeStats(shard.Stats, normalizedNodeTables, normalizedRelTables, normalizedBoundary),
            Options = NormalizeOptions(shard.Options),
            Metadata = NormalizeMetadata(shard.Metadata)
        };

        return normalized;
    }

    private static NodeShardTable NormalizeNodeTable(string key, NodeShardTable table)
    {
        var tableName = string.IsNullOrWhiteSpace(table.TableName) ? key : table.TableName;
        var columns = table.PropertyColumns
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static c => c, StringComparer.Ordinal)
            .ToList();

        var rows = table.Rows
            .Select(NormalizeNodeRow)
            .OrderBy(static row => row.ExternalId, StringComparer.Ordinal)
            .ToList();

        return new NodeShardTable
        {
            TableName = tableName,
            PropertyColumns = columns,
            Rows = rows
        };
    }

    private static NodeShardRow NormalizeNodeRow(NodeShardRow row)
        => new()
        {
            ExternalId = row.ExternalId,
            Properties = NormalizeMetadata(row.Properties)
        };

    private static RelShardTable NormalizeRelTable(string key, RelShardTable table)
    {
        var relType = string.IsNullOrWhiteSpace(table.RelType) ? key : table.RelType;
        var columns = table.PropertyColumns
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static c => c, StringComparer.Ordinal)
            .ToList();

        var rows = table.Rows
            .Select(NormalizeRelRow)
            .OrderBy(static row => row.RelId, StringComparer.Ordinal)
            .ToList();

        return new RelShardTable
        {
            RelType = relType,
            FromTable = table.FromTable,
            ToTable = table.ToTable,
            PropertyColumns = columns,
            Rows = rows
        };
    }

    private static RelShardRow NormalizeRelRow(RelShardRow row)
        => new()
        {
            RelId = row.RelId,
            SourceNodeId = row.SourceNodeId,
            TargetNodeId = row.TargetNodeId,
            Properties = NormalizeMetadata(row.Properties)
        };

    private static GraphShardAdjacency NormalizeAdjacency(
        GraphShardAdjacency adjacency,
        IReadOnlyDictionary<string, RelShardRow> relLookup,
        IReadOnlyDictionary<string, (string SourceNodeId, string TargetNodeId)> relEndpointLookup)
    {
        return new GraphShardAdjacency
        {
            Outgoing = NormalizeAdjacencyMap(adjacency.Outgoing, relLookup, relEndpointLookup, isOutgoing: true),
            Incoming = NormalizeAdjacencyMap(adjacency.Incoming, relLookup, relEndpointLookup, isOutgoing: false)
        };
    }

    private static Dictionary<string, List<ShardEdgeRef>> NormalizeAdjacencyMap(
        Dictionary<string, List<ShardEdgeRef>> map,
        IReadOnlyDictionary<string, RelShardRow> relLookup,
        IReadOnlyDictionary<string, (string SourceNodeId, string TargetNodeId)> relEndpointLookup,
        bool isOutgoing)
    {
        var result = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal);
        foreach (var kvp in map.OrderBy(static kvp => kvp.Key, StringComparer.Ordinal))
        {
            var refs = kvp.Value
                .Select(refRow => NormalizeEdgeRef(refRow, relLookup, relEndpointLookup, kvp.Key, isOutgoing))
                .OrderBy(static r => r.RelType, StringComparer.Ordinal)
                .ThenBy(static r => r.RelId, StringComparer.Ordinal)
                .ThenBy(static r => r.NeighborNodeId, StringComparer.Ordinal)
                .ThenBy(static r => r.Direction, StringComparer.Ordinal)
                .ToList();
            result[kvp.Key] = refs;
        }

        return result;
    }

    private static ShardEdgeRef NormalizeEdgeRef(
        ShardEdgeRef edgeRef,
        IReadOnlyDictionary<string, RelShardRow> relLookup,
        IReadOnlyDictionary<string, (string SourceNodeId, string TargetNodeId)> relEndpointLookup,
        string ownerNodeId,
        bool isOutgoing)
    {
        if (relLookup.TryGetValue(edgeRef.RelId, out var relRow))
        {
            var direction = isOutgoing ? "out" : "in";
            var neighborNodeId = isOutgoing ? relRow.TargetNodeId : relRow.SourceNodeId;
            if (relEndpointLookup.TryGetValue(edgeRef.RelId, out var endpoints))
            {
                var expectedOwner = isOutgoing ? endpoints.SourceNodeId : endpoints.TargetNodeId;
                if (string.Equals(expectedOwner, ownerNodeId, StringComparison.Ordinal))
                {
                    return new ShardEdgeRef
                    {
                        RelId = edgeRef.RelId,
                        RelType = string.IsNullOrWhiteSpace(edgeRef.RelType) ? relRow.Properties.GetValueOrDefault("_label")?.ToString() ?? edgeRef.RelType : edgeRef.RelType,
                        NeighborNodeId = neighborNodeId,
                        Direction = direction
                    };
                }
            }
        }

        return new ShardEdgeRef
        {
            RelId = edgeRef.RelId,
            RelType = edgeRef.RelType,
            NeighborNodeId = edgeRef.NeighborNodeId,
            Direction = string.IsNullOrWhiteSpace(edgeRef.Direction) ? (isOutgoing ? "out" : "in") : edgeRef.Direction
        };
    }

    private static GraphShardSeedProvenance NormalizeSeedProvenance(GraphShardSeedProvenance provenance)
    {
        var seeds = provenance.RequestedSeeds
            .Select(seed => new GraphShardSeedRecord
            {
                TableName = seed.TableName,
                RequestedNodeId = seed.RequestedNodeId,
                ExternalId = seed.ExternalId,
                Status = seed.Status,
                Reason = seed.Reason
            })
            .OrderBy(static seed => seed.TableName, StringComparer.Ordinal)
            .ThenBy(static seed => seed.RequestedNodeId, StringComparer.Ordinal)
            .ToList();

        var includedCount = seeds.Count(static seed => string.Equals(seed.Status, "included", StringComparison.Ordinal));
        var excludedCount = seeds.Count - includedCount;

        return new GraphShardSeedProvenance
        {
            RequestedCount = seeds.Count,
            IncludedCount = includedCount,
            ExcludedCount = excludedCount,
            RequestedSeeds = seeds
        };
    }

    private static GraphShardBoundary NormalizeBoundary(GraphShardBoundary boundary)
    {
        var reasons = boundary.TruncationReasons
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static reason => reason, StringComparer.Ordinal)
            .ToList();
        var boundaryNodeIds = boundary.BoundaryNodeIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToList();

        var fetchHintReasons = boundary.FetchHints.Reasons
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static reason => reason, StringComparer.Ordinal)
            .ToList();
        var recommendedSeeds = boundary.FetchHints.RecommendedSeedNodeIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToList();

        return new GraphShardBoundary
        {
            IsTruncated = boundary.IsTruncated,
            HasNodeBoundary = boundary.HasNodeBoundary,
            HasEdgeBoundary = boundary.HasEdgeBoundary,
            TruncatedByNodeLimit = boundary.TruncatedByNodeLimit,
            TruncatedByEdgeLimit = boundary.TruncatedByEdgeLimit,
            TruncatedByDepth = boundary.TruncatedByDepth,
            TruncationReasons = reasons,
            BoundaryNodeIds = boundaryNodeIds,
            FetchHints = new GraphShardBoundaryFetchHints
            {
                ShouldFetchMore = boundary.FetchHints.ShouldFetchMore,
                CanResumeFromBoundary = boundary.FetchHints.CanResumeFromBoundary,
                RecommendedSeedNodeIds = recommendedSeeds,
                SuggestedMaxDepth = boundary.FetchHints.SuggestedMaxDepth,
                SuggestedMaxNodes = boundary.FetchHints.SuggestedMaxNodes,
                SuggestedMaxEdges = boundary.FetchHints.SuggestedMaxEdges,
                Reasons = fetchHintReasons
            }
        };
    }

    private static GraphShardStats NormalizeStats(
        GraphShardStats stats,
        IReadOnlyDictionary<string, NodeShardTable> nodeTables,
        IReadOnlyDictionary<string, RelShardTable> relTables,
        GraphShardBoundary boundary)
    {
        var nodeCount = nodeTables.Sum(static kvp => kvp.Value.Rows.Count);
        var edgeCount = relTables.Sum(static kvp => kvp.Value.Rows.Count);
        return new GraphShardStats
        {
            NodeCount = nodeCount,
            EdgeCount = edgeCount,
            BoundaryNodeCount = boundary.BoundaryNodeIds.Count,
            NodeTableCount = nodeTables.Count,
            RelTableCount = relTables.Count
        };
    }

    private static GraphShardExtractionOptions NormalizeOptions(GraphShardExtractionOptions options)
        => new()
        {
            MaxDepth = options.MaxDepth,
            MaxNodes = options.MaxNodes,
            MaxEdges = options.MaxEdges,
            IncludeOutgoing = options.IncludeOutgoing,
            IncludeIncoming = options.IncludeIncoming,
            IncludeNodeProperties = options.IncludeNodeProperties,
            IncludeRelProperties = options.IncludeRelProperties,
            IncludeAdjacency = options.IncludeAdjacency,
            IncludeBoundaryMetadata = options.IncludeBoundaryMetadata,
            StopAtBoundary = options.StopAtBoundary,
            RelTypes = options.RelTypes
                .Where(static relType => !string.IsNullOrWhiteSpace(relType))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static relType => relType, StringComparer.Ordinal)
                .ToList(),
            NodeTables = options.NodeTables
                .Where(static table => !string.IsNullOrWhiteSpace(table))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static table => table, StringComparer.Ordinal)
                .ToList()
        };

    private static Dictionary<string, object?> NormalizeMetadata(IReadOnlyDictionary<string, object?> metadata)
        => metadata
            .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
}
