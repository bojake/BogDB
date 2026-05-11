namespace BogDb.Core.Extraction;

/// <summary>
/// Validates extracted graph payloads before transport or runtime consumption.
/// </summary>
public static class GraphShardValidator
{
    public static void Validate(GraphShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);

        if (string.IsNullOrWhiteSpace(shard.FormatVersion))
        {
            throw new GraphShardValidationException("GraphShard.FormatVersion is required.");
        }

        if (string.IsNullOrWhiteSpace(shard.ExtractorVersion))
        {
            throw new GraphShardValidationException("GraphShard.ExtractorVersion is required.");
        }

        var knownNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in shard.NodeTables)
        {
            if (!string.Equals(kvp.Key, kvp.Value.TableName, StringComparison.Ordinal))
            {
                throw new GraphShardValidationException(
                    $"Node table key '{kvp.Key}' does not match NodeShardTable.TableName '{kvp.Value.TableName}'.");
            }

            foreach (var row in kvp.Value.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.ExternalId))
                {
                    throw new GraphShardValidationException($"Node row in table '{kvp.Key}' has an empty ExternalId.");
                }

                if (!knownNodeIds.Add(row.ExternalId))
                {
                    throw new GraphShardValidationException($"Duplicate node ExternalId '{row.ExternalId}' found in shard.");
                }
            }
        }

        var relById = new Dictionary<string, (string RelType, string SourceNodeId, string TargetNodeId)>(StringComparer.Ordinal);
        foreach (var kvp in shard.RelTables)
        {
            if (!string.Equals(kvp.Key, kvp.Value.RelType, StringComparison.Ordinal))
            {
                throw new GraphShardValidationException(
                    $"Relationship table key '{kvp.Key}' does not match RelShardTable.RelType '{kvp.Value.RelType}'.");
            }

            foreach (var row in kvp.Value.Rows)
            {
                if (string.IsNullOrWhiteSpace(row.RelId))
                {
                    throw new GraphShardValidationException($"Relationship row in rel table '{kvp.Key}' has an empty RelId.");
                }

                if (!knownNodeIds.Contains(row.SourceNodeId))
                {
                    throw new GraphShardValidationException(
                        $"Relationship '{row.RelId}' references missing source node '{row.SourceNodeId}'.");
                }

                if (!knownNodeIds.Contains(row.TargetNodeId))
                {
                    throw new GraphShardValidationException(
                        $"Relationship '{row.RelId}' references missing target node '{row.TargetNodeId}'.");
                }

                if (!relById.TryAdd(row.RelId, (kvp.Value.RelType, row.SourceNodeId, row.TargetNodeId)))
                {
                    throw new GraphShardValidationException($"Duplicate relationship RelId '{row.RelId}' found in shard.");
                }
            }
        }

        ValidateAdjacencyMap(shard.Adjacency.Outgoing, relById, knownNodeIds, isOutgoing: true);
        ValidateAdjacencyMap(shard.Adjacency.Incoming, relById, knownNodeIds, isOutgoing: false);

        if (shard.Stats.NodeCount != knownNodeIds.Count)
        {
            throw new GraphShardValidationException(
                $"GraphShard.Stats.NodeCount ({shard.Stats.NodeCount}) does not match materialized node count ({knownNodeIds.Count}).");
        }

        if (shard.Stats.EdgeCount != relById.Count)
        {
            throw new GraphShardValidationException(
                $"GraphShard.Stats.EdgeCount ({shard.Stats.EdgeCount}) does not match materialized edge count ({relById.Count}).");
        }

        if (shard.Stats.NodeTableCount != shard.NodeTables.Count)
        {
            throw new GraphShardValidationException(
                $"GraphShard.Stats.NodeTableCount ({shard.Stats.NodeTableCount}) does not match node table count ({shard.NodeTables.Count}).");
        }

        if (shard.Stats.RelTableCount != shard.RelTables.Count)
        {
            throw new GraphShardValidationException(
                $"GraphShard.Stats.RelTableCount ({shard.Stats.RelTableCount}) does not match relationship table count ({shard.RelTables.Count}).");
        }

        if (shard.Stats.BoundaryNodeCount != shard.Boundary.BoundaryNodeIds.Count)
        {
            throw new GraphShardValidationException(
                $"GraphShard.Stats.BoundaryNodeCount ({shard.Stats.BoundaryNodeCount}) does not match boundary node count ({shard.Boundary.BoundaryNodeIds.Count}).");
        }

        if (shard.SeedProvenance.RequestedCount != shard.SeedProvenance.RequestedSeeds.Count)
        {
            throw new GraphShardValidationException(
                $"GraphShard.SeedProvenance.RequestedCount ({shard.SeedProvenance.RequestedCount}) does not match requested seed records ({shard.SeedProvenance.RequestedSeeds.Count}).");
        }

        var includedCount = shard.SeedProvenance.RequestedSeeds.Count(static seed => string.Equals(seed.Status, "included", StringComparison.Ordinal));
        if (shard.SeedProvenance.IncludedCount != includedCount)
        {
            throw new GraphShardValidationException(
                $"GraphShard.SeedProvenance.IncludedCount ({shard.SeedProvenance.IncludedCount}) does not match included seed records ({includedCount}).");
        }

        var excludedCount = shard.SeedProvenance.RequestedSeeds.Count - includedCount;
        if (shard.SeedProvenance.ExcludedCount != excludedCount)
        {
            throw new GraphShardValidationException(
                $"GraphShard.SeedProvenance.ExcludedCount ({shard.SeedProvenance.ExcludedCount}) does not match excluded seed records ({excludedCount}).");
        }
    }

    private static void ValidateAdjacencyMap(
        IReadOnlyDictionary<string, List<ShardEdgeRef>> map,
        IReadOnlyDictionary<string, (string RelType, string SourceNodeId, string TargetNodeId)> relById,
        IReadOnlySet<string> knownNodeIds,
        bool isOutgoing)
    {
        foreach (var kvp in map)
        {
            if (!knownNodeIds.Contains(kvp.Key))
            {
                throw new GraphShardValidationException(
                    $"Adjacency entry references missing owner node '{kvp.Key}'.");
            }

            foreach (var edgeRef in kvp.Value)
            {
                if (!relById.TryGetValue(edgeRef.RelId, out var rel))
                {
                    throw new GraphShardValidationException(
                        $"Adjacency entry for node '{kvp.Key}' references missing relationship '{edgeRef.RelId}'.");
                }

                if (!knownNodeIds.Contains(edgeRef.NeighborNodeId))
                {
                    throw new GraphShardValidationException(
                        $"Adjacency entry for node '{kvp.Key}' references missing neighbor node '{edgeRef.NeighborNodeId}'.");
                }

                var expectedOwner = isOutgoing ? rel.SourceNodeId : rel.TargetNodeId;
                var expectedNeighbor = isOutgoing ? rel.TargetNodeId : rel.SourceNodeId;
                var expectedDirection = isOutgoing ? "out" : "in";

                if (!string.Equals(kvp.Key, expectedOwner, StringComparison.Ordinal))
                {
                    throw new GraphShardValidationException(
                        $"Adjacency entry for node '{kvp.Key}' does not match relationship '{edgeRef.RelId}' owner endpoint.");
                }

                if (!string.Equals(edgeRef.NeighborNodeId, expectedNeighbor, StringComparison.Ordinal))
                {
                    throw new GraphShardValidationException(
                        $"Adjacency entry for relationship '{edgeRef.RelId}' has neighbor '{edgeRef.NeighborNodeId}' but expected '{expectedNeighbor}'.");
                }

                if (!string.IsNullOrWhiteSpace(edgeRef.RelType) &&
                    !string.Equals(edgeRef.RelType, rel.RelType, StringComparison.Ordinal))
                {
                    throw new GraphShardValidationException(
                        $"Adjacency entry for relationship '{edgeRef.RelId}' has rel type '{edgeRef.RelType}' but expected '{rel.RelType}'.");
                }

                if (!string.IsNullOrWhiteSpace(edgeRef.Direction) &&
                    !string.Equals(edgeRef.Direction, expectedDirection, StringComparison.Ordinal))
                {
                    throw new GraphShardValidationException(
                        $"Adjacency entry for relationship '{edgeRef.RelId}' has direction '{edgeRef.Direction}' but expected '{expectedDirection}'.");
                }
            }
        }
    }
}
