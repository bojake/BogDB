namespace BogDb.Core.Extraction;

/// <summary>
/// Local runtime facade over GraphShard payloads for lookup, merge, and adjacency-first traversal.
/// </summary>
public sealed class GraphShardLocalRuntime
{
    private sealed record CursorState
    {
        public List<string> Values { get; init; } = [];
        public string Id { get; init; } = string.Empty;
    }

    private GraphShard _shard;
    private GraphShardRuntimeView _view;

    private GraphShardLocalRuntime(GraphShard shard, GraphShardRuntimeView view)
    {
        _shard = shard;
        _view = view;
    }

    public GraphShard Shard => _shard;
    public GraphShardRuntimeView View => _view;

    public static GraphShardLocalRuntime LoadExtract(GraphShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        var view = GraphShardRuntimeView.Load(shard);
        return new GraphShardLocalRuntime(view.Shard, view);
    }

    public void MergeExtract(GraphShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        Reload(GraphShardMerger.Merge(_shard, shard));
    }

    public void MergeExtracts(IEnumerable<GraphShard> shards)
    {
        ArgumentNullException.ThrowIfNull(shards);
        Reload(GraphShardMerger.Merge([_shard, .. shards]));
    }

    public bool HasNode(string externalId)
        => _view.HasNode(externalId);

    public NodeShardRow GetNodeRow(string externalId)
    {
        if (_view.TryGetNode(externalId, out _, out var row))
            return row;

        throw new KeyNotFoundException($"Node '{externalId}' was not found in the local runtime.");
    }

    public bool TryGetNodeRow(string externalId, out NodeShardRow row)
    {
        if (_view.TryGetNode(externalId, out _, out row))
            return true;

        row = null!;
        return false;
    }

    public IReadOnlyDictionary<string, object?> GetNodeProps(string externalId)
        => _view.GetNodeProperties(externalId);

    public IReadOnlyList<ShardEdgeRef> GetOutgoing(string externalId, string? relType = null)
        => _view.GetOutgoing(externalId, relType);

    public IReadOnlyList<ShardEdgeRef> GetIncoming(string externalId, string? relType = null)
        => _view.GetIncoming(externalId, relType);

    public IReadOnlyList<string> Expand(
        string externalId,
        bool includeOutgoing = true,
        bool includeIncoming = false,
        string? relType = null)
        => _view.Expand(externalId, includeOutgoing, includeIncoming, relType);

    public IReadOnlyList<GraphShardNodeMatch> FilterNodes(GraphShardNodePredicateSpec predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (string.IsNullOrWhiteSpace(predicate.PropertyName))
            throw new ArgumentException("PropertyName is required.", nameof(predicate));

        var matches = new List<GraphShardNodeMatch>();
        foreach (var (tableName, table) in _shard.NodeTables.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(predicate.TableName) &&
                !string.Equals(predicate.TableName, tableName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var row in table.Rows)
            {
                if (MatchesPredicate(tableName, row, predicate))
                {
                    matches.Add(new GraphShardNodeMatch
                    {
                        TableName = tableName,
                        Row = row
                    });
                }
            }
        }

        return matches;
    }

    public IReadOnlyList<GraphShardNodeMatch> FilterNodes(GraphShardNodeFilterSpec filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var matches = new List<GraphShardNodeMatch>();
        foreach (var (tableName, table) in _shard.NodeTables.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            foreach (var row in table.Rows)
            {
                if (MatchesFilter(tableName, row, filter))
                {
                    matches.Add(new GraphShardNodeMatch
                    {
                        TableName = tableName,
                        Row = row
                    });
                }
            }
        }

        return matches;
    }

    public IReadOnlyList<GraphShardRelationshipMatch> FilterRelationships(GraphShardRelationshipPredicateSpec predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (string.IsNullOrWhiteSpace(predicate.PropertyName))
            throw new ArgumentException("PropertyName is required.", nameof(predicate));

        var matches = new List<GraphShardRelationshipMatch>();
        foreach (var (relType, table) in _shard.RelTables.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(predicate.RelType) &&
                !string.Equals(predicate.RelType, relType, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var row in table.Rows)
            {
                if (MatchesRelationshipPredicate(relType, row, predicate))
                {
                    matches.Add(new GraphShardRelationshipMatch
                    {
                        RelType = relType,
                        Row = row
                    });
                }
            }
        }

        return matches;
    }

    public IReadOnlyList<GraphShardRelationshipMatch> FilterRelationships(GraphShardRelationshipFilterSpec filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var matches = new List<GraphShardRelationshipMatch>();
        foreach (var (relType, table) in _shard.RelTables.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            foreach (var row in table.Rows)
            {
                if (MatchesRelationshipFilter(relType, row, filter))
                {
                    matches.Add(new GraphShardRelationshipMatch
                    {
                        RelType = relType,
                        Row = row
                    });
                }
            }
        }

        return matches;
    }

    public GraphShard ProjectFilteredRelationships(
        GraphShardRelationshipFilterSpec filter,
        bool includeEndpointNodes = true)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var selectedMatches = FilterRelationships(filter);
        var relIds = selectedMatches.Select(match => match.Row.RelId).ToHashSet(StringComparer.Ordinal);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        if (includeEndpointNodes)
        {
            foreach (var match in selectedMatches)
            {
                nodeIds.Add(match.Row.SourceNodeId);
                nodeIds.Add(match.Row.TargetNodeId);
            }
        }

        var shard = BuildProjectedShard(
            nodeIds,
            relIds,
            seedExternalId: nodeIds.FirstOrDefault() ?? string.Empty,
            depth: 0,
            includeOutgoing: true,
            includeIncoming: true,
            relType: null,
            boundaryNodeIds: new HashSet<string>(StringComparer.Ordinal));

        return GraphShardNormalizer.Normalize(new GraphShard
        {
            FormatVersion = shard.FormatVersion,
            GraphVersionToken = shard.GraphVersionToken,
            ExtractorVersion = shard.ExtractorVersion,
            ExtractedAtUtc = shard.ExtractedAtUtc,
            ExtractionPolicy = "runtime_relationship_projection",
            IsComplete = _shard.IsComplete,
            NodeTables = shard.NodeTables,
            RelTables = shard.RelTables,
            Adjacency = shard.Adjacency,
            SeedProvenance = new GraphShardSeedProvenance
            {
                RequestedCount = selectedMatches.Count,
                IncludedCount = selectedMatches.Count,
                ExcludedCount = 0,
                RequestedSeeds = selectedMatches
                    .Select(match => new GraphShardSeedRecord
                    {
                        TableName = match.RelType,
                        RequestedNodeId = match.Row.RelId,
                        ExternalId = match.Row.RelId,
                        Status = "included",
                        Reason = string.Empty
                    })
                    .ToList()
            },
            Boundary = shard.Boundary,
            Options = new GraphShardExtractionOptions
            {
                IncludeOutgoing = true,
                IncludeIncoming = true,
                IncludeNodeProperties = true,
                IncludeRelProperties = true,
                IncludeAdjacency = true,
                IncludeBoundaryMetadata = true,
                StopAtBoundary = false
            },
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runtimeRelationshipProjection"] = true,
                ["selectedRelationshipCount"] = selectedMatches.Count,
                ["includeEndpointNodes"] = includeEndpointNodes
            }
        });
    }

    public GraphShardAggregateResult AggregateNodes(
        GraphShardNodeFilterSpec filter,
        string? groupByProperty,
        params GraphShardAggregateSpec[] aggregates)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(aggregates);
        return AggregateNodeMatches(FilterNodes(filter), groupByProperty, aggregates);
    }

    public GraphShardAggregateResult AggregateRelationships(
        GraphShardRelationshipFilterSpec filter,
        string? groupByProperty,
        params GraphShardAggregateSpec[] aggregates)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(aggregates);
        return AggregateRelationshipMatches(FilterRelationships(filter), groupByProperty, aggregates);
    }

    public IReadOnlyList<GraphShardNodeMatch> SortAndPageNodes(
        IEnumerable<GraphShardNodeMatch> matches,
        GraphShardNodeSortSpec? sort = null,
        GraphShardPageSpec? page = null)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var ordered = OrderNodeMatches(matches, GetNodeSorts(sort));
        return ApplyPage(ordered, page).ToList();
    }

    public IReadOnlyList<GraphShardRelationshipMatch> SortAndPageRelationships(
        IEnumerable<GraphShardRelationshipMatch> matches,
        GraphShardRelationshipSortSpec? sort = null,
        GraphShardPageSpec? page = null)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var ordered = OrderRelationshipMatches(matches, GetRelationshipSorts(sort));
        return ApplyPage(ordered, page).ToList();
    }

    public GraphShardCursorPageResult<GraphShardNodeMatch> CursorPageNodes(
        IEnumerable<GraphShardNodeMatch> matches,
        IReadOnlyList<GraphShardNodeSortSpec>? sorts = null,
        GraphShardCursorPageSpec? page = null)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var normalizedSorts = GetNodeSorts(sorts);
        var ordered = OrderNodeMatches(matches, normalizedSorts).ToList();
        var paged = ApplyCursorPage(
            ordered,
            page,
            normalizedSorts,
            match => GetNodeCursorValues(match, normalizedSorts),
            match => match.Row.ExternalId);
        return paged;
    }

    public GraphShardCursorPageResult<GraphShardRelationshipMatch> CursorPageRelationships(
        IEnumerable<GraphShardRelationshipMatch> matches,
        IReadOnlyList<GraphShardRelationshipSortSpec>? sorts = null,
        GraphShardCursorPageSpec? page = null)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var normalizedSorts = GetRelationshipSorts(sorts);
        var ordered = OrderRelationshipMatches(matches, normalizedSorts).ToList();
        var paged = ApplyCursorPage(
            ordered,
            page,
            normalizedSorts,
            match => GetRelationshipCursorValues(match, normalizedSorts),
            match => match.Row.RelId);
        return paged;
    }

    public GraphShardNeighborSummaryResult SummarizeNeighbors(
        string sourceNodeId,
        GraphShardNodeFilterSpec filter,
        bool includeOutgoing = true,
        bool includeIncoming = false,
        string? relType = null,
        params GraphShardAggregateSpec[] aggregates)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (!_view.HasNode(sourceNodeId))
            throw new KeyNotFoundException($"Node '{sourceNodeId}' was not found in the local runtime.");

        var allowed = FilterNodes(filter)
            .Select(match => match.Row.ExternalId)
            .ToHashSet(StringComparer.Ordinal);

        var discovered = EnumerateEdges(sourceNodeId, includeOutgoing, includeIncoming, relType)
            .Where(edge => allowed.Contains(edge.NeighborNodeId))
            .Select(edge =>
            {
                var neighborMatch = GetRequiredNodeMatch(edge.NeighborNodeId);
                var relationshipMatch = GetRequiredRelationshipMatch(edge.RelId);
                return new NeighborAggregateItem
                {
                    Edge = edge,
                    Neighbor = neighborMatch,
                    Relationship = relationshipMatch
                };
            })
            .ToList();
        var totalCount = discovered.Count;

        var rows = discovered
            .GroupBy(x => (x.Edge.RelType, x.Neighbor.TableName))
            .OrderBy(group => group.Key.RelType, StringComparer.Ordinal)
            .ThenBy(group => group.Key.TableName, StringComparer.Ordinal)
            .Select(group =>
            {
                var count = group.Count();
                return new GraphShardNeighborSummaryRow
                {
                    RelType = group.Key.RelType,
                    TargetTable = group.Key.TableName,
                    Label = FormatNeighborSummaryLabel(group.Key.RelType, group.Key.TableName),
                    Count = count,
                    TotalCount = totalCount,
                    Share = ComputeShare(count, totalCount),
                    AggregateValues = ComputeNeighborSummaryAggregates(group, aggregates)
                };
            })
            .ToList();

        return new GraphShardNeighborSummaryResult
        {
            SourceNodeId = sourceNodeId,
            TotalCount = totalCount,
            Rows = rows
        };
    }

    public GraphShardRelationshipSummaryResult SummarizeRelationships(
        GraphShardRelationshipFilterSpec filter,
        params GraphShardAggregateSpec[] aggregates)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var matches = FilterRelationships(filter);
        var totalCount = matches.Count;
        var rows = matches
            .Select(match => new
            {
                Match = match,
                SourceTable = GetRequiredNodeTable(match.Row.SourceNodeId),
                TargetTable = GetRequiredNodeTable(match.Row.TargetNodeId)
            })
            .GroupBy(x => (x.Match.RelType, x.SourceTable, x.TargetTable))
            .OrderBy(group => group.Key.RelType, StringComparer.Ordinal)
            .ThenBy(group => group.Key.SourceTable, StringComparer.Ordinal)
            .ThenBy(group => group.Key.TargetTable, StringComparer.Ordinal)
            .Select(group =>
            {
                var count = group.Count();
                return new GraphShardRelationshipSummaryRow
                {
                    RelType = group.Key.RelType,
                    SourceTable = group.Key.SourceTable,
                    TargetTable = group.Key.TargetTable,
                    Label = FormatRelationshipSummaryLabel(group.Key.RelType, group.Key.SourceTable, group.Key.TargetTable),
                    Count = count,
                    TotalCount = totalCount,
                    Share = ComputeShare(count, totalCount),
                    AggregateValues = ComputeRelationshipSummaryAggregates(group.Select(x => x.Match), aggregates)
                };
            })
            .ToList();

        return new GraphShardRelationshipSummaryResult
        {
            TotalCount = totalCount,
            Rows = rows
        };
    }

    public GraphShardNeighborSummaryResult TopNeighborSummaries(
        GraphShardNeighborSummaryResult summary,
        GraphShardTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var ranked = RankNeighborSummaries(summary, topN);
        return new GraphShardNeighborSummaryResult
        {
            SourceNodeId = ranked.SourceNodeId,
            TotalCount = ranked.TotalCount,
            Rows = ranked.Rows
        };
    }

    public GraphShardRelationshipSummaryResult TopRelationshipSummaries(
        GraphShardRelationshipSummaryResult summary,
        GraphShardTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var ranked = RankRelationshipSummaries(summary, topN);
        return new GraphShardRelationshipSummaryResult
        {
            TotalCount = ranked.TotalCount,
            Rows = ranked.Rows
        };
    }

    public GraphShardNeighborSummaryResult RankNeighborSummaries(
        GraphShardNeighborSummaryResult summary,
        GraphShardTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var rows = ApplyTopN(
            summary.Rows,
            topN,
            row => row.Count,
            row => row.AggregateValues,
            row => [row.RelType, row.TargetTable]);
        var cumulativeCount = 0;
        var rankedRows = rows
            .Select((row, index) =>
            {
                cumulativeCount += row.Count;
                return row with
                {
                    Rank = index + 1,
                    CumulativeCount = cumulativeCount,
                    CumulativeShare = ComputeShare(cumulativeCount, summary.TotalCount)
                };
            })
            .ToList();
        return new GraphShardNeighborSummaryResult
        {
            SourceNodeId = summary.SourceNodeId,
            TotalCount = summary.TotalCount,
            Rows = rankedRows
        };
    }

    public GraphShardRelationshipSummaryResult RankRelationshipSummaries(
        GraphShardRelationshipSummaryResult summary,
        GraphShardTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var rows = ApplyTopN(
            summary.Rows,
            topN,
            row => row.Count,
            row => row.AggregateValues,
            row => [row.RelType, row.SourceTable, row.TargetTable]);
        var cumulativeCount = 0;
        var rankedRows = rows
            .Select((row, index) =>
            {
                cumulativeCount += row.Count;
                return row with
                {
                    Rank = index + 1,
                    CumulativeCount = cumulativeCount,
                    CumulativeShare = ComputeShare(cumulativeCount, summary.TotalCount)
                };
            })
            .ToList();
        return new GraphShardRelationshipSummaryResult
        {
            TotalCount = summary.TotalCount,
            Rows = rankedRows
        };
    }

    public GraphShardSummaryDeltaResult CompareNeighborSummaries(
        GraphShardNeighborSummaryResult current,
        GraphShardNeighborSummaryResult previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var currentRows = current.Rows.ToDictionary(
            row => $"{row.RelType}|{row.TargetTable}",
            row => row,
            StringComparer.Ordinal);
        var previousRows = previous.Rows.ToDictionary(
            row => $"{row.RelType}|{row.TargetTable}",
            row => row,
            StringComparer.Ordinal);

        var keys = currentRows.Keys
            .Concat(previousRows.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);

        return new GraphShardSummaryDeltaResult
        {
            Rows = keys.Select(key =>
            {
                currentRows.TryGetValue(key, out var currentRow);
                previousRows.TryGetValue(key, out var previousRow);
                return new GraphShardSummaryDeltaRow
                {
                    Key = key,
                    Label = currentRow?.Label ?? previousRow?.Label ?? key,
                    CurrentCount = currentRow?.Count ?? 0,
                    PreviousCount = previousRow?.Count ?? 0,
                    CountDelta = (currentRow?.Count ?? 0) - (previousRow?.Count ?? 0),
                    CurrentShare = currentRow?.Share ?? 0m,
                    PreviousShare = previousRow?.Share ?? 0m,
                    ShareDelta = (currentRow?.Share ?? 0m) - (previousRow?.Share ?? 0m)
                };
            }).ToList()
        };
    }

    public GraphShardSummaryDeltaResult CompareRelationshipSummaries(
        GraphShardRelationshipSummaryResult current,
        GraphShardRelationshipSummaryResult previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var currentRows = current.Rows.ToDictionary(
            row => $"{row.RelType}|{row.SourceTable}|{row.TargetTable}",
            row => row,
            StringComparer.Ordinal);
        var previousRows = previous.Rows.ToDictionary(
            row => $"{row.RelType}|{row.SourceTable}|{row.TargetTable}",
            row => row,
            StringComparer.Ordinal);

        var keys = currentRows.Keys
            .Concat(previousRows.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);

        return new GraphShardSummaryDeltaResult
        {
            Rows = keys.Select(key =>
            {
                currentRows.TryGetValue(key, out var currentRow);
                previousRows.TryGetValue(key, out var previousRow);
                return new GraphShardSummaryDeltaRow
                {
                    Key = key,
                    Label = currentRow?.Label ?? previousRow?.Label ?? key,
                    CurrentCount = currentRow?.Count ?? 0,
                    PreviousCount = previousRow?.Count ?? 0,
                    CountDelta = (currentRow?.Count ?? 0) - (previousRow?.Count ?? 0),
                    CurrentShare = currentRow?.Share ?? 0m,
                    PreviousShare = previousRow?.Share ?? 0m,
                    ShareDelta = (currentRow?.Share ?? 0m) - (previousRow?.Share ?? 0m)
                };
            }).ToList()
        };
    }

    public GraphShardBucketDeltaResult CompareHistogramBuckets(
        GraphShardHistogramResult current,
        GraphShardHistogramResult previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var currentBuckets = current.Buckets.ToDictionary(
            bucket => $"{bucket.StartInclusive}|{bucket.EndExclusive}",
            bucket => bucket,
            StringComparer.Ordinal);
        var previousBuckets = previous.Buckets.ToDictionary(
            bucket => $"{bucket.StartInclusive}|{bucket.EndExclusive}",
            bucket => bucket,
            StringComparer.Ordinal);

        var keys = currentBuckets.Keys
            .Concat(previousBuckets.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);

        return new GraphShardBucketDeltaResult
        {
            Rows = keys.Select(key =>
            {
                currentBuckets.TryGetValue(key, out var currentBucket);
                previousBuckets.TryGetValue(key, out var previousBucket);
                return new GraphShardBucketDeltaRow
                {
                    Key = key,
                    Label = currentBucket?.Label ?? previousBucket?.Label ?? key,
                    CurrentCount = currentBucket?.Count ?? 0,
                    PreviousCount = previousBucket?.Count ?? 0,
                    CountDelta = (currentBucket?.Count ?? 0) - (previousBucket?.Count ?? 0),
                    CurrentShare = currentBucket?.Share ?? 0m,
                    PreviousShare = previousBucket?.Share ?? 0m,
                    ShareDelta = (currentBucket?.Share ?? 0m) - (previousBucket?.Share ?? 0m)
                };
            }).ToList()
        };
    }

    public GraphShardBucketDeltaResult CompareTimeBuckets(
        GraphShardTimeBucketResult current,
        GraphShardTimeBucketResult previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var currentBuckets = current.Buckets.ToDictionary(bucket => bucket.BucketKey, bucket => bucket, StringComparer.Ordinal);
        var previousBuckets = previous.Buckets.ToDictionary(bucket => bucket.BucketKey, bucket => bucket, StringComparer.Ordinal);

        var keys = currentBuckets.Keys
            .Concat(previousBuckets.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal);

        return new GraphShardBucketDeltaResult
        {
            Rows = keys.Select(key =>
            {
                currentBuckets.TryGetValue(key, out var currentBucket);
                previousBuckets.TryGetValue(key, out var previousBucket);
                return new GraphShardBucketDeltaRow
                {
                    Key = key,
                    Label = currentBucket?.Label ?? previousBucket?.Label ?? key,
                    CurrentCount = currentBucket?.Count ?? 0,
                    PreviousCount = previousBucket?.Count ?? 0,
                    CountDelta = (currentBucket?.Count ?? 0) - (previousBucket?.Count ?? 0),
                    CurrentShare = currentBucket?.Share ?? 0m,
                    PreviousShare = previousBucket?.Share ?? 0m,
                    ShareDelta = (currentBucket?.Share ?? 0m) - (previousBucket?.Share ?? 0m)
                };
            }).ToList()
        };
    }

    public GraphShardSummaryDeltaResult RankSummaryDeltas(
        GraphShardSummaryDeltaResult deltas,
        GraphShardDeltaTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        var spec = topN ?? new GraphShardDeltaTopNSpec();
        var rows = deltas.Rows
            .OrderBy(row => row, Comparer<GraphShardSummaryDeltaRow>.Create((left, right) =>
                CompareDeltaRows(left.CountDelta, left.ShareDelta, left.Key, right.CountDelta, right.ShareDelta, right.Key, spec)))
            .Take(Math.Max(spec.Limit, 0))
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
        return new GraphShardSummaryDeltaResult { Rows = rows };
    }

    public GraphShardBucketDeltaResult RankBucketDeltas(
        GraphShardBucketDeltaResult deltas,
        GraphShardDeltaTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        var spec = topN ?? new GraphShardDeltaTopNSpec();
        var rows = deltas.Rows
            .OrderBy(row => row, Comparer<GraphShardBucketDeltaRow>.Create((left, right) =>
                CompareDeltaRows(left.CountDelta, left.ShareDelta, left.Key, right.CountDelta, right.ShareDelta, right.Key, spec)))
            .Take(Math.Max(spec.Limit, 0))
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
        return new GraphShardBucketDeltaResult { Rows = rows };
    }

    public GraphShardSummaryDeltaResult TopGainingSummaryDeltas(
        GraphShardSummaryDeltaResult deltas,
        GraphShardDeltaTopNSpec? topN = null)
        => RankSummaryDeltas(FilterSummaryDeltas(deltas, positive: true), NormalizeDirectionalDeltaSpec(topN, GraphShardSortDirection.Desc));

    public GraphShardSummaryDeltaResult TopDecliningSummaryDeltas(
        GraphShardSummaryDeltaResult deltas,
        GraphShardDeltaTopNSpec? topN = null)
        => RankSummaryDeltas(FilterSummaryDeltas(deltas, positive: false), NormalizeDirectionalDeltaSpec(topN, GraphShardSortDirection.Asc));

    public GraphShardBucketDeltaResult TopGainingBucketDeltas(
        GraphShardBucketDeltaResult deltas,
        GraphShardDeltaTopNSpec? topN = null)
        => RankBucketDeltas(FilterBucketDeltas(deltas, positive: true), NormalizeDirectionalDeltaSpec(topN, GraphShardSortDirection.Desc));

    public GraphShardBucketDeltaResult TopDecliningBucketDeltas(
        GraphShardBucketDeltaResult deltas,
        GraphShardDeltaTopNSpec? topN = null)
        => RankBucketDeltas(FilterBucketDeltas(deltas, positive: false), NormalizeDirectionalDeltaSpec(topN, GraphShardSortDirection.Asc));

    public GraphShardProjectionDeltaResult CompareProjectedShards(GraphShard previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var currentNodeIds = _shard.NodeTables.Values
            .SelectMany(table => table.Rows)
            .Select(row => row.ExternalId)
            .ToHashSet(StringComparer.Ordinal);
        var previousNodeIds = previous.NodeTables.Values
            .SelectMany(table => table.Rows)
            .Select(row => row.ExternalId)
            .ToHashSet(StringComparer.Ordinal);
        var currentRelIds = _shard.RelTables.Values
            .SelectMany(table => table.Rows)
            .Select(row => row.RelId)
            .ToHashSet(StringComparer.Ordinal);
        var previousRelIds = previous.RelTables.Values
            .SelectMany(table => table.Rows)
            .Select(row => row.RelId)
            .ToHashSet(StringComparer.Ordinal);

        return new GraphShardProjectionDeltaResult
        {
            AddedNodeIds = currentNodeIds.Except(previousNodeIds, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            RemovedNodeIds = previousNodeIds.Except(currentNodeIds, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            AddedRelIds = currentRelIds.Except(previousRelIds, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            RemovedRelIds = previousRelIds.Except(currentRelIds, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList()
        };
    }

    public GraphShardProjectionNodeDeltaResult SummarizeProjectedNodeChanges(GraphShard previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        var currentLookup = _shard.NodeTables.Values
            .SelectMany(table => table.Rows.Select(row => new KeyValuePair<string, string>(row.ExternalId, table.TableName)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var previousLookup = previous.NodeTables.Values
            .SelectMany(table => table.Rows.Select(row => new KeyValuePair<string, string>(row.ExternalId, table.TableName)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var delta = CompareProjectedShards(previous);

        var addedCounts = CountProjectionMembership(delta.AddedNodeIds, currentLookup);
        var removedCounts = CountProjectionMembership(delta.RemovedNodeIds, previousLookup);

        var rows = addedCounts.Keys
            .Concat(removedCounts.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(tableName =>
            {
                var added = addedCounts.GetValueOrDefault(tableName);
                var removed = removedCounts.GetValueOrDefault(tableName);
                return new GraphShardProjectionNodeDeltaRow
                {
                    TableName = tableName,
                    Label = tableName,
                    AddedCount = added,
                    RemovedCount = removed,
                    NetDelta = added - removed
                };
            })
            .ToList();

        return new GraphShardProjectionNodeDeltaResult { Rows = rows };
    }

    public GraphShardProjectionRelationshipDeltaResult SummarizeProjectedRelationshipChanges(GraphShard previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        var currentLookup = _shard.RelTables.Values
            .SelectMany(table => table.Rows.Select(row => new KeyValuePair<string, string>(row.RelId, table.RelType)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var previousLookup = previous.RelTables.Values
            .SelectMany(table => table.Rows.Select(row => new KeyValuePair<string, string>(row.RelId, table.RelType)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var delta = CompareProjectedShards(previous);

        var addedCounts = CountProjectionMembership(delta.AddedRelIds, currentLookup);
        var removedCounts = CountProjectionMembership(delta.RemovedRelIds, previousLookup);

        var rows = addedCounts.Keys
            .Concat(removedCounts.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(relType =>
            {
                var added = addedCounts.GetValueOrDefault(relType);
                var removed = removedCounts.GetValueOrDefault(relType);
                return new GraphShardProjectionRelationshipDeltaRow
                {
                    RelType = relType,
                    Label = relType,
                    AddedCount = added,
                    RemovedCount = removed,
                    NetDelta = added - removed
                };
            })
            .ToList();

        return new GraphShardProjectionRelationshipDeltaResult { Rows = rows };
    }

    public GraphShardProjectionNodeDeltaResult TopChangedNodeTables(
        GraphShard previous,
        GraphShardTopNSpec? topN = null)
    {
        var summary = SummarizeProjectedNodeChanges(previous);
        var spec = topN ?? new GraphShardTopNSpec();
        var rows = summary.Rows
            .OrderByDescending(row => Math.Abs(row.NetDelta))
            .ThenByDescending(row => row.AddedCount + row.RemovedCount)
            .ThenBy(row => row.TableName, StringComparer.Ordinal)
            .Take(Math.Max(spec.Limit, 0))
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
        return new GraphShardProjectionNodeDeltaResult { Rows = rows };
    }

    public GraphShardProjectionNodeDeltaResult TopGainingNodeTables(
        GraphShard previous,
        GraphShardTopNSpec? topN = null)
    {
        var summary = SummarizeProjectedNodeChanges(previous);
        var spec = topN ?? new GraphShardTopNSpec();
        var rows = summary.Rows
            .Where(row => row.NetDelta > 0)
            .OrderByDescending(row => row.NetDelta)
            .ThenByDescending(row => row.AddedCount + row.RemovedCount)
            .ThenBy(row => row.TableName, StringComparer.Ordinal)
            .Take(Math.Max(spec.Limit, 0))
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
        return new GraphShardProjectionNodeDeltaResult { Rows = rows };
    }

    public GraphShardProjectionNodeDeltaResult TopDecliningNodeTables(
        GraphShard previous,
        GraphShardTopNSpec? topN = null)
    {
        var summary = SummarizeProjectedNodeChanges(previous);
        var spec = topN ?? new GraphShardTopNSpec();
        var rows = summary.Rows
            .Where(row => row.NetDelta < 0)
            .OrderBy(row => row.NetDelta)
            .ThenByDescending(row => row.AddedCount + row.RemovedCount)
            .ThenBy(row => row.TableName, StringComparer.Ordinal)
            .Take(Math.Max(spec.Limit, 0))
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
        return new GraphShardProjectionNodeDeltaResult { Rows = rows };
    }

    public GraphShardProjectionRelationshipDeltaResult TopChangedRelationshipTypes(
        GraphShard previous,
        GraphShardTopNSpec? topN = null)
    {
        var summary = SummarizeProjectedRelationshipChanges(previous);
        var spec = topN ?? new GraphShardTopNSpec();
        var rows = summary.Rows
            .OrderByDescending(row => Math.Abs(row.NetDelta))
            .ThenByDescending(row => row.AddedCount + row.RemovedCount)
            .ThenBy(row => row.RelType, StringComparer.Ordinal)
            .Take(Math.Max(spec.Limit, 0))
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
        return new GraphShardProjectionRelationshipDeltaResult { Rows = rows };
    }

    public GraphShardProjectionRelationshipDeltaResult TopGainingRelationshipTypes(
        GraphShard previous,
        GraphShardTopNSpec? topN = null)
    {
        var summary = SummarizeProjectedRelationshipChanges(previous);
        var spec = topN ?? new GraphShardTopNSpec();
        var rows = summary.Rows
            .Where(row => row.NetDelta > 0)
            .OrderByDescending(row => row.NetDelta)
            .ThenByDescending(row => row.AddedCount + row.RemovedCount)
            .ThenBy(row => row.RelType, StringComparer.Ordinal)
            .Take(Math.Max(spec.Limit, 0))
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
        return new GraphShardProjectionRelationshipDeltaResult { Rows = rows };
    }

    public GraphShardProjectionRelationshipDeltaResult TopDecliningRelationshipTypes(
        GraphShard previous,
        GraphShardTopNSpec? topN = null)
    {
        var summary = SummarizeProjectedRelationshipChanges(previous);
        var spec = topN ?? new GraphShardTopNSpec();
        var rows = summary.Rows
            .Where(row => row.NetDelta < 0)
            .OrderBy(row => row.NetDelta)
            .ThenByDescending(row => row.AddedCount + row.RemovedCount)
            .ThenBy(row => row.RelType, StringComparer.Ordinal)
            .Take(Math.Max(spec.Limit, 0))
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
        return new GraphShardProjectionRelationshipDeltaResult { Rows = rows };
    }

    public GraphShardChangeReviewResult BuildChangeReview(
        GraphShard previous,
        GraphShardTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return new GraphShardChangeReviewResult
        {
            ProjectionDelta = CompareProjectedShards(previous),
            NodeChanges = SummarizeProjectedNodeChanges(previous),
            RelationshipChanges = SummarizeProjectedRelationshipChanges(previous),
            TopChangedNodeTables = TopChangedNodeTables(previous, topN),
            TopChangedRelationshipTypes = TopChangedRelationshipTypes(previous, topN)
        };
    }

    public GraphShardChangeReviewOverview BuildChangeReviewOverview(GraphShard previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        var delta = CompareProjectedShards(previous);
        return new GraphShardChangeReviewOverview
        {
            AddedNodeCount = delta.AddedNodeCount,
            RemovedNodeCount = delta.RemovedNodeCount,
            AddedRelCount = delta.AddedRelCount,
            RemovedRelCount = delta.RemovedRelCount,
            NetNodeDelta = delta.AddedNodeCount - delta.RemovedNodeCount,
            NetRelDelta = delta.AddedRelCount - delta.RemovedRelCount,
            SummaryLabel = FormattableString.Invariant(
                $"nodes +{delta.AddedNodeCount}/-{delta.RemovedNodeCount}, rels +{delta.AddedRelCount}/-{delta.RemovedRelCount}")
        };
    }

    public GraphShardChangeGroupSummary BuildChangeGroupSummary(GraphShard previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        var nodeChanges = SummarizeProjectedNodeChanges(previous).Rows;
        var relationshipChanges = SummarizeProjectedRelationshipChanges(previous).Rows;

        return new GraphShardChangeGroupSummary
        {
            ChangedNodeTableCount = nodeChanges.Count(row => row.AddedCount > 0 || row.RemovedCount > 0),
            GainingNodeTableCount = nodeChanges.Count(row => row.NetDelta > 0),
            DecliningNodeTableCount = nodeChanges.Count(row => row.NetDelta < 0),
            ChangedRelationshipTypeCount = relationshipChanges.Count(row => row.AddedCount > 0 || row.RemovedCount > 0),
            GainingRelationshipTypeCount = relationshipChanges.Count(row => row.NetDelta > 0),
            DecliningRelationshipTypeCount = relationshipChanges.Count(row => row.NetDelta < 0)
        };
    }

    public GraphShardChangeReviewHighlights BuildChangeReviewHighlights(
        GraphShard previous,
        GraphShardTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(previous);

        return new GraphShardChangeReviewHighlights
        {
            Overview = BuildChangeReviewOverview(previous),
            GroupSummary = BuildChangeGroupSummary(previous),
            Review = BuildChangeReview(previous, topN),
            TopGainingNodeTables = TopGainingNodeTables(previous, topN),
            TopDecliningNodeTables = TopDecliningNodeTables(previous, topN),
            TopGainingRelationshipTypes = TopGainingRelationshipTypes(previous, topN),
            TopDecliningRelationshipTypes = TopDecliningRelationshipTypes(previous, topN)
        };
    }

    public GraphShardChangeProjectionDrilldown ProjectNodeTableChangeDrilldown(
        GraphShard previous,
        string tableName)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var delta = CompareProjectedShards(previous);
        var previousRuntime = LoadExtract(previous);

        var addedNodeIds = delta.AddedNodeIds
            .Where(nodeId => string.Equals(GetRequiredNodeTable(nodeId), tableName, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var removedNodeIds = delta.RemovedNodeIds
            .Where(nodeId => string.Equals(previousRuntime.GetRequiredNodeTable(nodeId), tableName, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var addedRelIds = delta.AddedRelIds
            .Where(relId =>
            {
                var rel = GetRequiredRelationshipMatch(relId);
                return addedNodeIds.Contains(rel.Row.SourceNodeId) || addedNodeIds.Contains(rel.Row.TargetNodeId);
            })
            .ToHashSet(StringComparer.Ordinal);
        var removedRelIds = delta.RemovedRelIds
            .Where(relId =>
            {
                var rel = previousRuntime.GetRequiredRelationshipMatch(relId);
                return removedNodeIds.Contains(rel.Row.SourceNodeId) || removedNodeIds.Contains(rel.Row.TargetNodeId);
            })
            .ToHashSet(StringComparer.Ordinal);

        return new GraphShardChangeProjectionDrilldown
        {
            Key = tableName,
            Label = tableName,
            AddedCount = addedNodeIds.Count,
            RemovedCount = removedNodeIds.Count,
            AddedProjection = BuildChangeProjectionSubset(
                this,
                addedNodeIds,
                addedRelIds,
                $"node_table:{tableName}",
                "runtime_change_projection"),
            RemovedProjection = BuildChangeProjectionSubset(
                previousRuntime,
                removedNodeIds,
                removedRelIds,
                $"node_table:{tableName}",
                "runtime_change_projection")
        };
    }

    public GraphShardChangeProjectionDrilldown ProjectRelationshipTypeChangeDrilldown(
        GraphShard previous,
        string relType)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentException.ThrowIfNullOrWhiteSpace(relType);

        var delta = CompareProjectedShards(previous);
        var previousRuntime = LoadExtract(previous);

        var addedRelIds = delta.AddedRelIds
            .Where(relId => string.Equals(GetRequiredRelationshipMatch(relId).RelType, relType, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var removedRelIds = delta.RemovedRelIds
            .Where(relId => string.Equals(previousRuntime.GetRequiredRelationshipMatch(relId).RelType, relType, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        return new GraphShardChangeProjectionDrilldown
        {
            Key = relType,
            Label = relType,
            AddedCount = addedRelIds.Count,
            RemovedCount = removedRelIds.Count,
            AddedProjection = BuildChangeProjectionSubset(
                this,
                GetEndpointNodeIds(this, addedRelIds),
                addedRelIds,
                $"rel_type:{relType}",
                "runtime_change_projection"),
            RemovedProjection = BuildChangeProjectionSubset(
                previousRuntime,
                GetEndpointNodeIds(previousRuntime, removedRelIds),
                removedRelIds,
                $"rel_type:{relType}",
                "runtime_change_projection")
        };
    }

    public GraphShardChangeReviewDrilldown BuildChangeReviewDrilldown(
        GraphShard previous,
        GraphShardTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(previous);

        var review = BuildChangeReview(previous, topN);
        var nodeDrilldowns = review.TopChangedNodeTables.Rows
            .Select(row => ProjectNodeTableChangeDrilldown(previous, row.TableName))
            .ToList();
        var relationshipDrilldowns = review.TopChangedRelationshipTypes.Rows
            .Select(row => ProjectRelationshipTypeChangeDrilldown(previous, row.RelType))
            .ToList();

        return new GraphShardChangeReviewDrilldown
        {
            Overview = BuildChangeReviewOverview(previous),
            GroupSummary = BuildChangeGroupSummary(previous),
            Review = review,
            NodeTableDrilldowns = nodeDrilldowns,
            RelationshipTypeDrilldowns = relationshipDrilldowns
        };
    }

    public GraphShardComposedChangeProjection ComposeNodeTableChangeDrilldowns(
        GraphShard previous,
        IEnumerable<string> tableNames)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(tableNames);

        var keys = tableNames
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToList();
        var drilldowns = keys
            .Select(key => ProjectNodeTableChangeDrilldown(previous, key))
            .ToList();
        return BuildComposedChangeProjection(
            scope: "node_tables",
            keys,
            drilldowns,
            selectedNodeTableCount: keys.Count,
            selectedRelationshipTypeCount: 0);
    }

    public GraphShardComposedChangeProjection ComposeRelationshipTypeChangeDrilldowns(
        GraphShard previous,
        IEnumerable<string> relTypes)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(relTypes);

        var keys = relTypes
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToList();
        var drilldowns = keys
            .Select(key => ProjectRelationshipTypeChangeDrilldown(previous, key))
            .ToList();
        return BuildComposedChangeProjection(
            scope: "relationship_types",
            keys,
            drilldowns,
            selectedNodeTableCount: 0,
            selectedRelationshipTypeCount: keys.Count);
    }

    public GraphShardSelectedChangeSummary BuildSelectedChangeSummary(
        GraphShard previous,
        IEnumerable<string>? nodeTables = null,
        IEnumerable<string>? relTypes = null)
    {
        ArgumentNullException.ThrowIfNull(previous);

        var nodeComposition = ComposeNodeTableChangeDrilldowns(previous, nodeTables ?? []);
        var relationshipComposition = ComposeRelationshipTypeChangeDrilldowns(previous, relTypes ?? []);
        var combined = BuildComposedChangeProjection(
            scope: "selected_groups",
            [
                .. nodeComposition.Keys.Select(static key => $"node:{key}"),
                .. relationshipComposition.Keys.Select(static key => $"rel:{key}")
            ],
            [.. nodeComposition.Drilldowns, .. relationshipComposition.Drilldowns],
            nodeComposition.Summary.SelectedNodeTableCount,
            relationshipComposition.Summary.SelectedRelationshipTypeCount);
        return combined.Summary;
    }

    public GraphShardMultiGroupChangeReview BuildMultiGroupChangeReview(
        GraphShard previous,
        IEnumerable<string>? nodeTables = null,
        IEnumerable<string>? relTypes = null,
        GraphShardTopNSpec? topN = null)
    {
        ArgumentNullException.ThrowIfNull(previous);

        var nodeComposition = ComposeNodeTableChangeDrilldowns(previous, nodeTables ?? []);
        var relationshipComposition = ComposeRelationshipTypeChangeDrilldowns(previous, relTypes ?? []);
        var combined = BuildComposedChangeProjection(
            scope: "selected_groups",
            [
                .. nodeComposition.Keys.Select(static key => $"node:{key}"),
                .. relationshipComposition.Keys.Select(static key => $"rel:{key}")
            ],
            [.. nodeComposition.Drilldowns, .. relationshipComposition.Drilldowns],
            nodeComposition.Summary.SelectedNodeTableCount,
            relationshipComposition.Summary.SelectedRelationshipTypeCount);

        return new GraphShardMultiGroupChangeReview
        {
            Overview = BuildChangeReviewOverview(previous),
            GroupSummary = BuildChangeGroupSummary(previous),
            Review = BuildChangeReview(previous, topN),
            SelectionSummary = combined.Summary,
            NodeTableComposition = nodeComposition,
            RelationshipTypeComposition = relationshipComposition,
            CombinedComposition = combined
        };
    }

    public GraphShardSelectedChangeSummaryDelta CompareSelectedChangeSummaries(
        GraphShardSelectedChangeSummary current,
        GraphShardSelectedChangeSummary previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        return new GraphShardSelectedChangeSummaryDelta
        {
            SelectedNodeTableCountDelta = current.SelectedNodeTableCount - previous.SelectedNodeTableCount,
            SelectedRelationshipTypeCountDelta = current.SelectedRelationshipTypeCount - previous.SelectedRelationshipTypeCount,
            AddedNodeCountDelta = current.AddedNodeCount - previous.AddedNodeCount,
            RemovedNodeCountDelta = current.RemovedNodeCount - previous.RemovedNodeCount,
            AddedRelCountDelta = current.AddedRelCount - previous.AddedRelCount,
            RemovedRelCountDelta = current.RemovedRelCount - previous.RemovedRelCount,
            SummaryLabel = FormattableString.Invariant(
                $"selected nodeTables {current.SelectedNodeTableCount - previous.SelectedNodeTableCount:+#;-#;0}, relTypes {current.SelectedRelationshipTypeCount - previous.SelectedRelationshipTypeCount:+#;-#;0}; nodes +{current.AddedNodeCount - previous.AddedNodeCount:+#;-#;0}/-{current.RemovedNodeCount - previous.RemovedNodeCount:+#;-#;0}, rels +{current.AddedRelCount - previous.AddedRelCount:+#;-#;0}/-{current.RemovedRelCount - previous.RemovedRelCount:+#;-#;0}")
        };
    }

    public GraphShardComposedChangeProjectionComparison CompareComposedChangeProjections(
        GraphShardComposedChangeProjection current,
        GraphShardComposedChangeProjection previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        return new GraphShardComposedChangeProjectionComparison
        {
            Scope = string.Equals(current.Scope, previous.Scope, StringComparison.Ordinal) ? current.Scope : $"{previous.Scope}->{current.Scope}",
            CurrentKeys = [.. current.Keys],
            PreviousKeys = [.. previous.Keys],
            SummaryDelta = CompareSelectedChangeSummaries(current.Summary, previous.Summary),
            AddedProjectionDelta = LoadExtract(current.AddedProjection).CompareProjectedShards(previous.AddedProjection),
            RemovedProjectionDelta = LoadExtract(current.RemovedProjection).CompareProjectedShards(previous.RemovedProjection)
        };
    }

    public GraphShardMultiGroupChangeReviewComparison CompareMultiGroupChangeReviews(
        GraphShardMultiGroupChangeReview current,
        GraphShardMultiGroupChangeReview previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        return new GraphShardMultiGroupChangeReviewComparison
        {
            SelectionSummaryDelta = CompareSelectedChangeSummaries(current.SelectionSummary, previous.SelectionSummary),
            NodeTableCompositionComparison = CompareComposedChangeProjections(current.NodeTableComposition, previous.NodeTableComposition),
            RelationshipTypeCompositionComparison = CompareComposedChangeProjections(current.RelationshipTypeComposition, previous.RelationshipTypeComposition),
            CombinedCompositionComparison = CompareComposedChangeProjections(current.CombinedComposition, previous.CombinedComposition)
        };
    }

    public GraphShardMultiGroupChangeReviewSeriesComparison CompareMultiGroupChangeReviewSeries(
        IReadOnlyList<GraphShardMultiGroupChangeReview> reviews,
        int baselineIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
            throw new ArgumentException("At least one review is required.", nameof(reviews));
        if (baselineIndex < 0 || baselineIndex >= reviews.Count)
            throw new ArgumentOutOfRangeException(nameof(baselineIndex), baselineIndex, "Baseline index is out of range.");

        var baseline = reviews[baselineIndex];
        var rows = reviews
            .Select((review, index) => new { review, index })
            .Where(item => item.index != baselineIndex)
            .Select(item => new GraphShardMultiGroupChangeReviewSeriesRow
            {
                Index = item.index,
                Label = $"review[{item.index}]",
                Comparison = CompareMultiGroupChangeReviews(item.review, baseline)
            })
            .OrderBy(row => row.Index)
            .ToList();

        var summary = new GraphShardMultiGroupChangeReviewSeriesSummary
        {
            BaselineIndex = baselineIndex,
            ComparisonCount = rows.Count,
            MaxAddedNodeCountDelta = rows.Count == 0 ? 0 : rows.Max(row => row.Comparison.SelectionSummaryDelta.AddedNodeCountDelta),
            MaxRemovedNodeCountDelta = rows.Count == 0 ? 0 : rows.Max(row => row.Comparison.SelectionSummaryDelta.RemovedNodeCountDelta),
            MaxAddedRelCountDelta = rows.Count == 0 ? 0 : rows.Max(row => row.Comparison.SelectionSummaryDelta.AddedRelCountDelta),
            MaxRemovedRelCountDelta = rows.Count == 0 ? 0 : rows.Max(row => row.Comparison.SelectionSummaryDelta.RemovedRelCountDelta),
            SummaryLabel = rows.Count == 0
                ? $"baseline review[{baselineIndex}] with no comparison rows"
                : FormattableString.Invariant(
                    $"baseline review[{baselineIndex}] across {rows.Count} comparisons; max nodes +{rows.Max(row => row.Comparison.SelectionSummaryDelta.AddedNodeCountDelta)}/-{rows.Max(row => row.Comparison.SelectionSummaryDelta.RemovedNodeCountDelta)}, rels +{rows.Max(row => row.Comparison.SelectionSummaryDelta.AddedRelCountDelta)}/-{rows.Max(row => row.Comparison.SelectionSummaryDelta.RemovedRelCountDelta)}")
        };

        return new GraphShardMultiGroupChangeReviewSeriesComparison
        {
            BaselineIndex = baselineIndex,
            Rows = rows,
            Summary = summary
        };
    }

    public GraphShardNamedMultiGroupChangeReviewSeriesComparison CompareNamedMultiGroupChangeReviewSeries(
        IReadOnlyDictionary<string, GraphShardMultiGroupChangeReview> reviews,
        string baselineKey)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineKey);
        if (reviews.Count == 0)
            throw new ArgumentException("At least one review is required.", nameof(reviews));
        if (!reviews.TryGetValue(baselineKey, out var baseline))
            throw new ArgumentException($"Baseline key '{baselineKey}' does not exist.", nameof(baselineKey));

        var rows = reviews
            .Where(entry => !string.Equals(entry.Key, baselineKey, StringComparison.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new GraphShardNamedMultiGroupChangeReviewSeriesRow
            {
                Key = entry.Key,
                Comparison = CompareMultiGroupChangeReviews(entry.Value, baseline)
            })
            .ToList();

        var summary = new GraphShardNamedMultiGroupChangeReviewSeriesSummary
        {
            BaselineKey = baselineKey,
            ComparisonCount = rows.Count,
            MaxAddedNodeCountDelta = rows.Count == 0 ? 0 : rows.Max(row => row.Comparison.SelectionSummaryDelta.AddedNodeCountDelta),
            MaxRemovedNodeCountDelta = rows.Count == 0 ? 0 : rows.Max(row => row.Comparison.SelectionSummaryDelta.RemovedNodeCountDelta),
            MaxAddedRelCountDelta = rows.Count == 0 ? 0 : rows.Max(row => row.Comparison.SelectionSummaryDelta.AddedRelCountDelta),
            MaxRemovedRelCountDelta = rows.Count == 0 ? 0 : rows.Max(row => row.Comparison.SelectionSummaryDelta.RemovedRelCountDelta),
            SummaryLabel = rows.Count == 0
                ? $"baseline '{baselineKey}' with no comparison rows"
                : FormattableString.Invariant(
                    $"baseline '{baselineKey}' across {rows.Count} named comparisons; max nodes +{rows.Max(row => row.Comparison.SelectionSummaryDelta.AddedNodeCountDelta)}/-{rows.Max(row => row.Comparison.SelectionSummaryDelta.RemovedNodeCountDelta)}, rels +{rows.Max(row => row.Comparison.SelectionSummaryDelta.AddedRelCountDelta)}/-{rows.Max(row => row.Comparison.SelectionSummaryDelta.RemovedRelCountDelta)}")
        };

        return new GraphShardNamedMultiGroupChangeReviewSeriesComparison
        {
            BaselineKey = baselineKey,
            Rows = rows,
            Summary = summary
        };
    }

    public GraphShardMultiGroupChangeReviewMatrixComparison CompareMultiGroupChangeReviewMatrix(
        IReadOnlyDictionary<string, GraphShardMultiGroupChangeReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
            throw new ArgumentException("At least one review is required.", nameof(reviews));

        var ordered = reviews
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        var cells = new List<GraphShardMultiGroupChangeReviewMatrixCell>();
        foreach (var current in ordered)
        {
            foreach (var previous in ordered)
            {
                if (string.Equals(current.Key, previous.Key, StringComparison.Ordinal))
                    continue;

                cells.Add(new GraphShardMultiGroupChangeReviewMatrixCell
                {
                    CurrentKey = current.Key,
                    PreviousKey = previous.Key,
                    Comparison = CompareMultiGroupChangeReviews(current.Value, previous.Value)
                });
            }
        }

        var summary = new GraphShardMultiGroupChangeReviewMatrixSummary
        {
            ReviewCount = ordered.Count,
            ComparisonCount = cells.Count,
            MaxAddedNodeCountDelta = cells.Count == 0 ? 0 : cells.Max(cell => cell.Comparison.SelectionSummaryDelta.AddedNodeCountDelta),
            MaxRemovedNodeCountDelta = cells.Count == 0 ? 0 : cells.Max(cell => cell.Comparison.SelectionSummaryDelta.RemovedNodeCountDelta),
            MaxAddedRelCountDelta = cells.Count == 0 ? 0 : cells.Max(cell => cell.Comparison.SelectionSummaryDelta.AddedRelCountDelta),
            MaxRemovedRelCountDelta = cells.Count == 0 ? 0 : cells.Max(cell => cell.Comparison.SelectionSummaryDelta.RemovedRelCountDelta),
            SummaryLabel = cells.Count == 0
                ? $"matrix across {ordered.Count} review(s) with no comparison cells"
                : FormattableString.Invariant(
                    $"matrix across {ordered.Count} review(s) with {cells.Count} comparison cells; max nodes +{cells.Max(cell => cell.Comparison.SelectionSummaryDelta.AddedNodeCountDelta)}/-{cells.Max(cell => cell.Comparison.SelectionSummaryDelta.RemovedNodeCountDelta)}, rels +{cells.Max(cell => cell.Comparison.SelectionSummaryDelta.AddedRelCountDelta)}/-{cells.Max(cell => cell.Comparison.SelectionSummaryDelta.RemovedRelCountDelta)}")
        };

        return new GraphShardMultiGroupChangeReviewMatrixComparison
        {
            Keys = ordered.Select(entry => entry.Key).ToList(),
            Cells = cells,
            Summary = summary
        };
    }

    public GraphShardMultiGroupChangeReviewOverlap BuildMultiGroupChangeReviewOverlap(
        IReadOnlyDictionary<string, GraphShardMultiGroupChangeReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
            throw new ArgumentException("At least one review is required.", nameof(reviews));

        var ordered = reviews
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        return new GraphShardMultiGroupChangeReviewOverlap
        {
            Keys = ordered.Select(entry => entry.Key).ToList(),
            CommonNodeTableKeys = IntersectOrderedSets(ordered.Select(entry => entry.Value.NodeTableComposition.Keys)),
            CommonRelationshipTypeKeys = IntersectOrderedSets(ordered.Select(entry => entry.Value.RelationshipTypeComposition.Keys)),
            NodeTableCompositionOverlap = BuildProjectionOverlap(ordered.Select(entry => entry.Value.NodeTableComposition)),
            RelationshipTypeCompositionOverlap = BuildProjectionOverlap(ordered.Select(entry => entry.Value.RelationshipTypeComposition)),
            CombinedCompositionOverlap = BuildProjectionOverlap(ordered.Select(entry => entry.Value.CombinedComposition))
        };
    }

    public GraphShardMultiGroupChangeReviewConsensus BuildMultiGroupChangeReviewConsensus(
        IReadOnlyDictionary<string, GraphShardMultiGroupChangeReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
            throw new ArgumentException("At least one review is required.", nameof(reviews));

        var ordered = reviews
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
        var overlap = BuildMultiGroupChangeReviewOverlap(reviews);
        var commonSummary = new GraphShardSelectedChangeSummary
        {
            SelectedNodeTableCount = overlap.CommonNodeTableKeys.Count,
            SelectedRelationshipTypeCount = overlap.CommonRelationshipTypeKeys.Count,
            AddedNodeCount = overlap.CombinedCompositionOverlap.CommonAddedNodeCount,
            RemovedNodeCount = overlap.CombinedCompositionOverlap.CommonRemovedNodeCount,
            AddedRelCount = overlap.CombinedCompositionOverlap.CommonAddedRelCount,
            RemovedRelCount = overlap.CombinedCompositionOverlap.CommonRemovedRelCount,
            SummaryLabel = FormattableString.Invariant(
                $"consensus across {ordered.Count} scopes; nodeTables={overlap.CommonNodeTableKeys.Count}, relTypes={overlap.CommonRelationshipTypeKeys.Count}; nodes +{overlap.CombinedCompositionOverlap.CommonAddedNodeCount}/-{overlap.CombinedCompositionOverlap.CommonRemovedNodeCount}, rels +{overlap.CombinedCompositionOverlap.CommonAddedRelCount}/-{overlap.CombinedCompositionOverlap.CommonRemovedRelCount}")
        };

        return new GraphShardMultiGroupChangeReviewConsensus
        {
            Keys = ordered.Select(entry => entry.Key).ToList(),
            ScopeCount = ordered.Count,
            CommonSummary = commonSummary,
            Overlap = overlap
        };
    }

    public GraphShardMultiGroupChangeReviewFrequency BuildMultiGroupChangeReviewFrequency(
        IReadOnlyDictionary<string, GraphShardMultiGroupChangeReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
            throw new ArgumentException("At least one review is required.", nameof(reviews));

        var ordered = reviews
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        return new GraphShardMultiGroupChangeReviewFrequency
        {
            Keys = ordered.Select(entry => entry.Key).ToList(),
            ScopeCount = ordered.Count,
            NodeTableKeys = BuildFrequencyRows(ordered.Select(entry => entry.Value.NodeTableComposition.Keys), ordered.Count),
            RelationshipTypeKeys = BuildFrequencyRows(ordered.Select(entry => entry.Value.RelationshipTypeComposition.Keys), ordered.Count),
            NodeTableCompositionFrequency = BuildProjectionFrequency(ordered.Select(entry => entry.Value.NodeTableComposition), ordered.Count),
            RelationshipTypeCompositionFrequency = BuildProjectionFrequency(ordered.Select(entry => entry.Value.RelationshipTypeComposition), ordered.Count),
            CombinedCompositionFrequency = BuildProjectionFrequency(ordered.Select(entry => entry.Value.CombinedComposition), ordered.Count)
        };
    }

    public GraphShardMultiGroupChangeReviewThresholdConsensus BuildMultiGroupChangeReviewThresholdConsensus(
        IReadOnlyDictionary<string, GraphShardMultiGroupChangeReview> reviews,
        int minScopeCount)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
            throw new ArgumentException("At least one review is required.", nameof(reviews));
        if (minScopeCount <= 0 || minScopeCount > reviews.Count)
            throw new ArgumentOutOfRangeException(nameof(minScopeCount), minScopeCount, "minScopeCount must be between 1 and the number of scopes.");

        var frequency = BuildMultiGroupChangeReviewFrequency(reviews);
        var qualifiedNodeTableKeys = frequency.NodeTableKeys
            .Where(row => row.ScopeCount >= minScopeCount)
            .Select(row => row.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var qualifiedRelationshipTypeKeys = frequency.RelationshipTypeKeys
            .Where(row => row.ScopeCount >= minScopeCount)
            .Select(row => row.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var thresholdOverlap = new GraphShardProjectionOverlapResult
        {
            CommonAddedNodeIds = frequency.CombinedCompositionFrequency.AddedNodes.Where(row => row.ScopeCount >= minScopeCount).Select(row => row.Key).OrderBy(key => key, StringComparer.Ordinal).ToList(),
            CommonRemovedNodeIds = frequency.CombinedCompositionFrequency.RemovedNodes.Where(row => row.ScopeCount >= minScopeCount).Select(row => row.Key).OrderBy(key => key, StringComparer.Ordinal).ToList(),
            CommonAddedRelIds = frequency.CombinedCompositionFrequency.AddedRelationships.Where(row => row.ScopeCount >= minScopeCount).Select(row => row.Key).OrderBy(key => key, StringComparer.Ordinal).ToList(),
            CommonRemovedRelIds = frequency.CombinedCompositionFrequency.RemovedRelationships.Where(row => row.ScopeCount >= minScopeCount).Select(row => row.Key).OrderBy(key => key, StringComparer.Ordinal).ToList()
        };

        var thresholdSummary = new GraphShardSelectedChangeSummary
        {
            SelectedNodeTableCount = qualifiedNodeTableKeys.Count,
            SelectedRelationshipTypeCount = qualifiedRelationshipTypeKeys.Count,
            AddedNodeCount = thresholdOverlap.CommonAddedNodeCount,
            RemovedNodeCount = thresholdOverlap.CommonRemovedNodeCount,
            AddedRelCount = thresholdOverlap.CommonAddedRelCount,
            RemovedRelCount = thresholdOverlap.CommonRemovedRelCount,
            SummaryLabel = FormattableString.Invariant(
                $"threshold consensus across {frequency.ScopeCount} scopes at >= {minScopeCount}; nodeTables={qualifiedNodeTableKeys.Count}, relTypes={qualifiedRelationshipTypeKeys.Count}; nodes +{thresholdOverlap.CommonAddedNodeCount}/-{thresholdOverlap.CommonRemovedNodeCount}, rels +{thresholdOverlap.CommonAddedRelCount}/-{thresholdOverlap.CommonRemovedRelCount}")
        };

        return new GraphShardMultiGroupChangeReviewThresholdConsensus
        {
            Keys = frequency.Keys,
            ScopeCount = frequency.ScopeCount,
            MinScopeCount = minScopeCount,
            ThresholdSummary = thresholdSummary,
            Frequency = frequency,
            QualifiedNodeTableKeys = qualifiedNodeTableKeys,
            QualifiedRelationshipTypeKeys = qualifiedRelationshipTypeKeys,
            ThresholdProjectionOverlap = thresholdOverlap
        };
    }

    public GraphShardMultiGroupChangeReviewSelectionProfiles BuildMultiGroupChangeReviewSelectionProfiles(
        IReadOnlyDictionary<string, GraphShardMultiGroupChangeReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
            throw new ArgumentException("At least one review is required.", nameof(reviews));

        var ordered = reviews
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();
        var profiles = ordered
            .Select(entry =>
            {
                var nodeKeys = entry.Value.NodeTableComposition.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
                var relKeys = entry.Value.RelationshipTypeComposition.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
                return new GraphShardMultiGroupChangeReviewSelectionProfile
                {
                    Key = entry.Key,
                    SelectedNodeTableKeys = nodeKeys,
                    SelectedRelationshipTypeKeys = relKeys,
                    SelectedNodeTableCount = nodeKeys.Count,
                    SelectedRelationshipTypeCount = relKeys.Count,
                    AddedNodeCount = entry.Value.SelectionSummary.AddedNodeCount,
                    RemovedNodeCount = entry.Value.SelectionSummary.RemovedNodeCount,
                    AddedRelCount = entry.Value.SelectionSummary.AddedRelCount,
                    RemovedRelCount = entry.Value.SelectionSummary.RemovedRelCount,
                    Signature = BuildSelectionSignature(nodeKeys, relKeys)
                };
            })
            .ToList();

        return new GraphShardMultiGroupChangeReviewSelectionProfiles
        {
            Keys = ordered.Select(entry => entry.Key).ToList(),
            Profiles = profiles
        };
    }

    public GraphShardMultiGroupChangeReviewSelectionFamilies BuildMultiGroupChangeReviewSelectionFamilies(
        IReadOnlyDictionary<string, GraphShardMultiGroupChangeReview> reviews)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        if (reviews.Count == 0)
            throw new ArgumentException("At least one review is required.", nameof(reviews));

        var profiles = BuildMultiGroupChangeReviewSelectionProfiles(reviews);
        var families = profiles.Profiles
            .GroupBy(profile => profile.Signature, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return new GraphShardMultiGroupChangeReviewSelectionFamily
                {
                    Signature = group.Key,
                    Keys = group.Select(profile => profile.Key).OrderBy(key => key, StringComparer.Ordinal).ToList(),
                    SelectedNodeTableKeys = first.SelectedNodeTableKeys,
                    SelectedRelationshipTypeKeys = first.SelectedRelationshipTypeKeys,
                    ScopeCount = group.Count()
                };
            })
            .ToList();

        return new GraphShardMultiGroupChangeReviewSelectionFamilies
        {
            Keys = profiles.Keys,
            Families = families
        };
    }

    public GraphShardMultiGroupChangeReviewSelectionProfileDelta CompareSelectionProfiles(
        GraphShardMultiGroupChangeReviewSelectionProfile current,
        GraphShardMultiGroupChangeReviewSelectionProfile previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var addedNodeTableKeys = current.SelectedNodeTableKeys
            .Except(previous.SelectedNodeTableKeys, StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var removedNodeTableKeys = previous.SelectedNodeTableKeys
            .Except(current.SelectedNodeTableKeys, StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var addedRelationshipTypeKeys = current.SelectedRelationshipTypeKeys
            .Except(previous.SelectedRelationshipTypeKeys, StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        var removedRelationshipTypeKeys = previous.SelectedRelationshipTypeKeys
            .Except(current.SelectedRelationshipTypeKeys, StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        return new GraphShardMultiGroupChangeReviewSelectionProfileDelta
        {
            CurrentKey = current.Key,
            PreviousKey = previous.Key,
            SelectedNodeTableCountDelta = current.SelectedNodeTableCount - previous.SelectedNodeTableCount,
            SelectedRelationshipTypeCountDelta = current.SelectedRelationshipTypeCount - previous.SelectedRelationshipTypeCount,
            AddedNodeTableKeys = addedNodeTableKeys,
            RemovedNodeTableKeys = removedNodeTableKeys,
            AddedRelationshipTypeKeys = addedRelationshipTypeKeys,
            RemovedRelationshipTypeKeys = removedRelationshipTypeKeys,
            SummaryLabel = FormattableString.Invariant(
                $"selection profile '{current.Key}' vs '{previous.Key}': nodeTables +{addedNodeTableKeys.Count}/-{removedNodeTableKeys.Count}, relTypes +{addedRelationshipTypeKeys.Count}/-{removedRelationshipTypeKeys.Count}")
        };
    }

    public GraphShardSelectionFamilyDeltaResult CompareSelectionFamilies(
        GraphShardMultiGroupChangeReviewSelectionFamilies current,
        GraphShardMultiGroupChangeReviewSelectionFamilies previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var currentLookup = current.Families.ToDictionary(family => family.Signature, family => family, StringComparer.Ordinal);
        var previousLookup = previous.Families.ToDictionary(family => family.Signature, family => family, StringComparer.Ordinal);
        var signatures = currentLookup.Keys
            .Concat(previousLookup.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(signature => signature, StringComparer.Ordinal);

        var rows = signatures
            .Select(signature =>
            {
                currentLookup.TryGetValue(signature, out var currentFamily);
                previousLookup.TryGetValue(signature, out var previousFamily);
                var currentScopeCount = currentFamily?.ScopeCount ?? 0;
                var previousScopeCount = previousFamily?.ScopeCount ?? 0;
                var template = currentFamily ?? previousFamily ?? new GraphShardMultiGroupChangeReviewSelectionFamily { Signature = signature };
                return new GraphShardSelectionFamilyDeltaRow
                {
                    Signature = signature,
                    CurrentScopeCount = currentScopeCount,
                    PreviousScopeCount = previousScopeCount,
                    ScopeCountDelta = currentScopeCount - previousScopeCount,
                    SelectedNodeTableKeys = template.SelectedNodeTableKeys,
                    SelectedRelationshipTypeKeys = template.SelectedRelationshipTypeKeys,
                    SummaryLabel = FormattableString.Invariant(
                        $"selection family '{signature}': scopes {currentScopeCount} vs {previousScopeCount} (delta {currentScopeCount - previousScopeCount})")
                };
            })
            .ToList();

        return new GraphShardSelectionFamilyDeltaResult { Rows = rows };
    }

    public GraphShardSelectionSignatureTransitionResult BuildSelectionSignatureTransitions(
        GraphShardMultiGroupChangeReviewSelectionProfiles current,
        GraphShardMultiGroupChangeReviewSelectionProfiles previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        var currentLookup = current.Profiles.ToDictionary(profile => profile.Key, profile => profile, StringComparer.Ordinal);
        var previousLookup = previous.Profiles.ToDictionary(profile => profile.Key, profile => profile, StringComparer.Ordinal);
        var keys = currentLookup.Keys
            .Concat(previousLookup.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var rows = keys
            .Select(key =>
            {
                currentLookup.TryGetValue(key, out var currentProfile);
                previousLookup.TryGetValue(key, out var previousProfile);
                var currentSignature = currentProfile?.Signature ?? "<missing>";
                var previousSignature = previousProfile?.Signature ?? "<missing>";
                var changed = !string.Equals(currentSignature, previousSignature, StringComparison.Ordinal);

                return new GraphShardSelectionSignatureTransitionRow
                {
                    Key = key,
                    PreviousSignature = previousSignature,
                    CurrentSignature = currentSignature,
                    Changed = changed,
                    PreviousSelectedNodeTableKeys = previousProfile?.SelectedNodeTableKeys ?? [],
                    PreviousSelectedRelationshipTypeKeys = previousProfile?.SelectedRelationshipTypeKeys ?? [],
                    CurrentSelectedNodeTableKeys = currentProfile?.SelectedNodeTableKeys ?? [],
                    CurrentSelectedRelationshipTypeKeys = currentProfile?.SelectedRelationshipTypeKeys ?? [],
                    SummaryLabel = FormattableString.Invariant(
                        $"selection signature '{key}': {previousSignature} -> {currentSignature}")
                };
            })
            .ToList();

        return new GraphShardSelectionSignatureTransitionResult
        {
            Keys = keys,
            Rows = rows,
            ChangedScopeCount = rows.Count(row => row.Changed),
            UnchangedScopeCount = rows.Count(row => !row.Changed)
        };
    }

    public GraphShardSelectionSignatureTransitionCountResult SummarizeSelectionSignatureTransitions(
        GraphShardSelectionSignatureTransitionResult transitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);

        var rows = transitions.Rows
            .GroupBy(
                row => (row.PreviousSignature, row.CurrentSignature),
                StringTupleComparer.Instance)
            .OrderBy(group => group.Key.PreviousSignature, StringComparer.Ordinal)
            .ThenBy(group => group.Key.CurrentSignature, StringComparer.Ordinal)
            .Select(group => new GraphShardSelectionSignatureTransitionCountRow
            {
                PreviousSignature = group.Key.PreviousSignature,
                CurrentSignature = group.Key.CurrentSignature,
                ScopeCount = group.Count(),
                Changed = !string.Equals(group.Key.PreviousSignature, group.Key.CurrentSignature, StringComparison.Ordinal),
                SummaryLabel = FormattableString.Invariant(
                    $"transition {group.Key.PreviousSignature} -> {group.Key.CurrentSignature}: scopes {group.Count()}")
            })
            .ToList();

        return new GraphShardSelectionSignatureTransitionCountResult
        {
            Rows = rows,
            ChangedTransitionCount = rows.Count(row => row.Changed),
            UnchangedTransitionCount = rows.Count(row => !row.Changed)
        };
    }

    public GraphShardHistogramResult HistogramNodes(
        GraphShardNodeFilterSpec filter,
        string propertyName,
        decimal bucketSize)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return BuildNodeHistogram(FilterNodes(filter), propertyName, bucketSize);
    }

    public GraphShardHistogramResult HistogramRelationships(
        GraphShardRelationshipFilterSpec filter,
        string propertyName,
        decimal bucketSize)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return BuildRelationshipHistogram(FilterRelationships(filter), propertyName, bucketSize);
    }

    public GraphShardHistogramResult HistogramNodesForTable(
        string tableName,
        string propertyName,
        decimal bucketSize,
        GraphShardNodeFilterSpec? baseFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return HistogramNodes(ComposeNodeScopedFilter(baseFilter, tableName), propertyName, bucketSize);
    }

    public GraphShardHistogramResult HistogramRelationshipsForRelType(
        string relType,
        string propertyName,
        decimal bucketSize,
        GraphShardRelationshipFilterSpec? baseFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relType);
        return HistogramRelationships(ComposeRelationshipScopedFilter(baseFilter, relType), propertyName, bucketSize);
    }

    public GraphShardTimeBucketResult TimeBucketNodes(
        GraphShardNodeFilterSpec filter,
        string propertyName,
        GraphShardTimeBucketInterval interval)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return BuildNodeTimeBuckets(FilterNodes(filter), propertyName, interval);
    }

    public GraphShardTimeBucketResult TimeBucketRelationships(
        GraphShardRelationshipFilterSpec filter,
        string propertyName,
        GraphShardTimeBucketInterval interval)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return BuildRelationshipTimeBuckets(FilterRelationships(filter), propertyName, interval);
    }

    public GraphShardNeighborSummaryResult SummarizeNeighborsForTargetTable(
        string sourceNodeId,
        string targetTable,
        GraphShardNodeFilterSpec? baseFilter = null,
        bool includeOutgoing = true,
        bool includeIncoming = false,
        string? relType = null,
        params GraphShardAggregateSpec[] aggregates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTable);
        return SummarizeNeighbors(
            sourceNodeId,
            ComposeNodeScopedFilter(baseFilter, targetTable),
            includeOutgoing,
            includeIncoming,
            relType,
            aggregates);
    }

    public GraphShardRelationshipSummaryResult SummarizeRelationshipsForRelType(
        string relType,
        GraphShardRelationshipFilterSpec? baseFilter = null,
        params GraphShardAggregateSpec[] aggregates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relType);
        return SummarizeRelationships(ComposeRelationshipScopedFilter(baseFilter, relType), aggregates);
    }

    public GraphShardNodeFilterSpec CreateNodeRangeFilter(
        GraphShardNumericRangeSpec range,
        GraphShardNodeFilterSpec? baseFilter = null)
    {
        ArgumentNullException.ThrowIfNull(range);
        return ComposeNodeRangeFilter(baseFilter, range);
    }

    public GraphShardRelationshipFilterSpec CreateRelationshipRangeFilter(
        GraphShardNumericRangeSpec range,
        GraphShardRelationshipFilterSpec? baseFilter = null)
    {
        ArgumentNullException.ThrowIfNull(range);
        return ComposeRelationshipRangeFilter(baseFilter, range);
    }

    public GraphShardNodeFilterSpec CreateNodeHistogramBucketFilter(
        string propertyName,
        GraphShardHistogramBucket bucket,
        GraphShardNodeFilterSpec? baseFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(bucket);
        return ComposeNodeRangeFilter(baseFilter, new GraphShardNumericRangeSpec
        {
            PropertyName = propertyName,
            StartInclusive = bucket.StartInclusive,
            EndExclusive = bucket.EndExclusive
        });
    }

    public GraphShardRelationshipFilterSpec CreateRelationshipHistogramBucketFilter(
        string propertyName,
        GraphShardHistogramBucket bucket,
        GraphShardRelationshipFilterSpec? baseFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(bucket);
        return ComposeRelationshipRangeFilter(baseFilter, new GraphShardNumericRangeSpec
        {
            PropertyName = propertyName,
            StartInclusive = bucket.StartInclusive,
            EndExclusive = bucket.EndExclusive
        });
    }

    public GraphShard ProjectNodeHistogramBucketSubgraph(
        string propertyName,
        GraphShardHistogramBucket bucket,
        GraphShardNodeFilterSpec? baseFilter = null,
        bool includeOutgoing = true,
        bool includeIncoming = false,
        string? relType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(bucket);
        return ProjectFilteredSubgraph(
            CreateNodeHistogramBucketFilter(propertyName, bucket, baseFilter),
            includeOutgoing,
            includeIncoming,
            relType);
    }

    public GraphShard ProjectRelationshipHistogramBucketSubgraph(
        string propertyName,
        GraphShardHistogramBucket bucket,
        GraphShardRelationshipFilterSpec? baseFilter = null,
        bool includeEndpointNodes = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(bucket);
        return ProjectFilteredRelationships(
            CreateRelationshipHistogramBucketFilter(propertyName, bucket, baseFilter),
            includeEndpointNodes);
    }

    public GraphShardNeighborSummaryResult SummarizeNeighborsForNodeHistogramBucket(
        string sourceNodeId,
        string propertyName,
        GraphShardHistogramBucket bucket,
        GraphShardNodeFilterSpec? baseFilter = null,
        bool includeOutgoing = true,
        bool includeIncoming = false,
        string? relType = null,
        params GraphShardAggregateSpec[] aggregates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(bucket);
        return SummarizeNeighbors(
            sourceNodeId,
            CreateNodeHistogramBucketFilter(propertyName, bucket, baseFilter),
            includeOutgoing,
            includeIncoming,
            relType,
            aggregates);
    }

    public GraphShardRelationshipSummaryResult SummarizeRelationshipsForHistogramBucket(
        string propertyName,
        GraphShardHistogramBucket bucket,
        GraphShardRelationshipFilterSpec? baseFilter = null,
        params GraphShardAggregateSpec[] aggregates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(bucket);
        return SummarizeRelationships(
            CreateRelationshipHistogramBucketFilter(propertyName, bucket, baseFilter),
            aggregates);
    }

    public GraphShardPathResult? FindPath(
        string startExternalId,
        string endExternalId,
        GraphShardPathOptions? options = null)
    {
        if (!_view.HasNode(startExternalId))
            throw new KeyNotFoundException($"Node '{startExternalId}' was not found in the local runtime.");
        if (!_view.HasNode(endExternalId))
            throw new KeyNotFoundException($"Node '{endExternalId}' was not found in the local runtime.");

        if (string.Equals(startExternalId, endExternalId, StringComparison.Ordinal))
        {
            return new GraphShardPathResult
            {
                NodeIds = [startExternalId],
                RelIds = []
            };
        }

        var normalizedOptions = options ?? new GraphShardPathOptions();
        var visited = new HashSet<string>(StringComparer.Ordinal) { startExternalId };
        var queue = new Queue<(string NodeId, int Depth)>();
        var parents = new Dictionary<string, (string PreviousNodeId, string RelId)>(StringComparer.Ordinal);
        queue.Enqueue((startExternalId, 0));

        while (queue.Count > 0)
        {
            var (nodeId, depth) = queue.Dequeue();
            if (normalizedOptions.MaxDepth.HasValue && depth >= normalizedOptions.MaxDepth.Value)
                continue;

            foreach (var edge in EnumerateEdges(nodeId, normalizedOptions.IncludeOutgoing, normalizedOptions.IncludeIncoming, normalizedOptions.RelType))
            {
                var neighborNodeId = edge.NeighborNodeId;
                if (!visited.Add(neighborNodeId))
                    continue;

                parents[neighborNodeId] = (nodeId, edge.RelId);
                if (string.Equals(neighborNodeId, endExternalId, StringComparison.Ordinal))
                    return BuildPath(startExternalId, endExternalId, parents);

                queue.Enqueue((neighborNodeId, depth + 1));
            }
        }

        return null;
    }

    public GraphShardProjectedPath? ProjectPath(
        string startExternalId,
        string endExternalId,
        GraphShardPathOptions? options = null)
    {
        var path = FindPath(startExternalId, endExternalId, options);
        if (path is null)
            return null;

        var nodeIds = path.NodeIds.ToHashSet(StringComparer.Ordinal);
        var relIds = path.RelIds.ToHashSet(StringComparer.Ordinal);
        var shard = BuildProjectedShard(
            nodeIds,
            relIds,
            seedExternalId: startExternalId,
            depth: path.Length,
            includeOutgoing: options?.IncludeOutgoing ?? true,
            includeIncoming: options?.IncludeIncoming ?? false,
            relType: options?.RelType,
            boundaryNodeIds: new HashSet<string>(StringComparer.Ordinal));

        return new GraphShardProjectedPath
        {
            Path = path,
            Shard = GraphShardNormalizer.Normalize(shard),
            StartNode = GetRequiredNodeMatch(path.NodeIds[0]),
            EndNode = GetRequiredNodeMatch(path.NodeIds[^1]),
            Nodes = path.NodeIds.Select(GetRequiredNodeMatch).ToList(),
            Relationships = path.RelIds.Select(GetRequiredRelationshipMatch).ToList(),
            NodeTableSequence = path.NodeIds.Select(GetRequiredNodeTable).ToList(),
            RelTypeSequence = path.RelIds.Select(relId => GetRequiredRelationshipMatch(relId).RelType).ToList()
        };
    }

    public GraphShard ProjectFilteredSubgraph(
        GraphShardNodeFilterSpec filter,
        bool includeOutgoing = true,
        bool includeIncoming = true,
        string? relType = null)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var selectedMatches = FilterNodes(filter);
        if (selectedMatches.Count == 0)
        {
            return GraphShardNormalizer.Normalize(new GraphShard
            {
                FormatVersion = GraphShard.CurrentFormatVersion,
                GraphVersionToken = _shard.GraphVersionToken,
                ExtractorVersion = GraphShard.CurrentExtractorVersion,
                ExtractedAtUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ExtractionPolicy = "runtime_filter_projection",
                IsComplete = _shard.IsComplete,
                SeedProvenance = new GraphShardSeedProvenance(),
                Boundary = new GraphShardBoundary(),
                Options = new GraphShardExtractionOptions
                {
                    IncludeOutgoing = includeOutgoing,
                    IncludeIncoming = includeIncoming,
                    IncludeNodeProperties = true,
                    IncludeRelProperties = true,
                    IncludeAdjacency = true,
                    IncludeBoundaryMetadata = true,
                    StopAtBoundary = false,
                    RelTypes = string.IsNullOrWhiteSpace(relType) ? [] : [relType]
                },
                Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["runtimeFilterProjection"] = true,
                    ["selectedNodeCount"] = 0
                }
            });
        }

        var nodeIds = selectedMatches.Select(match => match.Row.ExternalId).ToHashSet(StringComparer.Ordinal);
        var relIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nodeId in nodeIds)
        {
            foreach (var edge in EnumerateEdges(nodeId, includeOutgoing, includeIncoming, relType))
            {
                if (nodeIds.Contains(edge.NeighborNodeId))
                    relIds.Add(edge.RelId);
            }
        }

        var shard = BuildProjectedShard(
            nodeIds,
            relIds,
            seedExternalId: selectedMatches.Count > 0 ? selectedMatches[0].Row.ExternalId : string.Empty,
            depth: 0,
            includeOutgoing,
            includeIncoming,
            relType,
            boundaryNodeIds: new HashSet<string>(StringComparer.Ordinal));

        return GraphShardNormalizer.Normalize(new GraphShard
        {
            FormatVersion = shard.FormatVersion,
            GraphVersionToken = shard.GraphVersionToken,
            ExtractorVersion = shard.ExtractorVersion,
            ExtractedAtUtc = shard.ExtractedAtUtc,
            ExtractionPolicy = "runtime_filter_projection",
            IsComplete = _shard.IsComplete,
            NodeTables = shard.NodeTables,
            RelTables = shard.RelTables,
            Adjacency = shard.Adjacency,
            SeedProvenance = new GraphShardSeedProvenance
            {
                RequestedCount = selectedMatches.Count,
                IncludedCount = selectedMatches.Count,
                ExcludedCount = 0,
                RequestedSeeds = selectedMatches
                    .Select(match => new GraphShardSeedRecord
                    {
                        TableName = match.TableName,
                        RequestedNodeId = match.Row.ExternalId,
                        ExternalId = match.Row.ExternalId,
                        Status = "included",
                        Reason = string.Empty
                    })
                    .ToList()
            },
            Boundary = shard.Boundary,
            Options = new GraphShardExtractionOptions
            {
                IncludeOutgoing = includeOutgoing,
                IncludeIncoming = includeIncoming,
                IncludeNodeProperties = true,
                IncludeRelProperties = true,
                IncludeAdjacency = true,
                IncludeBoundaryMetadata = true,
                StopAtBoundary = false,
                RelTypes = string.IsNullOrWhiteSpace(relType) ? [] : [relType]
            },
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runtimeFilterProjection"] = true,
                ["selectedNodeCount"] = selectedMatches.Count
            }
        });
    }

    public GraphShard ProjectNeighborhood(
        string seedExternalId,
        int depth,
        bool includeOutgoing = true,
        bool includeIncoming = false,
        string? relType = null)
    {
        if (depth < 0)
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "Depth must be >= 0.");

        if (!_view.HasNode(seedExternalId))
            throw new KeyNotFoundException($"Node '{seedExternalId}' was not found in the local runtime.");

        var nodeIds = new HashSet<string>(StringComparer.Ordinal) { seedExternalId };
        var relIds = new HashSet<string>(StringComparer.Ordinal);
        var boundaryNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<(string NodeId, int Depth)>();
        frontier.Enqueue((seedExternalId, 0));

        while (frontier.Count > 0)
        {
            var (nodeId, currentDepth) = frontier.Dequeue();
            if (currentDepth == depth)
            {
                if (HasMatchingEdges(nodeId, includeOutgoing, includeIncoming, relType))
                    boundaryNodeIds.Add(nodeId);
                continue;
            }

            foreach (var edge in EnumerateEdges(nodeId, includeOutgoing, includeIncoming, relType))
            {
                relIds.Add(edge.RelId);
                if (nodeIds.Add(edge.NeighborNodeId))
                    frontier.Enqueue((edge.NeighborNodeId, currentDepth + 1));
            }
        }

        var shard = BuildProjectedShard(
            nodeIds,
            relIds,
            seedExternalId,
            depth,
            includeOutgoing,
            includeIncoming,
            relType,
            boundaryNodeIds);

        return GraphShardNormalizer.Normalize(shard);
    }

    private void Reload(GraphShard shard)
    {
        _view = GraphShardRuntimeView.Load(shard);
        _shard = _view.Shard;
    }

    private static bool MatchesPredicate(string tableName, NodeShardRow row, GraphShardNodePredicateSpec predicate)
    {
        if (predicate.ExternalIds.Count > 0 &&
            !predicate.ExternalIds.Contains(row.ExternalId, StringComparer.Ordinal))
        {
            return false;
        }

        if (predicate.TableNames.Count > 0 &&
            !predicate.TableNames.Contains(tableName, StringComparer.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(predicate.PropertyName))
            return true;

        var hasProperty = row.Properties.TryGetValue(predicate.PropertyName, out var propertyValue);
        return predicate.Operator switch
        {
            GraphShardPredicateOperator.Exists => hasProperty && propertyValue is not null,
            _ when !hasProperty => false,
            GraphShardPredicateOperator.Equal => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) == 0,
            GraphShardPredicateOperator.NotEqual => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) != 0,
            GraphShardPredicateOperator.In => In(propertyValue, predicate.Values, predicate.CaseInsensitive),
            GraphShardPredicateOperator.NotIn => !In(propertyValue, predicate.Values, predicate.CaseInsensitive),
            GraphShardPredicateOperator.GreaterThan => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) > 0,
            GraphShardPredicateOperator.GreaterThanOrEqual => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) >= 0,
            GraphShardPredicateOperator.LessThan => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) < 0,
            GraphShardPredicateOperator.LessThanOrEqual => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) <= 0,
            GraphShardPredicateOperator.Contains => Contains(propertyValue, predicate.Value, predicate.CaseInsensitive),
            GraphShardPredicateOperator.StartsWith => StartsWith(propertyValue, predicate.Value, predicate.CaseInsensitive),
            _ => false
        };
    }

    private static bool MatchesFilter(string tableName, NodeShardRow row, GraphShardNodeFilterSpec filter)
    {
        var selfMatch = filter.Predicate is null || MatchesPredicate(tableName, row, filter.Predicate);
        var childrenMatch = filter.Children.Count switch
        {
            0 => true,
            _ when filter.Composition == GraphShardFilterComposition.All => filter.Children.All(child => MatchesFilter(tableName, row, child)),
            _ => filter.Children.Any(child => MatchesFilter(tableName, row, child))
        };
        var notMatch = filter.Not is null || !MatchesFilter(tableName, row, filter.Not);
        return selfMatch && childrenMatch && notMatch;
    }

    private static bool MatchesRelationshipPredicate(string relType, RelShardRow row, GraphShardRelationshipPredicateSpec predicate)
    {
        if (predicate.RelIds.Count > 0 &&
            !predicate.RelIds.Contains(row.RelId, StringComparer.Ordinal))
        {
            return false;
        }

        if (predicate.SourceNodeIds.Count > 0 &&
            !predicate.SourceNodeIds.Contains(row.SourceNodeId, StringComparer.Ordinal))
        {
            return false;
        }

        if (predicate.TargetNodeIds.Count > 0 &&
            !predicate.TargetNodeIds.Contains(row.TargetNodeId, StringComparer.Ordinal))
        {
            return false;
        }

        if (predicate.RelTypes.Count > 0 &&
            !predicate.RelTypes.Contains(relType, StringComparer.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(predicate.PropertyName))
            return true;

        var hasProperty = row.Properties.TryGetValue(predicate.PropertyName, out var propertyValue);
        return predicate.Operator switch
        {
            GraphShardPredicateOperator.Exists => hasProperty && propertyValue is not null,
            _ when !hasProperty => false,
            GraphShardPredicateOperator.Equal => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) == 0,
            GraphShardPredicateOperator.NotEqual => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) != 0,
            GraphShardPredicateOperator.In => In(propertyValue, predicate.Values, predicate.CaseInsensitive),
            GraphShardPredicateOperator.NotIn => !In(propertyValue, predicate.Values, predicate.CaseInsensitive),
            GraphShardPredicateOperator.GreaterThan => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) > 0,
            GraphShardPredicateOperator.GreaterThanOrEqual => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) >= 0,
            GraphShardPredicateOperator.LessThan => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) < 0,
            GraphShardPredicateOperator.LessThanOrEqual => Compare(propertyValue, predicate.Value, predicate.CaseInsensitive) <= 0,
            GraphShardPredicateOperator.Contains => Contains(propertyValue, predicate.Value, predicate.CaseInsensitive),
            GraphShardPredicateOperator.StartsWith => StartsWith(propertyValue, predicate.Value, predicate.CaseInsensitive),
            _ => false
        };
    }

    private static bool MatchesRelationshipFilter(string relType, RelShardRow row, GraphShardRelationshipFilterSpec filter)
    {
        var selfMatch = filter.Predicate is null || MatchesRelationshipPredicate(relType, row, filter.Predicate);
        var childrenMatch = filter.Children.Count switch
        {
            0 => true,
            _ when filter.Composition == GraphShardFilterComposition.All => filter.Children.All(child => MatchesRelationshipFilter(relType, row, child)),
            _ => filter.Children.Any(child => MatchesRelationshipFilter(relType, row, child))
        };
        var notMatch = filter.Not is null || !MatchesRelationshipFilter(relType, row, filter.Not);
        return selfMatch && childrenMatch && notMatch;
    }

    private static bool In(object? candidate, IReadOnlyList<object?> values, bool caseInsensitive)
    {
        if (values.Count == 0)
            return false;

        foreach (var value in values)
        {
            if (Compare(candidate, value, caseInsensitive) == 0)
                return true;
        }

        return false;
    }

    private static int Compare(object? left, object? right, bool caseInsensitive)
    {
        if (left is null && right is null)
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        if (TryConvertToDecimal(left, out var leftDecimal) && TryConvertToDecimal(right, out var rightDecimal))
            return leftDecimal.CompareTo(rightDecimal);

        var comparison = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Compare(NormalizeComparable(left), NormalizeComparable(right), comparison);
    }

    private static bool Contains(object? left, object? right, bool caseInsensitive)
    {
        if (left is null || right is null)
            return false;

        var comparison = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return NormalizeComparable(left).Contains(NormalizeComparable(right), comparison);
    }

    private static bool StartsWith(object? left, object? right, bool caseInsensitive)
    {
        if (left is null || right is null)
            return false;

        var comparison = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return NormalizeComparable(left).StartsWith(NormalizeComparable(right), comparison);
    }

    private static string NormalizeComparable(object value)
        => value switch
        {
            string s => s,
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static bool TryConvertToDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case byte b:
                result = b;
                return true;
            case sbyte sb:
                result = sb;
                return true;
            case short s:
                result = s;
                return true;
            case ushort us:
                result = us;
                return true;
            case int i:
                result = i;
                return true;
            case uint ui:
                result = ui;
                return true;
            case long l:
                result = l;
                return true;
            case ulong ul when ul <= long.MaxValue:
                result = ul;
                return true;
            case float f:
                result = (decimal)f;
                return true;
            case double d:
                result = (decimal)d;
                return true;
            case decimal dec:
                result = dec;
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static GraphShardNodeFilterSpec ComposeNodeRangeFilter(
        GraphShardNodeFilterSpec? baseFilter,
        GraphShardNumericRangeSpec range)
    {
        ValidateRange(range);

        var children = new List<GraphShardNodeFilterSpec>();
        if (baseFilter is not null)
            children.Add(baseFilter);

        if (range.StartInclusive.HasValue)
        {
            children.Add(new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    PropertyName = range.PropertyName,
                    Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                    Value = range.StartInclusive.Value
                }
            });
        }

        if (range.EndExclusive.HasValue)
        {
            children.Add(new GraphShardNodeFilterSpec
            {
                Predicate = new GraphShardNodePredicateSpec
                {
                    PropertyName = range.PropertyName,
                    Operator = GraphShardPredicateOperator.LessThan,
                    Value = range.EndExclusive.Value
                }
            });
        }

        return children.Count switch
        {
            0 => new GraphShardNodeFilterSpec(),
            1 => children[0],
            _ => new GraphShardNodeFilterSpec
            {
                Composition = GraphShardFilterComposition.All,
                Children = children
            }
        };
    }

    private static GraphShardRelationshipFilterSpec ComposeRelationshipRangeFilter(
        GraphShardRelationshipFilterSpec? baseFilter,
        GraphShardNumericRangeSpec range)
    {
        ValidateRange(range);

        var children = new List<GraphShardRelationshipFilterSpec>();
        if (baseFilter is not null)
            children.Add(baseFilter);

        if (range.StartInclusive.HasValue)
        {
            children.Add(new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = range.PropertyName,
                    Operator = GraphShardPredicateOperator.GreaterThanOrEqual,
                    Value = range.StartInclusive.Value
                }
            });
        }

        if (range.EndExclusive.HasValue)
        {
            children.Add(new GraphShardRelationshipFilterSpec
            {
                Predicate = new GraphShardRelationshipPredicateSpec
                {
                    PropertyName = range.PropertyName,
                    Operator = GraphShardPredicateOperator.LessThan,
                    Value = range.EndExclusive.Value
                }
            });
        }

        return children.Count switch
        {
            0 => new GraphShardRelationshipFilterSpec(),
            1 => children[0],
            _ => new GraphShardRelationshipFilterSpec
            {
                Composition = GraphShardFilterComposition.All,
                Children = children
            }
        };
    }

    private static GraphShardNodeFilterSpec ComposeNodeScopedFilter(
        GraphShardNodeFilterSpec? baseFilter,
        string tableName)
    {
        var scope = new GraphShardNodeFilterSpec
        {
            Predicate = new GraphShardNodePredicateSpec
            {
                TableNames = [tableName],
                PropertyName = string.Empty
            }
        };

        return baseFilter is null
            ? scope
            : new GraphShardNodeFilterSpec
            {
                Composition = GraphShardFilterComposition.All,
                Children = [scope, baseFilter]
            };
    }

    private static GraphShardRelationshipFilterSpec ComposeRelationshipScopedFilter(
        GraphShardRelationshipFilterSpec? baseFilter,
        string relType)
    {
        var scope = new GraphShardRelationshipFilterSpec
        {
            Predicate = new GraphShardRelationshipPredicateSpec
            {
                RelTypes = [relType],
                PropertyName = string.Empty
            }
        };

        return baseFilter is null
            ? scope
            : new GraphShardRelationshipFilterSpec
            {
                Composition = GraphShardFilterComposition.All,
                Children = [scope, baseFilter]
            };
    }

    private static void ValidateRange(GraphShardNumericRangeSpec range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(range.PropertyName);
        if (range.StartInclusive.HasValue &&
            range.EndExclusive.HasValue &&
            range.StartInclusive.Value >= range.EndExclusive.Value)
        {
            throw new ArgumentException("StartInclusive must be less than EndExclusive.", nameof(range));
        }
    }

    private static GraphShardAggregateResult AggregateNodeMatches(
        IReadOnlyList<GraphShardNodeMatch> matches,
        string? groupByProperty,
        IReadOnlyList<GraphShardAggregateSpec> aggregates)
    {
        var groups = matches
            .GroupBy(match => GetNodeGroupKey(match, groupByProperty), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        var rows = new List<GraphShardAggregateRow>();
        foreach (var group in groups)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var aggregate in aggregates)
            {
                values[aggregate.Key] = ComputeNodeAggregate(group, aggregate);
            }

            rows.Add(new GraphShardAggregateRow
            {
                GroupKey = group.Key,
                Values = values
            });
        }

        return new GraphShardAggregateResult { Rows = rows };
    }

    private static GraphShardAggregateResult AggregateRelationshipMatches(
        IReadOnlyList<GraphShardRelationshipMatch> matches,
        string? groupByProperty,
        IReadOnlyList<GraphShardAggregateSpec> aggregates)
    {
        var groups = matches
            .GroupBy(match => GetRelationshipGroupKey(match, groupByProperty), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        var rows = new List<GraphShardAggregateRow>();
        foreach (var group in groups)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var aggregate in aggregates)
            {
                values[aggregate.Key] = ComputeRelationshipAggregate(group, aggregate);
            }

            rows.Add(new GraphShardAggregateRow
            {
                GroupKey = group.Key,
                Values = values
            });
        }

        return new GraphShardAggregateResult { Rows = rows };
    }

    private static IEnumerable<GraphShardNodeMatch> OrderNodeMatches(
        IEnumerable<GraphShardNodeMatch> matches,
        IReadOnlyList<GraphShardNodeSortSpec> sorts)
    {
        var list = matches.ToList();
        list.Sort((left, right) => CompareNodeMatches(left, right, sorts));
        return list;
    }

    private static IEnumerable<GraphShardRelationshipMatch> OrderRelationshipMatches(
        IEnumerable<GraphShardRelationshipMatch> matches,
        IReadOnlyList<GraphShardRelationshipSortSpec> sorts)
    {
        var list = matches.ToList();
        list.Sort((left, right) => CompareRelationshipMatches(left, right, sorts));
        return list;
    }

    private static IEnumerable<T> ApplyPage<T>(IEnumerable<T> items, GraphShardPageSpec? page)
    {
        if (page is null)
            return items;

        var offset = Math.Max(page.Offset, 0);
        var limit = page.Limit < 0 ? 0 : page.Limit;
        return items.Skip(offset).Take(limit);
    }

    private static string GetNodeSortValue(GraphShardNodeMatch match, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return match.Row.ExternalId;

        return match.Row.Properties.TryGetValue(propertyName, out var value)
            ? NormalizeComparable(value ?? string.Empty)
            : "<missing>";
    }

    private static string GetRelationshipSortValue(GraphShardRelationshipMatch match, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return match.Row.RelId;

        if (string.Equals(propertyName, "relType", StringComparison.Ordinal))
            return match.RelType;

        return match.Row.Properties.TryGetValue(propertyName, out var value)
            ? NormalizeComparable(value ?? string.Empty)
            : "<missing>";
    }

    private static IReadOnlyList<GraphShardNodeSortSpec> GetNodeSorts(GraphShardNodeSortSpec? sort)
        => GetNodeSorts(sort is null ? null : [sort]);

    private static IReadOnlyList<GraphShardNodeSortSpec> GetNodeSorts(IReadOnlyList<GraphShardNodeSortSpec>? sorts)
        => sorts is { Count: > 0 } ? sorts : [new GraphShardNodeSortSpec()];

    private static IReadOnlyList<GraphShardRelationshipSortSpec> GetRelationshipSorts(GraphShardRelationshipSortSpec? sort)
        => GetRelationshipSorts(sort is null ? null : [sort]);

    private static IReadOnlyList<GraphShardRelationshipSortSpec> GetRelationshipSorts(IReadOnlyList<GraphShardRelationshipSortSpec>? sorts)
        => sorts is { Count: > 0 } ? sorts : [new GraphShardRelationshipSortSpec()];

    private static int CompareNodeMatches(
        GraphShardNodeMatch left,
        GraphShardNodeMatch right,
        IReadOnlyList<GraphShardNodeSortSpec> sorts)
    {
        foreach (var sort in sorts)
        {
            var comparison = string.Compare(
                GetNodeSortValue(left, sort.PropertyName),
                GetNodeSortValue(right, sort.PropertyName),
                StringComparison.Ordinal);
            if (comparison != 0)
                return sort.Direction == GraphShardSortDirection.Desc ? -comparison : comparison;
        }

        return string.Compare(left.Row.ExternalId, right.Row.ExternalId, StringComparison.Ordinal);
    }

    private static int CompareRelationshipMatches(
        GraphShardRelationshipMatch left,
        GraphShardRelationshipMatch right,
        IReadOnlyList<GraphShardRelationshipSortSpec> sorts)
    {
        foreach (var sort in sorts)
        {
            var comparison = string.Compare(
                GetRelationshipSortValue(left, sort.PropertyName),
                GetRelationshipSortValue(right, sort.PropertyName),
                StringComparison.Ordinal);
            if (comparison != 0)
                return sort.Direction == GraphShardSortDirection.Desc ? -comparison : comparison;
        }

        return string.Compare(left.Row.RelId, right.Row.RelId, StringComparison.Ordinal);
    }

    private static GraphShardCursorPageResult<T> ApplyCursorPage<T, TSort>(
        IReadOnlyList<T> ordered,
        GraphShardCursorPageSpec? page,
        IReadOnlyList<TSort> sorts,
        Func<T, IReadOnlyList<string>> getValues,
        Func<T, string> getId)
    {
        var normalizedPage = page ?? new GraphShardCursorPageSpec();
        var startIndex = 0;
        if (!string.IsNullOrWhiteSpace(normalizedPage.AfterCursor))
        {
            var cursor = DecodeCursor(normalizedPage.AfterCursor);
            startIndex = FindCursorStartIndex(ordered, sorts, cursor, getValues, getId);
        }

        var limit = normalizedPage.Limit <= 0 ? 0 : normalizedPage.Limit;
        var items = ordered.Skip(startIndex).Take(limit).ToList();
        var nextCursor = startIndex + items.Count < ordered.Count && items.Count > 0
            ? EncodeCursor(getValues(items[^1]), getId(items[^1]))
            : null;

        return new GraphShardCursorPageResult<T>
        {
            Items = items,
            NextCursor = nextCursor
        };
    }

    private static List<TRow> ApplyTopN<TRow>(
        IEnumerable<TRow> rows,
        GraphShardTopNSpec? topN,
        Func<TRow, int> getCount,
        Func<TRow, IReadOnlyDictionary<string, object?>> getAggregateValues,
        Func<TRow, IReadOnlyList<string>> getTieBreakKeys)
    {
        var spec = topN ?? new GraphShardTopNSpec();
        var limit = spec.Limit < 0 ? 0 : spec.Limit;
        return rows
            .OrderBy(row => row, Comparer<TRow>.Create((left, right) =>
                CompareTopNRows(left, right, spec, getCount, getAggregateValues, getTieBreakKeys)))
            .Take(limit)
            .ToList();
    }

    private static int CompareTopNRows<TRow>(
        TRow left,
        TRow right,
        GraphShardTopNSpec spec,
        Func<TRow, int> getCount,
        Func<TRow, IReadOnlyDictionary<string, object?>> getAggregateValues,
        Func<TRow, IReadOnlyList<string>> getTieBreakKeys)
    {
        int comparison;
        if (string.IsNullOrWhiteSpace(spec.AggregateKey))
        {
            comparison = getCount(left).CompareTo(getCount(right));
        }
        else
        {
            var leftValues = getAggregateValues(left);
            var rightValues = getAggregateValues(right);
            leftValues.TryGetValue(spec.AggregateKey, out var leftValue);
            rightValues.TryGetValue(spec.AggregateKey, out var rightValue);
            comparison = Compare(leftValue, rightValue, caseInsensitive: false);
        }

        if (comparison != 0)
            return spec.Direction == GraphShardSortDirection.Desc ? -comparison : comparison;

        var leftKeys = getTieBreakKeys(left);
        var rightKeys = getTieBreakKeys(right);
        for (var i = 0; i < Math.Min(leftKeys.Count, rightKeys.Count); i++)
        {
            comparison = string.Compare(leftKeys[i], rightKeys[i], StringComparison.Ordinal);
            if (comparison != 0)
                return comparison;
        }

        return leftKeys.Count.CompareTo(rightKeys.Count);
    }

    private static int FindCursorStartIndex<T, TSort>(
        IReadOnlyList<T> ordered,
        IReadOnlyList<TSort> sorts,
        CursorState cursor,
        Func<T, IReadOnlyList<string>> getValues,
        Func<T, string> getId)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            if (CompareCursor(getValues(item), getId(item), cursor.Values, cursor.Id, sorts) > 0)
                return i;
        }

        return ordered.Count;
    }

    private static int CompareCursor<TSort>(
        IReadOnlyList<string> itemValues,
        string itemId,
        IReadOnlyList<string> cursorValues,
        string cursorId,
        IReadOnlyList<TSort> sorts)
    {
        for (var i = 0; i < itemValues.Count; i++)
        {
            var comparison = string.Compare(itemValues[i], cursorValues[i], StringComparison.Ordinal);
            if (comparison != 0)
            {
                var direction = sorts[i] switch
                {
                    GraphShardNodeSortSpec nodeSort => nodeSort.Direction,
                    GraphShardRelationshipSortSpec relSort => relSort.Direction,
                    _ => GraphShardSortDirection.Asc
                };
                return direction == GraphShardSortDirection.Desc ? -comparison : comparison;
            }
        }

        return string.Compare(itemId, cursorId, StringComparison.Ordinal);
    }

    private static CursorState DecodeCursor(string cursor)
    {
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        return System.Text.Json.JsonSerializer.Deserialize<CursorState>(json)
            ?? throw new InvalidOperationException("Cursor payload could not be deserialized.");
    }

    private static string EncodeCursor(IReadOnlyList<string> values, string id)
    {
        var state = new CursorState
        {
            Values = [.. values],
            Id = id
        };
        var json = System.Text.Json.JsonSerializer.Serialize(state);
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    private static IReadOnlyList<string> GetNodeCursorValues(
        GraphShardNodeMatch match,
        IReadOnlyList<GraphShardNodeSortSpec> sorts)
        => sorts.Select(sort => GetNodeSortValue(match, sort.PropertyName)).ToList();

    private static IReadOnlyList<string> GetRelationshipCursorValues(
        GraphShardRelationshipMatch match,
        IReadOnlyList<GraphShardRelationshipSortSpec> sorts)
        => sorts.Select(sort => GetRelationshipSortValue(match, sort.PropertyName)).ToList();

    private sealed record NeighborAggregateItem
    {
        public ShardEdgeRef Edge { get; init; } = new();
        public GraphShardNodeMatch Neighbor { get; init; } = new();
        public GraphShardRelationshipMatch Relationship { get; init; } = new();
    }

    private static Dictionary<string, object?> ComputeNeighborSummaryAggregates(
        IEnumerable<NeighborAggregateItem> group,
        IReadOnlyList<GraphShardAggregateSpec> aggregates)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var aggregate in aggregates)
        {
            values[aggregate.Key] = aggregate.Function switch
            {
                GraphShardAggregateFunction.Count => group.Count(),
                GraphShardAggregateFunction.Sum => Sum(group.Select(item => GetNeighborAggregateValue(item, aggregate))),
                _ => throw new ArgumentOutOfRangeException(nameof(aggregate.Function), aggregate.Function, "Unsupported aggregate function.")
            };
        }

        return values;
    }

    private static object? GetNeighborAggregateValue(NeighborAggregateItem item, GraphShardAggregateSpec aggregate)
    {
        if (string.IsNullOrWhiteSpace(aggregate.PropertyName))
            return null;

        var properties = aggregate.Source == GraphShardAggregateSource.Relationship
            ? item.Relationship.Row.Properties
            : item.Neighbor.Row.Properties;
        return properties.TryGetValue(aggregate.PropertyName, out var value) ? value : null;
    }

    private static Dictionary<string, object?> ComputeRelationshipSummaryAggregates(
        IEnumerable<GraphShardRelationshipMatch> group,
        IReadOnlyList<GraphShardAggregateSpec> aggregates)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var aggregate in aggregates)
        {
            values[aggregate.Key] = aggregate.Function switch
            {
                GraphShardAggregateFunction.Count => group.Count(),
                GraphShardAggregateFunction.Sum => Sum(group.Select(match => GetRelationshipSummaryAggregateValue(match, aggregate))),
                _ => throw new ArgumentOutOfRangeException(nameof(aggregate.Function), aggregate.Function, "Unsupported aggregate function.")
            };
        }

        return values;
    }

    private static object? GetRelationshipSummaryAggregateValue(
        GraphShardRelationshipMatch match,
        GraphShardAggregateSpec aggregate)
    {
        if (string.IsNullOrWhiteSpace(aggregate.PropertyName))
            return null;

        return aggregate.Source switch
        {
            GraphShardAggregateSource.Relationship => match.Row.Properties.TryGetValue(aggregate.PropertyName, out var relValue) ? relValue : null,
            _ => null
        };
    }

    private static GraphShardHistogramResult BuildNodeHistogram(
        IReadOnlyList<GraphShardNodeMatch> matches,
        string propertyName,
        decimal bucketSize)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("PropertyName is required.", nameof(propertyName));
        if (bucketSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bucketSize), bucketSize, "Bucket size must be > 0.");

        var values = matches
            .Select(match => GetHistogramDecimal(match.Row.Properties, propertyName))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        var totalCount = values.Count;

        var buckets = values
            .GroupBy(value => decimal.Floor(value / bucketSize) * bucketSize)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var count = group.Count();
                return new GraphShardHistogramBucket
                {
                    Label = FormatNumericBucketLabel(group.Key, group.Key + bucketSize),
                    StartInclusive = group.Key,
                    EndExclusive = group.Key + bucketSize,
                    Count = count,
                    TotalCount = totalCount,
                    Share = ComputeShare(count, totalCount)
                };
            })
            .ToList();

        return new GraphShardHistogramResult
        {
            PropertyName = propertyName,
            TotalCount = totalCount,
            Buckets = buckets
        };
    }

    private static GraphShardHistogramResult BuildRelationshipHistogram(
        IReadOnlyList<GraphShardRelationshipMatch> matches,
        string propertyName,
        decimal bucketSize)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            throw new ArgumentException("PropertyName is required.", nameof(propertyName));
        if (bucketSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bucketSize), bucketSize, "Bucket size must be > 0.");

        var values = matches
            .Select(match => GetHistogramDecimal(match.Row.Properties, propertyName))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        var totalCount = values.Count;

        var buckets = values
            .GroupBy(value => decimal.Floor(value / bucketSize) * bucketSize)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var count = group.Count();
                return new GraphShardHistogramBucket
                {
                    Label = FormatNumericBucketLabel(group.Key, group.Key + bucketSize),
                    StartInclusive = group.Key,
                    EndExclusive = group.Key + bucketSize,
                    Count = count,
                    TotalCount = totalCount,
                    Share = ComputeShare(count, totalCount)
                };
            })
            .ToList();

        return new GraphShardHistogramResult
        {
            PropertyName = propertyName,
            TotalCount = totalCount,
            Buckets = buckets
        };
    }

    private static GraphShardTimeBucketResult BuildNodeTimeBuckets(
        IReadOnlyList<GraphShardNodeMatch> matches,
        string propertyName,
        GraphShardTimeBucketInterval interval)
    {
        var values = matches
            .Select(match => GetTemporalBucketValue(match.Row.Properties, propertyName))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        return BuildTimeBucketResult(values, propertyName, interval);
    }

    private static GraphShardTimeBucketResult BuildRelationshipTimeBuckets(
        IReadOnlyList<GraphShardRelationshipMatch> matches,
        string propertyName,
        GraphShardTimeBucketInterval interval)
    {
        var values = matches
            .Select(match => GetTemporalBucketValue(match.Row.Properties, propertyName))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        return BuildTimeBucketResult(values, propertyName, interval);
    }

    private static GraphShardTimeBucketResult BuildTimeBucketResult(
        IReadOnlyList<DateTimeOffset> values,
        string propertyName,
        GraphShardTimeBucketInterval interval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        var totalCount = values.Count;
        var buckets = values
            .GroupBy(value => GetTimeBucketStart(value, interval))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var count = group.Count();
                var start = group.Key;
                var end = GetTimeBucketEnd(start, interval);
                return new GraphShardTimeBucket
                {
                    BucketKey = FormatTimeBucketKey(start, interval),
                    Label = FormatTimeBucketLabel(start, end, interval),
                    StartInclusive = start.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    EndExclusive = end.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    Count = count,
                    TotalCount = totalCount,
                    Share = ComputeShare(count, totalCount)
                };
            })
            .ToList();

        return new GraphShardTimeBucketResult
        {
            PropertyName = propertyName,
            Interval = interval,
            TotalCount = totalCount,
            Buckets = buckets
        };
    }

    private static decimal? GetHistogramDecimal(
        IReadOnlyDictionary<string, object?> properties,
        string propertyName)
    {
        if (!properties.TryGetValue(propertyName, out var value) || value is null)
            return null;

        return TryConvertToDecimal(value, out var numeric) ? numeric : null;
    }

    private static decimal ComputeShare(int count, int totalCount)
        => totalCount <= 0 ? 0m : count / (decimal)totalCount;

    private static DateTimeOffset? GetTemporalBucketValue(
        IReadOnlyDictionary<string, object?> properties,
        string propertyName)
    {
        if (!properties.TryGetValue(propertyName, out var value) || value is null)
            return null;

        return TryConvertToDateTimeOffset(value, out var result) ? result : null;
    }

    private static bool TryConvertToDateTimeOffset(object value, out DateTimeOffset result)
    {
        switch (value)
        {
            case DateTimeOffset dto:
                result = dto.ToUniversalTime();
                return true;
            case DateTime dt:
                result = dt.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
                    : new DateTimeOffset(dt.ToUniversalTime());
                return true;
            case DateOnly dateOnly:
                result = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                return true;
            case string s when DateTimeOffset.TryParse(
                s,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var parsed):
                result = parsed.ToUniversalTime();
                return true;
            case string s when DateOnly.TryParse(
                s,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedDate):
                result = new DateTimeOffset(parsedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                return true;
            default:
                result = default;
                return false;
        }
    }

    private static DateTimeOffset GetTimeBucketStart(DateTimeOffset value, GraphShardTimeBucketInterval interval)
        => interval switch
        {
            GraphShardTimeBucketInterval.Year => new DateTimeOffset(value.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            GraphShardTimeBucketInterval.Month => new DateTimeOffset(value.Year, value.Month, 1, 0, 0, 0, TimeSpan.Zero),
            GraphShardTimeBucketInterval.Day => new DateTimeOffset(value.Year, value.Month, value.Day, 0, 0, 0, TimeSpan.Zero),
            GraphShardTimeBucketInterval.Hour => new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, 0, 0, TimeSpan.Zero),
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unsupported time bucket interval.")
        };

    private static DateTimeOffset GetTimeBucketEnd(DateTimeOffset start, GraphShardTimeBucketInterval interval)
        => interval switch
        {
            GraphShardTimeBucketInterval.Year => start.AddYears(1),
            GraphShardTimeBucketInterval.Month => start.AddMonths(1),
            GraphShardTimeBucketInterval.Day => start.AddDays(1),
            GraphShardTimeBucketInterval.Hour => start.AddHours(1),
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unsupported time bucket interval.")
        };

    private static string FormatTimeBucketKey(DateTimeOffset start, GraphShardTimeBucketInterval interval)
        => interval switch
        {
            GraphShardTimeBucketInterval.Year => start.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture),
            GraphShardTimeBucketInterval.Month => start.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            GraphShardTimeBucketInterval.Day => start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            GraphShardTimeBucketInterval.Hour => start.ToString("yyyy-MM-ddTHH:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            _ => start.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };

    private static string FormatNeighborSummaryLabel(string relType, string targetTable)
        => $"{relType} -> {targetTable}";

    private static string FormatRelationshipSummaryLabel(string relType, string sourceTable, string targetTable)
        => $"{sourceTable} -[{relType}]-> {targetTable}";

    private static string FormatNumericBucketLabel(decimal startInclusive, decimal endExclusive)
        => $"[{startInclusive}, {endExclusive})";

    private static string FormatTimeBucketLabel(DateTimeOffset start, DateTimeOffset end, GraphShardTimeBucketInterval interval)
        => interval switch
        {
            GraphShardTimeBucketInterval.Year => start.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture),
            GraphShardTimeBucketInterval.Month => start.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
            GraphShardTimeBucketInterval.Day => start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            GraphShardTimeBucketInterval.Hour => $"{start:yyyy-MM-dd HH}:00 - {end:HH}:00",
            _ => start.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };

    private static int CompareDeltaRows(
        int leftCountDelta,
        decimal leftShareDelta,
        string leftKey,
        int rightCountDelta,
        decimal rightShareDelta,
        string rightKey,
        GraphShardDeltaTopNSpec spec)
    {
        int comparison;
        if (spec.Metric == GraphShardDeltaMetric.ShareDelta)
        {
            var left = spec.UseAbsoluteValue ? Math.Abs(leftShareDelta) : leftShareDelta;
            var right = spec.UseAbsoluteValue ? Math.Abs(rightShareDelta) : rightShareDelta;
            comparison = left.CompareTo(right);
        }
        else
        {
            var left = spec.UseAbsoluteValue ? Math.Abs(leftCountDelta) : leftCountDelta;
            var right = spec.UseAbsoluteValue ? Math.Abs(rightCountDelta) : rightCountDelta;
            comparison = left.CompareTo(right);
        }

        if (comparison != 0)
            return spec.Direction == GraphShardSortDirection.Desc ? -comparison : comparison;

        return string.Compare(leftKey, rightKey, StringComparison.Ordinal);
    }

    private static GraphShardDeltaTopNSpec NormalizeDirectionalDeltaSpec(GraphShardDeltaTopNSpec? spec, GraphShardSortDirection direction)
    {
        var effective = spec ?? new GraphShardDeltaTopNSpec();
        return effective with { Direction = direction, UseAbsoluteValue = false };
    }

    private static GraphShardSummaryDeltaResult FilterSummaryDeltas(GraphShardSummaryDeltaResult deltas, bool positive)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        return new GraphShardSummaryDeltaResult
        {
            Rows = deltas.Rows
                .Where(row => positive ? row.CountDelta > 0 || row.ShareDelta > 0m : row.CountDelta < 0 || row.ShareDelta < 0m)
                .ToList()
        };
    }

    private static GraphShardBucketDeltaResult FilterBucketDeltas(GraphShardBucketDeltaResult deltas, bool positive)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        return new GraphShardBucketDeltaResult
        {
            Rows = deltas.Rows
                .Where(row => positive ? row.CountDelta > 0 || row.ShareDelta > 0m : row.CountDelta < 0 || row.ShareDelta < 0m)
                .ToList()
        };
    }

    private static Dictionary<string, int> CountProjectionMembership(
        IEnumerable<string> ids,
        IReadOnlyDictionary<string, string> classificationLookup)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!classificationLookup.TryGetValue(id, out var groupKey))
                groupKey = "<unknown>";
            counts[groupKey] = counts.GetValueOrDefault(groupKey) + 1;
        }

        return counts;
    }

    private static HashSet<string> GetEndpointNodeIds(
        GraphShardLocalRuntime runtime,
        IReadOnlySet<string> relIds)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relId in relIds)
        {
            var rel = runtime.GetRequiredRelationshipMatch(relId);
            nodeIds.Add(rel.Row.SourceNodeId);
            nodeIds.Add(rel.Row.TargetNodeId);
        }

        return nodeIds;
    }

    private static GraphShard BuildChangeProjectionSubset(
        GraphShardLocalRuntime runtime,
        IReadOnlySet<string> nodeIds,
        IReadOnlySet<string> relIds,
        string changeKey,
        string extractionPolicy)
    {
        if (nodeIds.Count == 0 && relIds.Count == 0)
        {
            return GraphShardNormalizer.Normalize(new GraphShard
            {
                FormatVersion = GraphShard.CurrentFormatVersion,
                GraphVersionToken = runtime._shard.GraphVersionToken,
                ExtractorVersion = GraphShard.CurrentExtractorVersion,
                ExtractedAtUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ExtractionPolicy = extractionPolicy,
                IsComplete = runtime._shard.IsComplete,
                Boundary = new GraphShardBoundary(),
                SeedProvenance = new GraphShardSeedProvenance(),
                Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["runtimeChangeProjection"] = true,
                    ["changeKey"] = changeKey
                }
            });
        }

        var expandedNodeIds = new HashSet<string>(nodeIds, StringComparer.Ordinal);
        foreach (var relId in relIds)
        {
            var rel = runtime.GetRequiredRelationshipMatch(relId);
            expandedNodeIds.Add(rel.Row.SourceNodeId);
            expandedNodeIds.Add(rel.Row.TargetNodeId);
        }

        var projected = runtime.BuildProjectedShard(
            expandedNodeIds,
            relIds,
            seedExternalId: string.Empty,
            depth: 0,
            includeOutgoing: true,
            includeIncoming: true,
            relType: null,
            boundaryNodeIds: new HashSet<string>(StringComparer.Ordinal));

        return GraphShardNormalizer.Normalize(new GraphShard
        {
            FormatVersion = projected.FormatVersion,
            GraphVersionToken = projected.GraphVersionToken,
            ExtractorVersion = projected.ExtractorVersion,
            ExtractedAtUtc = projected.ExtractedAtUtc,
            ExtractionPolicy = extractionPolicy,
            IsComplete = projected.IsComplete,
            NodeTables = projected.NodeTables,
            RelTables = projected.RelTables,
            Adjacency = projected.Adjacency,
            SeedProvenance = projected.SeedProvenance,
            Boundary = projected.Boundary,
            Options = projected.Options,
            Stats = projected.Stats,
            Metadata = new Dictionary<string, object?>(projected.Metadata, StringComparer.Ordinal)
            {
                ["runtimeChangeProjection"] = true,
                ["changeKey"] = changeKey
            }
        });
    }

    private static GraphShardComposedChangeProjection BuildComposedChangeProjection(
        string scope,
        List<string> keys,
        List<GraphShardChangeProjectionDrilldown> drilldowns,
        int selectedNodeTableCount,
        int selectedRelationshipTypeCount)
    {
        var addedProjection = drilldowns.Count == 0
            ? new GraphShard()
            : GraphShardMerger.Merge(drilldowns.Select(static drilldown => drilldown.AddedProjection));
        var removedProjection = drilldowns.Count == 0
            ? new GraphShard()
            : GraphShardMerger.Merge(drilldowns.Select(static drilldown => drilldown.RemovedProjection));

        var summary = new GraphShardSelectedChangeSummary
        {
            SelectedNodeTableCount = selectedNodeTableCount,
            SelectedRelationshipTypeCount = selectedRelationshipTypeCount,
            AddedNodeCount = CountShardNodes(addedProjection),
            RemovedNodeCount = CountShardNodes(removedProjection),
            AddedRelCount = CountShardRelationships(addedProjection),
            RemovedRelCount = CountShardRelationships(removedProjection),
            SummaryLabel = FormattableString.Invariant(
                $"selected nodeTables={selectedNodeTableCount}, relTypes={selectedRelationshipTypeCount}; nodes +{CountShardNodes(addedProjection)}/-{CountShardNodes(removedProjection)}, rels +{CountShardRelationships(addedProjection)}/-{CountShardRelationships(removedProjection)}")
        };

        return new GraphShardComposedChangeProjection
        {
            Scope = scope,
            Keys = keys,
            Drilldowns = drilldowns,
            AddedProjection = addedProjection,
            RemovedProjection = removedProjection,
            Summary = summary
        };
    }

    private static GraphShardProjectionOverlapResult BuildProjectionOverlap(
        IEnumerable<GraphShardComposedChangeProjection> projections)
    {
        var ordered = projections.ToList();
        if (ordered.Count == 0)
            return new GraphShardProjectionOverlapResult();

        return new GraphShardProjectionOverlapResult
        {
            CommonAddedNodeIds = IntersectOrderedSets(ordered.Select(projection => GetShardNodeIds(projection.AddedProjection))),
            CommonRemovedNodeIds = IntersectOrderedSets(ordered.Select(projection => GetShardNodeIds(projection.RemovedProjection))),
            CommonAddedRelIds = IntersectOrderedSets(ordered.Select(projection => GetShardRelationshipIds(projection.AddedProjection))),
            CommonRemovedRelIds = IntersectOrderedSets(ordered.Select(projection => GetShardRelationshipIds(projection.RemovedProjection)))
        };
    }

    private static GraphShardProjectionFrequencyResult BuildProjectionFrequency(
        IEnumerable<GraphShardComposedChangeProjection> projections,
        int scopeCount)
    {
        var ordered = projections.ToList();
        return new GraphShardProjectionFrequencyResult
        {
            AddedNodes = BuildFrequencyRows(ordered.Select(projection => GetShardNodeIds(projection.AddedProjection)), scopeCount),
            RemovedNodes = BuildFrequencyRows(ordered.Select(projection => GetShardNodeIds(projection.RemovedProjection)), scopeCount),
            AddedRelationships = BuildFrequencyRows(ordered.Select(projection => GetShardRelationshipIds(projection.AddedProjection)), scopeCount),
            RemovedRelationships = BuildFrequencyRows(ordered.Select(projection => GetShardRelationshipIds(projection.RemovedProjection)), scopeCount)
        };
    }

    private static int CountShardNodes(GraphShard shard)
        => shard.NodeTables.Values.Sum(static table => table.Rows.Count);

    private static int CountShardRelationships(GraphShard shard)
        => shard.RelTables.Values.Sum(static table => table.Rows.Count);

    private static List<string> GetShardNodeIds(GraphShard shard)
        => shard.NodeTables.Values
            .SelectMany(static table => table.Rows)
            .Select(static row => row.ExternalId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToList();

    private static List<string> GetShardRelationshipIds(GraphShard shard)
        => shard.RelTables.Values
            .SelectMany(static table => table.Rows)
            .Select(static row => row.RelId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToList();

    private static List<string> IntersectOrderedSets(IEnumerable<IEnumerable<string>> sets)
    {
        HashSet<string>? intersection = null;
        foreach (var set in sets)
        {
            var current = set.ToHashSet(StringComparer.Ordinal);
            if (intersection is null)
            {
                intersection = current;
                continue;
            }

            intersection.IntersectWith(current);
        }

        return intersection is null
            ? []
            : intersection.OrderBy(static value => value, StringComparer.Ordinal).ToList();
    }

    private static List<GraphShardScopeFrequencyRow> BuildFrequencyRows(
        IEnumerable<IEnumerable<string>> sets,
        int scopeCount)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var set in sets)
        {
            foreach (var key in set.Distinct(StringComparer.Ordinal))
            {
                counts.TryGetValue(key, out var current);
                counts[key] = current + 1;
            }
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new GraphShardScopeFrequencyRow
            {
                Key = pair.Key,
                ScopeCount = pair.Value,
                ScopeShare = scopeCount == 0 ? 0m : decimal.Divide(pair.Value, scopeCount)
            })
            .ToList();
    }

    private static string BuildSelectionSignature(
        IReadOnlyList<string> nodeKeys,
        IReadOnlyList<string> relationshipTypeKeys)
        => FormattableString.Invariant(
            $"nodes[{string.Join(",", nodeKeys)}]|rels[{string.Join(",", relationshipTypeKeys)}]");

    private sealed class StringTupleComparer : IEqualityComparer<(string PreviousSignature, string CurrentSignature)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string PreviousSignature, string CurrentSignature) x, (string PreviousSignature, string CurrentSignature) y)
            => string.Equals(x.PreviousSignature, y.PreviousSignature, StringComparison.Ordinal)
                && string.Equals(x.CurrentSignature, y.CurrentSignature, StringComparison.Ordinal);

        public int GetHashCode((string PreviousSignature, string CurrentSignature) obj)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.PreviousSignature),
                StringComparer.Ordinal.GetHashCode(obj.CurrentSignature));
    }

    private static string GetNodeGroupKey(GraphShardNodeMatch match, string? groupByProperty)
    {
        if (string.IsNullOrWhiteSpace(groupByProperty))
            return "__all__";

        return match.Row.Properties.TryGetValue(groupByProperty, out var value)
            ? NormalizeComparable(value ?? string.Empty)
            : "<missing>";
    }

    private static string GetRelationshipGroupKey(GraphShardRelationshipMatch match, string? groupByProperty)
    {
        if (string.IsNullOrWhiteSpace(groupByProperty))
            return "__all__";

        if (string.Equals(groupByProperty, "relType", StringComparison.Ordinal))
            return match.RelType;

        return match.Row.Properties.TryGetValue(groupByProperty, out var value)
            ? NormalizeComparable(value ?? string.Empty)
            : "<missing>";
    }

    private static object? ComputeNodeAggregate(
        IEnumerable<GraphShardNodeMatch> group,
        GraphShardAggregateSpec aggregate)
    {
        return aggregate.Function switch
        {
            GraphShardAggregateFunction.Count => group.Count(),
            GraphShardAggregateFunction.Sum => Sum(group.Select(match => GetAggregateValue(match.Row.Properties, aggregate.PropertyName))),
            _ => throw new ArgumentOutOfRangeException(nameof(aggregate.Function), aggregate.Function, "Unsupported aggregate function.")
        };
    }

    private static object? ComputeRelationshipAggregate(
        IEnumerable<GraphShardRelationshipMatch> group,
        GraphShardAggregateSpec aggregate)
    {
        return aggregate.Function switch
        {
            GraphShardAggregateFunction.Count => group.Count(),
            GraphShardAggregateFunction.Sum => Sum(group.Select(match => GetAggregateValue(match.Row.Properties, aggregate.PropertyName))),
            _ => throw new ArgumentOutOfRangeException(nameof(aggregate.Function), aggregate.Function, "Unsupported aggregate function.")
        };
    }

    private static object? GetAggregateValue(
        IReadOnlyDictionary<string, object?> properties,
        string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return null;

        return properties.TryGetValue(propertyName, out var value) ? value : null;
    }

    private static decimal Sum(IEnumerable<object?> values)
    {
        decimal total = 0;
        foreach (var value in values)
        {
            if (value is not null && TryConvertToDecimal(value, out var numeric))
                total += numeric;
        }

        return total;
    }

    private static GraphShardPathResult BuildPath(
        string startExternalId,
        string endExternalId,
        IReadOnlyDictionary<string, (string PreviousNodeId, string RelId)> parents)
    {
        var nodeIds = new List<string>();
        var relIds = new List<string>();
        var currentNodeId = endExternalId;
        nodeIds.Add(currentNodeId);

        while (!string.Equals(currentNodeId, startExternalId, StringComparison.Ordinal))
        {
            var parent = parents[currentNodeId];
            relIds.Add(parent.RelId);
            currentNodeId = parent.PreviousNodeId;
            nodeIds.Add(currentNodeId);
        }

        nodeIds.Reverse();
        relIds.Reverse();
        return new GraphShardPathResult
        {
            NodeIds = nodeIds,
            RelIds = relIds
        };
    }

    private IEnumerable<ShardEdgeRef> EnumerateEdges(
        string externalId,
        bool includeOutgoing,
        bool includeIncoming,
        string? relType)
    {
        if (includeOutgoing)
        {
            foreach (var edge in _view.GetOutgoing(externalId, relType))
                yield return edge;
        }

        if (includeIncoming)
        {
            foreach (var edge in _view.GetIncoming(externalId, relType))
                yield return edge;
        }
    }

    private bool HasMatchingEdges(
        string externalId,
        bool includeOutgoing,
        bool includeIncoming,
        string? relType)
    {
        if (includeOutgoing && _view.GetOutgoing(externalId, relType).Count > 0)
            return true;

        return includeIncoming && _view.GetIncoming(externalId, relType).Count > 0;
    }

    private GraphShard BuildProjectedShard(
        IReadOnlySet<string> nodeIds,
        IReadOnlySet<string> relIds,
        string seedExternalId,
        int depth,
        bool includeOutgoing,
        bool includeIncoming,
        string? relType,
        IReadOnlySet<string> boundaryNodeIds)
    {
        var nodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal);
        foreach (var (tableName, table) in _shard.NodeTables)
        {
            var rows = table.Rows
                .Where(row => nodeIds.Contains(row.ExternalId))
                .Select(row => new NodeShardRow
                {
                    ExternalId = row.ExternalId,
                    Properties = row.Properties.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
                })
                .ToList();

            if (rows.Count == 0)
                continue;

            nodeTables[tableName] = new NodeShardTable
            {
                TableName = table.TableName,
                PropertyColumns = [.. table.PropertyColumns],
                Rows = rows
            };
        }

        var relTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal);
        foreach (var (relTableName, table) in _shard.RelTables)
        {
            var rows = table.Rows
                .Where(row => relIds.Contains(row.RelId))
                .Select(row => new RelShardRow
                {
                    RelId = row.RelId,
                    SourceNodeId = row.SourceNodeId,
                    TargetNodeId = row.TargetNodeId,
                    Properties = row.Properties.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
                })
                .ToList();

            if (rows.Count == 0)
                continue;

            relTables[relTableName] = new RelShardTable
            {
                RelType = table.RelType,
                FromTable = table.FromTable,
                ToTable = table.ToTable,
                PropertyColumns = [.. table.PropertyColumns],
                Rows = rows
            };
        }

        var adjacency = new GraphShardAdjacency
        {
            Outgoing = BuildProjectedAdjacencyMap(_shard.Adjacency.Outgoing, nodeIds, relIds),
            Incoming = BuildProjectedAdjacencyMap(_shard.Adjacency.Incoming, nodeIds, relIds)
        };

        var isDepthTruncated = boundaryNodeIds.Count > 0;
        List<GraphShardSeedRecord> requestedSeeds;
        if (string.IsNullOrWhiteSpace(seedExternalId))
        {
            requestedSeeds = [];
        }
        else
        {
            requestedSeeds =
            [
                new GraphShardSeedRecord
                {
                    TableName = GetRequiredNodeTable(seedExternalId),
                    RequestedNodeId = seedExternalId,
                    ExternalId = seedExternalId,
                    Status = "included",
                    Reason = string.Empty
                }
            ];
        }

        return new GraphShard
        {
            FormatVersion = GraphShard.CurrentFormatVersion,
            GraphVersionToken = _shard.GraphVersionToken,
            ExtractorVersion = GraphShard.CurrentExtractorVersion,
            ExtractedAtUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ExtractionPolicy = "runtime_projection",
            IsComplete = _shard.IsComplete && !isDepthTruncated,
            NodeTables = nodeTables,
            RelTables = relTables,
            Adjacency = adjacency,
            SeedProvenance = new GraphShardSeedProvenance
            {
                RequestedCount = requestedSeeds.Count,
                IncludedCount = requestedSeeds.Count,
                ExcludedCount = 0,
                RequestedSeeds = requestedSeeds
            },
            Boundary = new GraphShardBoundary
            {
                IsTruncated = isDepthTruncated,
                HasNodeBoundary = isDepthTruncated,
                HasEdgeBoundary = false,
                TruncatedByNodeLimit = false,
                TruncatedByEdgeLimit = false,
                TruncatedByDepth = isDepthTruncated,
                TruncationReasons = isDepthTruncated ? ["depth"] : [],
                BoundaryNodeIds = [.. boundaryNodeIds],
                FetchHints = new GraphShardBoundaryFetchHints
                {
                    ShouldFetchMore = isDepthTruncated,
                    CanResumeFromBoundary = isDepthTruncated,
                    RecommendedSeedNodeIds = [.. boundaryNodeIds],
                    SuggestedMaxDepth = isDepthTruncated ? depth + 1 : null,
                    SuggestedMaxNodes = null,
                    SuggestedMaxEdges = null,
                    Reasons = isDepthTruncated ? ["depth"] : []
                }
            },
            Options = new GraphShardExtractionOptions
            {
                MaxDepth = depth,
                IncludeOutgoing = includeOutgoing,
                IncludeIncoming = includeIncoming,
                IncludeNodeProperties = true,
                IncludeRelProperties = true,
                IncludeAdjacency = true,
                IncludeBoundaryMetadata = true,
                StopAtBoundary = true,
                RelTypes = string.IsNullOrWhiteSpace(relType) ? [] : [relType]
            },
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runtimeProjection"] = true,
                ["sourceExtractionPolicy"] = _shard.ExtractionPolicy
            }
        };
    }

    private static Dictionary<string, List<ShardEdgeRef>> BuildProjectedAdjacencyMap(
        IReadOnlyDictionary<string, List<ShardEdgeRef>> source,
        IReadOnlySet<string> nodeIds,
        IReadOnlySet<string> relIds)
    {
        var result = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal);
        foreach (var (ownerNodeId, edges) in source)
        {
            if (!nodeIds.Contains(ownerNodeId))
                continue;

            var projectedEdges = edges
                .Where(edge => relIds.Contains(edge.RelId) && nodeIds.Contains(edge.NeighborNodeId))
                .Select(edge => new ShardEdgeRef
                {
                    RelId = edge.RelId,
                    RelType = edge.RelType,
                    NeighborNodeId = edge.NeighborNodeId,
                    Direction = edge.Direction
                })
                .ToList();

            if (projectedEdges.Count > 0)
                result[ownerNodeId] = projectedEdges;
        }

        return result;
    }

    private string GetRequiredNodeTable(string externalId)
    {
        if (_view.TryGetNode(externalId, out var tableName, out _))
            return tableName;

        throw new KeyNotFoundException($"Node '{externalId}' was not found in the local runtime.");
    }

    private GraphShardNodeMatch GetRequiredNodeMatch(string externalId)
    {
        if (_view.TryGetNode(externalId, out var tableName, out var row))
        {
            return new GraphShardNodeMatch
            {
                TableName = tableName,
                Row = row
            };
        }

        throw new KeyNotFoundException($"Node '{externalId}' was not found in the local runtime.");
    }

    private GraphShardRelationshipMatch GetRequiredRelationshipMatch(string relId)
    {
        if (_view.TryGetRelationship(relId, out var relType, out var row))
        {
            return new GraphShardRelationshipMatch
            {
                RelType = relType,
                Row = row
            };
        }

        throw new KeyNotFoundException($"Relationship '{relId}' was not found in the local runtime.");
    }
}
