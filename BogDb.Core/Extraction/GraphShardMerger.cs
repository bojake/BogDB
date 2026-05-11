namespace BogDb.Core.Extraction;

/// <summary>
/// Merges multiple normalized graph extracts into one canonical payload.
/// </summary>
public static class GraphShardMerger
{
    public static GraphShard Merge(params GraphShard[] shards)
        => Merge((IEnumerable<GraphShard>)shards);

    public static GraphShard Merge(IEnumerable<GraphShard> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);

        var normalizedShards = shards.Select(GraphShardNormalizer.Normalize).ToList();
        if (normalizedShards.Count == 0)
            return new GraphShard();

        var merged = new GraphShard
        {
            FormatVersion = GraphShard.CurrentFormatVersion,
            GraphVersionToken = MergeGraphVersionToken(normalizedShards),
            ExtractorVersion = GraphShard.CurrentExtractorVersion,
            ExtractedAtUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ExtractionPolicy = "merged",
            IsComplete = normalizedShards.All(static shard => shard.IsComplete),
            NodeTables = MergeNodeTables(normalizedShards),
            RelTables = MergeRelTables(normalizedShards),
            Adjacency = MergeAdjacency(normalizedShards),
            SeedProvenance = MergeSeedProvenance(normalizedShards),
            Boundary = MergeBoundary(normalizedShards),
            Options = MergeOptions(normalizedShards),
            Metadata = MergeMetadata(normalizedShards)
        };

        var normalized = GraphShardNormalizer.Normalize(merged);
        GraphShardValidator.Validate(normalized);
        return normalized;
    }

    private static Dictionary<string, NodeShardTable> MergeNodeTables(IEnumerable<GraphShard> shards)
    {
        var result = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal);
        foreach (var shard in shards)
        {
            foreach (var (tableName, table) in shard.NodeTables)
            {
                if (!result.TryGetValue(tableName, out var mergedTable))
                {
                    mergedTable = new NodeShardTable { TableName = tableName };
                    result[tableName] = mergedTable;
                }

                mergedTable.PropertyColumns = mergedTable.PropertyColumns
                    .Concat(table.PropertyColumns)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static c => c, StringComparer.Ordinal)
                    .ToList();

                foreach (var row in table.Rows)
                {
                    var existingIndex = mergedTable.Rows.FindIndex(r => string.Equals(r.ExternalId, row.ExternalId, StringComparison.Ordinal));
                    if (existingIndex < 0)
                    {
                        mergedTable.Rows.Add(CloneNodeRow(row));
                        continue;
                    }

                    mergedTable.Rows[existingIndex] = MergeNodeRow(mergedTable.Rows[existingIndex], row);
                }
            }
        }

        return result;
    }

    private static Dictionary<string, RelShardTable> MergeRelTables(IEnumerable<GraphShard> shards)
    {
        var result = new Dictionary<string, RelShardTable>(StringComparer.Ordinal);
        foreach (var shard in shards)
        {
            foreach (var (relType, table) in shard.RelTables)
            {
                if (!result.TryGetValue(relType, out var mergedTable))
                {
                    mergedTable = new RelShardTable
                    {
                        RelType = relType,
                        FromTable = table.FromTable,
                        ToTable = table.ToTable
                    };
                    result[relType] = mergedTable;
                }

                if (string.IsNullOrWhiteSpace(mergedTable.FromTable))
                    mergedTable.FromTable = table.FromTable;
                if (string.IsNullOrWhiteSpace(mergedTable.ToTable))
                    mergedTable.ToTable = table.ToTable;

                mergedTable.PropertyColumns = mergedTable.PropertyColumns
                    .Concat(table.PropertyColumns)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static c => c, StringComparer.Ordinal)
                    .ToList();

                foreach (var row in table.Rows)
                {
                    var existingIndex = mergedTable.Rows.FindIndex(r => string.Equals(r.RelId, row.RelId, StringComparison.Ordinal));
                    if (existingIndex < 0)
                    {
                        mergedTable.Rows.Add(CloneRelRow(row));
                        continue;
                    }

                    mergedTable.Rows[existingIndex] = MergeRelRow(mergedTable.Rows[existingIndex], row);
                }
            }
        }

        return result;
    }

    private static GraphShardAdjacency MergeAdjacency(IEnumerable<GraphShard> shards)
        => new()
        {
            Outgoing = MergeAdjacencyMap(shards.Select(static shard => shard.Adjacency.Outgoing)),
            Incoming = MergeAdjacencyMap(shards.Select(static shard => shard.Adjacency.Incoming))
        };

    private static Dictionary<string, List<ShardEdgeRef>> MergeAdjacencyMap(IEnumerable<Dictionary<string, List<ShardEdgeRef>>> maps)
    {
        var result = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal);
        foreach (var map in maps)
        {
            foreach (var (nodeId, refs) in map)
            {
                if (!result.TryGetValue(nodeId, out var mergedRefs))
                {
                    mergedRefs = [];
                    result[nodeId] = mergedRefs;
                }

                foreach (var edgeRef in refs)
                {
                    if (mergedRefs.Any(existing =>
                        string.Equals(existing.RelId, edgeRef.RelId, StringComparison.Ordinal) &&
                        string.Equals(existing.Direction, edgeRef.Direction, StringComparison.Ordinal) &&
                        string.Equals(existing.NeighborNodeId, edgeRef.NeighborNodeId, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    mergedRefs.Add(new ShardEdgeRef
                    {
                        RelId = edgeRef.RelId,
                        RelType = edgeRef.RelType,
                        NeighborNodeId = edgeRef.NeighborNodeId,
                        Direction = edgeRef.Direction
                    });
                }
            }
        }

        return result;
    }

    private static GraphShardSeedProvenance MergeSeedProvenance(IEnumerable<GraphShard> shards)
    {
        var mergedSeeds = new List<GraphShardSeedRecord>();
        foreach (var shard in shards)
        {
            foreach (var seed in shard.SeedProvenance.RequestedSeeds)
            {
                var existingIndex = mergedSeeds.FindIndex(existing =>
                    string.Equals(existing.TableName, seed.TableName, StringComparison.Ordinal) &&
                    string.Equals(existing.RequestedNodeId, seed.RequestedNodeId, StringComparison.Ordinal));
                if (existingIndex < 0)
                {
                    mergedSeeds.Add(new GraphShardSeedRecord
                    {
                        TableName = seed.TableName,
                        RequestedNodeId = seed.RequestedNodeId,
                        ExternalId = seed.ExternalId,
                        Status = seed.Status,
                        Reason = seed.Reason
                    });
                }
                else
                {
                    mergedSeeds[existingIndex] = MergeSeedRecord(mergedSeeds[existingIndex], seed);
                }
            }
        }

        return new GraphShardSeedProvenance
        {
            RequestedCount = mergedSeeds.Count,
            IncludedCount = mergedSeeds.Count(static seed => string.Equals(seed.Status, "included", StringComparison.Ordinal)),
            ExcludedCount = mergedSeeds.Count(static seed => string.Equals(seed.Status, "excluded", StringComparison.Ordinal)),
            RequestedSeeds = mergedSeeds
        };
    }

    private static GraphShardBoundary MergeBoundary(IEnumerable<GraphShard> shards)
    {
        var boundaryNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var truncationReasons = new HashSet<string>(StringComparer.Ordinal);
        var fetchHintReasons = new HashSet<string>(StringComparer.Ordinal);
        var recommendedSeeds = new HashSet<string>(StringComparer.Ordinal);

        var isTruncated = false;
        var hasNodeBoundary = false;
        var hasEdgeBoundary = false;
        var truncatedByNodeLimit = false;
        var truncatedByEdgeLimit = false;
        var truncatedByDepth = false;
        var shouldFetchMore = false;
        var canResumeFromBoundary = false;
        int? suggestedDepth = null;
        int? suggestedNodes = null;
        int? suggestedEdges = null;

        foreach (var shard in shards)
        {
            var boundary = shard.Boundary;
            isTruncated |= boundary.IsTruncated;
            hasNodeBoundary |= boundary.HasNodeBoundary;
            hasEdgeBoundary |= boundary.HasEdgeBoundary;
            truncatedByNodeLimit |= boundary.TruncatedByNodeLimit;
            truncatedByEdgeLimit |= boundary.TruncatedByEdgeLimit;
            truncatedByDepth |= boundary.TruncatedByDepth;
            shouldFetchMore |= boundary.FetchHints.ShouldFetchMore;
            canResumeFromBoundary |= boundary.FetchHints.CanResumeFromBoundary;

            foreach (var id in boundary.BoundaryNodeIds)
                boundaryNodeIds.Add(id);
            foreach (var reason in boundary.TruncationReasons)
                truncationReasons.Add(reason);
            foreach (var reason in boundary.FetchHints.Reasons)
                fetchHintReasons.Add(reason);
            foreach (var id in boundary.FetchHints.RecommendedSeedNodeIds)
                recommendedSeeds.Add(id);

            suggestedDepth = MaxNullable(suggestedDepth, boundary.FetchHints.SuggestedMaxDepth);
            suggestedNodes = MaxNullable(suggestedNodes, boundary.FetchHints.SuggestedMaxNodes);
            suggestedEdges = MaxNullable(suggestedEdges, boundary.FetchHints.SuggestedMaxEdges);
        }

        return new GraphShardBoundary
        {
            IsTruncated = isTruncated,
            HasNodeBoundary = hasNodeBoundary,
            HasEdgeBoundary = hasEdgeBoundary,
            TruncatedByNodeLimit = truncatedByNodeLimit,
            TruncatedByEdgeLimit = truncatedByEdgeLimit,
            TruncatedByDepth = truncatedByDepth,
            TruncationReasons = truncationReasons.ToList(),
            BoundaryNodeIds = boundaryNodeIds.ToList(),
            FetchHints = new GraphShardBoundaryFetchHints
            {
                ShouldFetchMore = shouldFetchMore,
                CanResumeFromBoundary = canResumeFromBoundary,
                RecommendedSeedNodeIds = recommendedSeeds.ToList(),
                SuggestedMaxDepth = suggestedDepth,
                SuggestedMaxNodes = suggestedNodes,
                SuggestedMaxEdges = suggestedEdges,
                Reasons = fetchHintReasons.ToList()
            }
        };
    }

    private static GraphShardExtractionOptions MergeOptions(IEnumerable<GraphShard> shards)
    {
        GraphShardExtractionOptions? merged = null;
        foreach (var options in shards.Select(static shard => shard.Options))
        {
            merged = merged is null
                ? options
                : new GraphShardExtractionOptions
                {
                    MaxDepth = MaxNullable(merged.MaxDepth, options.MaxDepth),
                    MaxNodes = MaxNullable(merged.MaxNodes, options.MaxNodes),
                    MaxEdges = MaxNullable(merged.MaxEdges, options.MaxEdges),
                    IncludeOutgoing = merged.IncludeOutgoing || options.IncludeOutgoing,
                    IncludeIncoming = merged.IncludeIncoming || options.IncludeIncoming,
                    IncludeNodeProperties = merged.IncludeNodeProperties || options.IncludeNodeProperties,
                    IncludeRelProperties = merged.IncludeRelProperties || options.IncludeRelProperties,
                    IncludeAdjacency = merged.IncludeAdjacency || options.IncludeAdjacency,
                    IncludeBoundaryMetadata = merged.IncludeBoundaryMetadata || options.IncludeBoundaryMetadata,
                    StopAtBoundary = merged.StopAtBoundary || options.StopAtBoundary,
                    RelTypes = merged.RelTypes.Concat(options.RelTypes).Distinct(StringComparer.Ordinal).ToList(),
                    NodeTables = merged.NodeTables.Concat(options.NodeTables).Distinct(StringComparer.Ordinal).ToList()
                };
        }

        return merged ?? new GraphShardExtractionOptions();
    }

    private static Dictionary<string, object?> MergeMetadata(IEnumerable<GraphShard> shards)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        var count = 0;
        foreach (var shard in shards)
        {
            count++;
            foreach (var (key, value) in shard.Metadata)
                metadata[key] = value;
        }

        metadata["mergedShardCount"] = count;
        return metadata;
    }

    private static NodeShardRow CloneNodeRow(NodeShardRow row)
        => new()
        {
            ExternalId = row.ExternalId,
            Properties = row.Properties.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
        };

    private static RelShardRow CloneRelRow(RelShardRow row)
        => new()
        {
            RelId = row.RelId,
            SourceNodeId = row.SourceNodeId,
            TargetNodeId = row.TargetNodeId,
            Properties = row.Properties.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
        };

    private static NodeShardRow MergeNodeRow(NodeShardRow existing, NodeShardRow incoming)
        => new()
        {
            ExternalId = existing.ExternalId,
            Properties = existing.Properties
                .Concat(incoming.Properties)
                .GroupBy(static kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal)
        };

    private static RelShardRow MergeRelRow(RelShardRow existing, RelShardRow incoming)
        => new()
        {
            RelId = existing.RelId,
            SourceNodeId = string.IsNullOrWhiteSpace(incoming.SourceNodeId) ? existing.SourceNodeId : incoming.SourceNodeId,
            TargetNodeId = string.IsNullOrWhiteSpace(incoming.TargetNodeId) ? existing.TargetNodeId : incoming.TargetNodeId,
            Properties = existing.Properties
                .Concat(incoming.Properties)
                .GroupBy(static kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal)
        };

    private static GraphShardSeedRecord MergeSeedRecord(GraphShardSeedRecord existing, GraphShardSeedRecord incoming)
        => new()
        {
            TableName = existing.TableName,
            RequestedNodeId = existing.RequestedNodeId,
            ExternalId = string.IsNullOrWhiteSpace(incoming.ExternalId) ? existing.ExternalId : incoming.ExternalId,
            Status = string.Equals(incoming.Status, "included", StringComparison.Ordinal) ? "included" : existing.Status,
            Reason = string.Equals(incoming.Status, "included", StringComparison.Ordinal) ? string.Empty : incoming.Reason
        };

    private static string? MergeGraphVersionToken(IReadOnlyList<GraphShard> shards)
    {
        var tokens = shards
            .Select(static shard => shard.GraphVersionToken)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return tokens.Count switch
        {
            0 => null,
            1 => tokens[0],
            _ => $"merged:{string.Join("|", tokens.OrderBy(static token => token, StringComparer.Ordinal))}"
        };
    }

    private static int? MaxNullable(int? left, int? right)
    {
        if (!left.HasValue)
            return right;
        if (!right.HasValue)
            return left;
        return Math.Max(left.Value, right.Value);
    }
}
