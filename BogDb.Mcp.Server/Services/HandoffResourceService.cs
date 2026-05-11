using System.Text.Json;

namespace BogDb.Mcp.Server.Services;

public sealed class HandoffResourceService
{
    private static readonly string[] SupportedIndexPaths =
    [
        ".handoffs/index.json",
        ".bo/handoffs/index.json"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _workspaceRoot;

    public HandoffResourceService(string? workspaceRoot = null)
    {
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(workspaceRoot);
    }

    public object ListResources()
    {
        var index = LoadIndex();
        return new
        {
            resources = index.Resources
                .Select(entry => new
                {
                    uri = entry.ResourceUri,
                    name = $"{entry.HandoffKind} handoff",
                    description = $"Handoff artifact from {entry.GeneratedAtUtc}.",
                    mimeType = "application/json"
                })
                .ToArray()
        };
    }

    public object QueryHandoffs(JsonElement arguments)
    {
        var index = LoadIndex();
        var query = HandoffQuery.From(arguments);

        IEnumerable<HandoffIndexEntry> filtered = index.Resources;

        if (!string.IsNullOrWhiteSpace(query.HandoffKind))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.HandoffKind, query.HandoffKind, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.CreatedByAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.CreatedByAgentUid, query.CreatedByAgentUid, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.TargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.TargetAgentUid, query.TargetAgentUid, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.LatestForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.TargetAgentUid, query.LatestForTargetAgentUid, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.ParticipantAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.CreatedByAgentUid, query.ParticipantAgentUid, StringComparison.Ordinal) ||
                string.Equals(entry.TargetAgentUid, query.ParticipantAgentUid, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.Status, query.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.LatestBetweenAgentAUid) &&
            !string.IsNullOrWhiteSpace(query.LatestBetweenAgentBUid))
        {
            filtered = filtered.Where(entry =>
                IsBetweenAgents(entry, query.LatestBetweenAgentAUid, query.LatestBetweenAgentBUid));
        }

        if (!string.IsNullOrWhiteSpace(query.LatestActionableForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.TargetAgentUid, query.LatestActionableForTargetAgentUid, StringComparison.Ordinal) &&
                IsActionable(entry));
        }

        if (!string.IsNullOrWhiteSpace(query.LatestReadyForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.TargetAgentUid, query.LatestReadyForTargetAgentUid, StringComparison.Ordinal) &&
                string.Equals(entry.Status, "ready", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.LatestReadyVerificationForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.HandoffKind, "orchestration_acceptance_verification", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.TargetAgentUid, query.LatestReadyVerificationForTargetAgentUid, StringComparison.Ordinal) &&
                string.Equals(entry.Status, "ready", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.LatestReadyVerificationPickupForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.HandoffKind, "orchestration_verification_pickup", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.TargetAgentUid, query.LatestReadyVerificationPickupForTargetAgentUid, StringComparison.Ordinal) &&
                string.Equals(entry.Status, "ready", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.GroupReadyVerificationHandoffsForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.HandoffKind, "orchestration_acceptance_verification", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.TargetAgentUid, query.GroupReadyVerificationHandoffsForTargetAgentUid, StringComparison.Ordinal) &&
                string.Equals(entry.Status, "ready", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.GroupReadyVerificationPickupHandoffsForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.HandoffKind, "orchestration_verification_pickup", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.TargetAgentUid, query.GroupReadyVerificationPickupHandoffsForTargetAgentUid, StringComparison.Ordinal) &&
                string.Equals(entry.Status, "ready", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.BestReadyVerificationBatchForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.HandoffKind, "orchestration_acceptance_verification", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.TargetAgentUid, query.BestReadyVerificationBatchForTargetAgentUid, StringComparison.Ordinal) &&
                string.Equals(entry.Status, "ready", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.BestReadyVerificationWorkForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.HandoffKind, "orchestration_acceptance_verification", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.TargetAgentUid, query.BestReadyVerificationWorkForTargetAgentUid, StringComparison.Ordinal) &&
                string.Equals(entry.Status, "ready", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.BestReadyVerificationPickupWorkForTargetAgentUid))
        {
            filtered = filtered.Where(entry =>
                string.Equals(entry.HandoffKind, "orchestration_verification_pickup", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.TargetAgentUid, query.BestReadyVerificationPickupWorkForTargetAgentUid, StringComparison.Ordinal) &&
                string.Equals(entry.Status, "ready", StringComparison.OrdinalIgnoreCase));
        }

        var ordered = filtered
            .OrderByDescending(entry => ParseTimestamp(entry.GeneratedAtUtc))
            .ThenByDescending(entry => entry.GeneratedAtUtc, StringComparer.Ordinal)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(query.GroupReadyVerificationHandoffsForTargetAgentUid))
        {
            var groups = GroupVerificationHandoffs(ordered, query.Limit);
            return new
            {
                totalMatches = ordered.Length,
                returnedGroupCount = groups.Length,
                groups
            };
        }

        if (!string.IsNullOrWhiteSpace(query.GroupReadyVerificationPickupHandoffsForTargetAgentUid))
        {
            var groups = GroupVerificationPickupHandoffs(ordered, query.Limit);
            return new
            {
                totalMatches = ordered.Length,
                returnedGroupCount = groups.Length,
                groups
            };
        }

        if (!string.IsNullOrWhiteSpace(query.BestReadyVerificationBatchForTargetAgentUid))
        {
            var groups = GroupVerificationHandoffs(ordered, 1);
            return new
            {
                totalMatches = ordered.Length,
                returnedGroupCount = groups.Length,
                bestGroup = groups.FirstOrDefault()
            };
        }

        if (!string.IsNullOrWhiteSpace(query.BestReadyVerificationWorkForTargetAgentUid))
        {
            var groups = GroupVerificationHandoffs(ordered, 1);
            var bestGroup = groups.FirstOrDefault();
            if (bestGroup is not null)
            {
                using var bestGroupDocument = JsonDocument.Parse(JsonSerializer.Serialize(bestGroup, JsonOptions));
                var bestGroupElement = bestGroupDocument.RootElement.Clone();
                var memberCount = bestGroupElement.GetProperty("memberCount").GetInt32();
                if (memberCount > 1)
                {
                    return new
                    {
                        totalMatches = ordered.Length,
                        workSelectionKind = "batch",
                        bestGroup = bestGroupElement
                    };
                }
            }

            var bestEntry = ordered
                .OrderBy(entry => entry.BlockerCodes?.Count ?? 0)
                .ThenBy(entry => ParseTimestamp(entry.GeneratedAtUtc))
                .ThenBy(entry => entry.GeneratedAtUtc, StringComparer.Ordinal)
                .FirstOrDefault();
            var bestEntryTimestamp = bestEntry == null ? DateTimeOffset.MinValue : ParseTimestamp(bestEntry.GeneratedAtUtc);
            var oldestOrderedTimestamp = ordered
                .Select(entry => ParseTimestamp(entry.GeneratedAtUtc))
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Min();
            var singleAgeHours = bestEntry == null
                ? 0d
                : Math.Max(0d, (bestEntryTimestamp - oldestOrderedTimestamp).TotalHours);
            var singleAgeUrgencyScore = Math.Min(1d, singleAgeHours / 24d);
            var singleBlockerPenaltyScore = bestEntry == null
                ? 0d
                : 1d - Math.Min(1d, (bestEntry.BlockerCodes?.Count ?? 0) / 3d);
            var dominantPickupPressure = bestEntry == null
                ? "none"
                : GetDominantPickupPressure(
                    impactScore: 0.4d,
                    lowCostScore: 1d,
                    relatednessScore: 0.4d,
                    blockerPenaltyScore: singleBlockerPenaltyScore,
                    ageUrgencyScore: singleAgeUrgencyScore);
            var pickupFactorSummary = bestEntry == null
                ? BuildPickupFactorSummary(0d, 0d, 0d, 0d, 0d)
                : BuildPickupFactorSummary(
                    impactScore: 0.4d,
                    lowCostScore: 1d,
                    relatednessScore: 0.4d,
                    blockerPenaltyScore: singleBlockerPenaltyScore,
                    ageUrgencyScore: singleAgeUrgencyScore);
            return new
            {
                totalMatches = ordered.Length,
                workSelectionKind = "single",
                dominantPickupPressure = dominantPickupPressure,
                pickupFactorSummary = pickupFactorSummary,
                bestEntry = bestEntry == null
                    ? null
                    : new
                    {
                        artifactId = bestEntry.ArtifactId,
                        resourceUri = bestEntry.ResourceUri,
                        handoffKind = bestEntry.HandoffKind,
                        generatedAtUtc = bestEntry.GeneratedAtUtc,
                        relativePath = bestEntry.RelativePath,
                        producer = bestEntry.Producer,
                        createdByAgentUid = bestEntry.CreatedByAgentUid,
                        targetAgentUid = bestEntry.TargetAgentUid,
                        status = bestEntry.Status,
                        blockerCodes = bestEntry.BlockerCodes ?? [],
                        blockerCount = bestEntry.BlockerCodes?.Count ?? 0,
                        ageUrgencyScore = Math.Round(singleAgeUrgencyScore, 3),
                        dominantPickupPressure = dominantPickupPressure,
                        pickupFactorSummary = pickupFactorSummary,
                        actionabilityScore = bestEntry.ActionabilityScore
                    }
            };
        }

        if (!string.IsNullOrWhiteSpace(query.BestReadyVerificationPickupWorkForTargetAgentUid))
        {
            var groups = GroupVerificationPickupHandoffs(ordered, 1);
            var bestGroup = groups.FirstOrDefault();
            if (bestGroup is not null)
            {
                using var bestGroupDocument = JsonDocument.Parse(JsonSerializer.Serialize(bestGroup, JsonOptions));
                var bestGroupElement = bestGroupDocument.RootElement.Clone();
                var memberCount = bestGroupElement.GetProperty("memberCount").GetInt32();
                if (memberCount > 1)
                {
                    return new
                    {
                        totalMatches = ordered.Length,
                        workSelectionKind = "batch",
                        bestGroup = bestGroupElement
                    };
                }
            }

            var enriched = ordered
                .Select(entry => new
                {
                    Entry = entry,
                    Context = ReadVerificationPickupContext(entry)
                })
                .ToArray();
            var bestItem = enriched
                .OrderByDescending(item => item.Context.PickupFactors.ImpactScore)
                .ThenByDescending(item => item.Context.PickupFactors.LowCostScore)
                .ThenByDescending(item => item.Context.PickupFactors.RelatednessScore)
                .ThenByDescending(item => item.Context.PickupFactors.BlockerPenaltyScore)
                .ThenByDescending(item => item.Context.PickupFactors.AgeUrgencyScore)
                .ThenBy(item => ParseTimestamp(item.Entry.GeneratedAtUtc))
                .ThenBy(item => item.Entry.GeneratedAtUtc, StringComparer.Ordinal)
                .FirstOrDefault();

            return new
            {
                totalMatches = ordered.Length,
                workSelectionKind = "single",
                dominantPickupPressure = bestItem?.Context.DominantPickupPressure ?? "none",
                pickupFactorSummary = BuildPickupFactorSummary(
                    bestItem?.Context.PickupFactors.ImpactScore ?? 0d,
                    bestItem?.Context.PickupFactors.LowCostScore ?? 0d,
                    bestItem?.Context.PickupFactors.RelatednessScore ?? 0d,
                    bestItem?.Context.PickupFactors.BlockerPenaltyScore ?? 0d,
                    bestItem?.Context.PickupFactors.AgeUrgencyScore ?? 0d),
                bestEntry = bestItem == null
                    ? null
                    : new
                    {
                        artifactId = bestItem.Entry.ArtifactId,
                        resourceUri = bestItem.Entry.ResourceUri,
                        handoffKind = bestItem.Entry.HandoffKind,
                        generatedAtUtc = bestItem.Entry.GeneratedAtUtc,
                        relativePath = bestItem.Entry.RelativePath,
                        producer = bestItem.Entry.Producer,
                        createdByAgentUid = bestItem.Entry.CreatedByAgentUid,
                        targetAgentUid = bestItem.Entry.TargetAgentUid,
                        status = bestItem.Entry.Status,
                        blockerCodes = bestItem.Entry.BlockerCodes ?? [],
                        blockerCount = bestItem.Entry.BlockerCodes?.Count ?? 0,
                        selectionKind = bestItem.Context.SelectionKind,
                        bestGroupKey = bestItem.Context.GroupKey,
                        bestEntryArtifactId = bestItem.Context.EntryArtifactId,
                        dominantPickupPressure = bestItem.Context.DominantPickupPressure,
                        pickupFactorSummary = BuildPickupFactorSummary(
                            bestItem.Context.PickupFactors.ImpactScore,
                            bestItem.Context.PickupFactors.LowCostScore,
                            bestItem.Context.PickupFactors.RelatednessScore,
                            bestItem.Context.PickupFactors.BlockerPenaltyScore,
                            bestItem.Context.PickupFactors.AgeUrgencyScore),
                        actionabilityScore = bestItem.Entry.ActionabilityScore
                    }
            };
        }

        var selected = query.LatestOnly
            ? ordered.Take(1).ToArray()
            : ordered.Take(query.Limit).ToArray();

        return new
        {
            totalMatches = ordered.Length,
            returnedCount = selected.Length,
            entries = selected.Select(entry => new
            {
                artifactId = entry.ArtifactId,
                resourceUri = entry.ResourceUri,
                handoffKind = entry.HandoffKind,
                generatedAtUtc = entry.GeneratedAtUtc,
                relativePath = entry.RelativePath,
                producer = entry.Producer,
                createdByAgentUid = entry.CreatedByAgentUid,
                targetAgentUid = entry.TargetAgentUid,
                status = entry.Status,
                blockerCodes = entry.BlockerCodes ?? [],
                actionabilityScore = entry.ActionabilityScore
            }).ToArray()
        };
    }

    public object ReadResource(JsonElement @params)
    {
        if (!@params.TryGetProperty("uri", out var uriElement))
            throw new InvalidOperationException("resources/read requires uri.");

        var uri = uriElement.GetString();
        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException("resources/read requires uri.");

        var index = LoadIndex();
        var entry = index.Resources.FirstOrDefault(candidate =>
            string.Equals(candidate.ResourceUri, uri, StringComparison.Ordinal));

        if (entry == null)
            throw new InvalidOperationException($"Unknown resource '{uri}'.");

        var fullPath = ResolveWorkspacePath(entry.RelativePath);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"Resource file '{entry.RelativePath}' does not exist.");

        return new
        {
            contents = new object[]
            {
                new
                {
                    uri = entry.ResourceUri,
                    mimeType = "application/json",
                    text = File.ReadAllText(fullPath)
                }
            }
        };
    }

    private HandoffIndex LoadIndex()
    {
        var indexPath = ResolveFirstExistingIndexPath();
        if (indexPath == null)
        {
            return new HandoffIndex
            {
                Resources = []
            };
        }

        var json = File.ReadAllText(indexPath);
        var index = JsonSerializer.Deserialize<HandoffIndex>(json, JsonOptions);
        return index ?? new HandoffIndex { Resources = [] };
    }

    private static DateTimeOffset ParseTimestamp(string value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;

    private static bool IsBetweenAgents(HandoffIndexEntry entry, string agentAUid, string agentBUid)
    {
        return
            (string.Equals(entry.CreatedByAgentUid, agentAUid, StringComparison.Ordinal) &&
             string.Equals(entry.TargetAgentUid, agentBUid, StringComparison.Ordinal)) ||
            (string.Equals(entry.CreatedByAgentUid, agentBUid, StringComparison.Ordinal) &&
             string.Equals(entry.TargetAgentUid, agentAUid, StringComparison.Ordinal));
    }

    private static bool IsActionable(HandoffIndexEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Status))
        {
            return entry.Status.ToLowerInvariant() switch
            {
                "ready" => true,
                "requires_attention" => true,
                "blocked" => false,
                "in_progress" => false,
                "completed" => false,
                "consumed" => false,
                "cancelled" => false,
                _ => entry.ActionabilityScore.GetValueOrDefault(1d) > 0d
            };
        }

        return entry.ActionabilityScore.GetValueOrDefault(1d) > 0d;
    }

    private object[] GroupVerificationHandoffs(HandoffIndexEntry[] ordered, int limit)
    {
        var enriched = ordered
            .Select(entry => new
            {
                Entry = entry,
                Context = ReadVerificationContext(entry)
            })
            .ToArray();

        return enriched
            .GroupBy(
                item => BuildVerificationGroupKey(item.Context),
                StringComparer.Ordinal)
            .Select(group =>
            {
                var items = group
                    .OrderByDescending(item => ParseTimestamp(item.Entry.GeneratedAtUtc))
                    .ThenByDescending(item => item.Entry.GeneratedAtUtc, StringComparer.Ordinal)
                    .ToArray();
                var first = items[0];
                var newestTimestamp = items
                    .Select(item => ParseTimestamp(item.Entry.GeneratedAtUtc))
                    .DefaultIfEmpty(DateTimeOffset.MinValue)
                    .Max();
                var oldestTimestamp = items
                    .Select(item => ParseTimestamp(item.Entry.GeneratedAtUtc))
                    .DefaultIfEmpty(DateTimeOffset.MinValue)
                    .Min();
                var ageHours = Math.Max(0d, (newestTimestamp - oldestTimestamp).TotalHours);
                var ageUrgencyScore = Math.Min(1d, ageHours / 24d);
                var averageActionability = items
                    .Select(item => item.Entry.ActionabilityScore.GetValueOrDefault(1d))
                    .DefaultIfEmpty(1d)
                    .Average();
                var averageBlockerCount = items
                    .Select(item => (double)(item.Entry.BlockerCodes?.Count ?? 0))
                    .DefaultIfEmpty(0d)
                    .Average();
                var blockerPressureScore = Math.Min(1d, averageBlockerCount / 3d);
                var blockerPenaltyScore = 1d - blockerPressureScore;
                var lowCostScore = Math.Min(1d, 0.35d + (items.Length * 0.2d));
                var impactScore = Math.Min(1d, 0.4d + (items.Length * 0.15d));
                var relatednessScore = items.All(item => string.Equals(item.Context.TargetKind, first.Context.TargetKind, StringComparison.OrdinalIgnoreCase))
                    ? 0.9d
                    : 0.6d;
                var dominantPickupPressure = GetDominantPickupPressure(
                    impactScore,
                    lowCostScore,
                    relatednessScore,
                    blockerPenaltyScore,
                    ageUrgencyScore);
                var pickupFactorSummary = BuildPickupFactorSummary(
                    impactScore,
                    lowCostScore,
                    relatednessScore,
                    blockerPenaltyScore,
                    ageUrgencyScore);

                return new
                {
                    groupKey = group.Key,
                    handoffKind = "orchestration_acceptance_verification",
                    targetAgentUid = first.Entry.TargetAgentUid,
                    targetKind = first.Context.TargetKind,
                    targetFamily = first.Context.TargetFamily,
                    memberCount = items.Length,
                    averageActionabilityScore = Math.Round(averageActionability, 3),
                    lowCostScore = Math.Round(lowCostScore, 3),
                    impactScore = Math.Round(impactScore, 3),
                    relatednessScore = Math.Round(relatednessScore, 3),
                    averageBlockerCount = Math.Round(averageBlockerCount, 3),
                    blockerPressureScore = Math.Round(blockerPressureScore, 3),
                    blockerPenaltyScore = Math.Round(blockerPenaltyScore, 3),
                    oldestGeneratedAtUtc = oldestTimestamp == DateTimeOffset.MinValue ? null : oldestTimestamp.ToString("O"),
                    newestGeneratedAtUtc = newestTimestamp == DateTimeOffset.MinValue ? null : newestTimestamp.ToString("O"),
                    ageHours = Math.Round(ageHours, 3),
                    ageUrgencyScore = Math.Round(ageUrgencyScore, 3),
                    dominantPickupPressure = dominantPickupPressure,
                    pickupFactorSummary = pickupFactorSummary,
                    recommendedBatchAction = "ingest_acceptance_verification_batch",
                    entries = items.Select(item => new
                    {
                        artifactId = item.Entry.ArtifactId,
                        resourceUri = item.Entry.ResourceUri,
                        generatedAtUtc = item.Entry.GeneratedAtUtc,
                        relativePath = item.Entry.RelativePath,
                        acceptanceId = item.Context.AcceptanceId,
                        targetId = item.Context.TargetId,
                        targetKind = item.Context.TargetKind,
                        status = item.Entry.Status,
                        actionabilityScore = item.Entry.ActionabilityScore,
                        blockerCodes = item.Entry.BlockerCodes ?? []
                    }).ToArray()
                };
            })
            .OrderByDescending(group => group.impactScore)
            .ThenByDescending(group => group.lowCostScore)
            .ThenByDescending(group => group.relatednessScore)
            .ThenByDescending(group => group.blockerPenaltyScore)
            .ThenByDescending(group => group.ageUrgencyScore)
            .ThenByDescending(group => group.memberCount)
            .Take(limit)
            .Cast<object>()
            .ToArray();
    }

    private object[] GroupVerificationPickupHandoffs(HandoffIndexEntry[] ordered, int limit)
    {
        var enriched = ordered
            .Select(entry => new
            {
                Entry = entry,
                Context = ReadVerificationPickupContext(entry)
            })
            .ToArray();

        return enriched
            .GroupBy(item => item.Context.GroupKey ?? "unknown", StringComparer.Ordinal)
            .Select(group =>
            {
                var items = group
                    .OrderByDescending(item => ParseTimestamp(item.Entry.GeneratedAtUtc))
                    .ThenByDescending(item => item.Entry.GeneratedAtUtc, StringComparer.Ordinal)
                    .ToArray();
                var first = items[0];
                var impactScore = items.Average(item => item.Context.PickupFactors.ImpactScore);
                var lowCostScore = items.Average(item => item.Context.PickupFactors.LowCostScore);
                var relatednessScore = items.Average(item => item.Context.PickupFactors.RelatednessScore);
                var blockerPenaltyScore = items.Average(item => item.Context.PickupFactors.BlockerPenaltyScore);
                var ageUrgencyScore = items.Average(item => item.Context.PickupFactors.AgeUrgencyScore);
                var dominantPickupPressure = GetDominantPickupPressure(
                    impactScore,
                    lowCostScore,
                    relatednessScore,
                    blockerPenaltyScore,
                    ageUrgencyScore);
                var roundedImpactScore = Math.Round(impactScore, 3);
                var roundedLowCostScore = Math.Round(lowCostScore, 3);
                var roundedRelatednessScore = Math.Round(relatednessScore, 3);
                var roundedBlockerPenaltyScore = Math.Round(blockerPenaltyScore, 3);
                var roundedAgeUrgencyScore = Math.Round(ageUrgencyScore, 3);

                return new
                {
                    groupKey = group.Key,
                    handoffKind = "orchestration_verification_pickup",
                    targetAgentUid = first.Entry.TargetAgentUid,
                    selectionKind = first.Context.SelectionKind,
                    memberCount = items.Length,
                    impactScore = roundedImpactScore,
                    lowCostScore = roundedLowCostScore,
                    relatednessScore = roundedRelatednessScore,
                    blockerPenaltyScore = roundedBlockerPenaltyScore,
                    ageUrgencyScore = roundedAgeUrgencyScore,
                    dominantPickupPressure = dominantPickupPressure,
                    pickupFactorSummary = BuildPickupFactorSummary(
                        roundedImpactScore,
                        roundedLowCostScore,
                        roundedRelatednessScore,
                        roundedBlockerPenaltyScore,
                        roundedAgeUrgencyScore),
                    recommendedBatchAction = "process_verification_pickup_group",
                    entries = items.Select(item => new
                    {
                        artifactId = item.Entry.ArtifactId,
                        resourceUri = item.Entry.ResourceUri,
                        generatedAtUtc = item.Entry.GeneratedAtUtc,
                        relativePath = item.Entry.RelativePath,
                        selectionKind = item.Context.SelectionKind,
                        bestGroupKey = item.Context.GroupKey,
                        bestEntryArtifactId = item.Context.EntryArtifactId,
                        status = item.Entry.Status,
                        actionabilityScore = item.Entry.ActionabilityScore,
                        blockerCodes = item.Entry.BlockerCodes ?? [],
                        dominantPickupPressure = item.Context.DominantPickupPressure,
                        pickupFactorSummary = BuildPickupFactorSummary(
                            item.Context.PickupFactors.ImpactScore,
                            item.Context.PickupFactors.LowCostScore,
                            item.Context.PickupFactors.RelatednessScore,
                            item.Context.PickupFactors.BlockerPenaltyScore,
                            item.Context.PickupFactors.AgeUrgencyScore)
                    }).ToArray()
                };
            })
            .OrderByDescending(group => group.impactScore)
            .ThenByDescending(group => group.lowCostScore)
            .ThenByDescending(group => group.relatednessScore)
            .ThenByDescending(group => group.blockerPenaltyScore)
            .ThenByDescending(group => group.ageUrgencyScore)
            .ThenByDescending(group => group.memberCount)
            .Take(limit)
            .Cast<object>()
            .ToArray();
    }

    private VerificationHandoffContext ReadVerificationContext(HandoffIndexEntry entry)
    {
        var fullPath = ResolveWorkspacePath(entry.RelativePath);
        if (!File.Exists(fullPath))
        {
            return new VerificationHandoffContext(null, null, "unknown", "unknown");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("handoff", out var handoff))
        {
            return new VerificationHandoffContext(null, null, "unknown", "unknown");
        }

        var acceptanceId = TryReadString(handoff, "acceptance_id");
        var targetId = TryReadString(handoff, "target_id");
        var targetKind = TryReadString(handoff, "target_kind") ?? "unknown";
        var targetFamily = ExtractTargetFamily(targetId);
        return new VerificationHandoffContext(acceptanceId, targetId, targetKind, targetFamily);
    }

    private VerificationPickupHandoffContext ReadVerificationPickupContext(HandoffIndexEntry entry)
    {
        var fullPath = ResolveWorkspacePath(entry.RelativePath);
        if (!File.Exists(fullPath))
        {
            return new VerificationPickupHandoffContext(
                "unknown",
                "unknown",
                null,
                "none",
                new PickupFactorValues(0d, 0d, 0d, 0d, 0d));
        }

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("handoff", out var handoff))
        {
            return new VerificationPickupHandoffContext(
                "unknown",
                "unknown",
                null,
                "none",
                new PickupFactorValues(0d, 0d, 0d, 0d, 0d));
        }

        var selectionKind = TryReadString(handoff, "selection_kind") ?? "unknown";
        var dominantPickupPressure = TryReadString(handoff, "dominant_pickup_pressure") ?? "none";
        string? groupKey = null;
        string? entryArtifactId = null;

        if (handoff.TryGetProperty("selection_reference", out var selectionReference))
        {
            groupKey = TryReadString(selectionReference, "best_group_key");
            entryArtifactId = TryReadString(selectionReference, "best_entry_artifact_id");
        }

        var groupFamily = !string.IsNullOrWhiteSpace(groupKey)
            ? groupKey
            : ExtractTargetFamily(entryArtifactId);

        PickupFactorValues pickupFactors = new(0d, 0d, 0d, 0d, 0d);
        if (handoff.TryGetProperty("pickup_factor_summary", out var pickupFactorSummary) &&
            pickupFactorSummary.ValueKind == JsonValueKind.Object)
        {
            pickupFactors = new PickupFactorValues(
                TryReadDouble(pickupFactorSummary, "impact_score", "impactScore"),
                TryReadDouble(pickupFactorSummary, "low_cost_score", "lowCostScore"),
                TryReadDouble(pickupFactorSummary, "relatedness_score", "relatednessScore"),
                TryReadDouble(pickupFactorSummary, "blocker_penalty_score", "blockerPenaltyScore"),
                TryReadDouble(pickupFactorSummary, "age_urgency_score", "ageUrgencyScore"));
        }

        return new VerificationPickupHandoffContext(
            selectionKind,
            groupFamily,
            entryArtifactId,
            dominantPickupPressure,
            pickupFactors);
    }

    private static string BuildVerificationGroupKey(VerificationHandoffContext context)
        => $"{context.TargetKind}:{context.TargetFamily}";

    private static string ExtractTargetFamily(string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            return "unknown";

        var parts = targetId.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return parts[1];
        return parts[0];
    }

    private static string? TryReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double TryReadDouble(JsonElement element, string snakeCaseName, string camelCaseName)
    {
        if (element.TryGetProperty(snakeCaseName, out var snakeCaseProperty) &&
            snakeCaseProperty.ValueKind == JsonValueKind.Number &&
            snakeCaseProperty.TryGetDouble(out var snakeCaseParsed))
        {
            return snakeCaseParsed;
        }

        if (element.TryGetProperty(camelCaseName, out var camelCaseProperty) &&
            camelCaseProperty.ValueKind == JsonValueKind.Number &&
            camelCaseProperty.TryGetDouble(out var camelCaseParsed))
        {
            return camelCaseParsed;
        }

        return 0d;
    }

    private static string GetDominantPickupPressure(
        double impactScore,
        double lowCostScore,
        double relatednessScore,
        double blockerPenaltyScore,
        double ageUrgencyScore)
    {
        return new[]
            {
                ("mostly impact", impactScore),
                ("mostly low cost", lowCostScore),
                ("mostly relatedness", relatednessScore),
                ("mostly low blocker pressure", blockerPenaltyScore),
                ("mostly age urgency", ageUrgencyScore)
            }
            .OrderByDescending(item => item.Item2)
            .ThenBy(item => item.Item1, StringComparer.Ordinal)
            .First().Item1;
    }

    private static object BuildPickupFactorSummary(
        double impactScore,
        double lowCostScore,
        double relatednessScore,
        double blockerPenaltyScore,
        double ageUrgencyScore)
    {
        return new
        {
            impactScore = Math.Round(impactScore, 3),
            lowCostScore = Math.Round(lowCostScore, 3),
            relatednessScore = Math.Round(relatednessScore, 3),
            blockerPenaltyScore = Math.Round(blockerPenaltyScore, 3),
            ageUrgencyScore = Math.Round(ageUrgencyScore, 3)
        };
    }

    private string? ResolveFirstExistingIndexPath()
    {
        foreach (var candidate in SupportedIndexPaths)
        {
            var fullPath = ResolveWorkspacePath(candidate);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    private string ResolveWorkspacePath(string relativePath)
    {
        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(_workspaceRoot, normalizedRelativePath));
        if (!fullPath.StartsWith(_workspaceRoot, StringComparison.Ordinal))
            throw new InvalidOperationException($"Path '{relativePath}' escapes the workspace root.");

        return fullPath;
    }
    private sealed class HandoffQuery
    {
        public string? HandoffKind { get; private set; }

        public string? CreatedByAgentUid { get; private set; }

        public string? TargetAgentUid { get; private set; }

        public string? ParticipantAgentUid { get; private set; }

        public string? LatestForTargetAgentUid { get; private set; }

        public string? LatestBetweenAgentAUid { get; private set; }

        public string? LatestBetweenAgentBUid { get; private set; }

        public string? LatestActionableForTargetAgentUid { get; private set; }

        public string? LatestReadyForTargetAgentUid { get; private set; }

        public string? LatestReadyVerificationForTargetAgentUid { get; private set; }

        public string? LatestReadyVerificationPickupForTargetAgentUid { get; private set; }

        public string? GroupReadyVerificationHandoffsForTargetAgentUid { get; private set; }

        public string? GroupReadyVerificationPickupHandoffsForTargetAgentUid { get; private set; }

        public string? BestReadyVerificationBatchForTargetAgentUid { get; private set; }

        public string? BestReadyVerificationWorkForTargetAgentUid { get; private set; }

        public string? BestReadyVerificationPickupWorkForTargetAgentUid { get; private set; }

        public string? Status { get; private set; }

        public bool LatestOnly { get; private set; }

        public int Limit { get; private set; } = 20;

        public static HandoffQuery From(JsonElement arguments)
        {
            var query = new HandoffQuery();

            if (arguments.ValueKind == JsonValueKind.Undefined || arguments.ValueKind == JsonValueKind.Null)
                return query;

            query.HandoffKind = ReadString(arguments, "handoffKind");
            query.CreatedByAgentUid = ReadString(arguments, "createdByAgentUid");
            query.TargetAgentUid = ReadString(arguments, "targetAgentUid");
            query.ParticipantAgentUid = ReadString(arguments, "participantAgentUid");
            query.LatestForTargetAgentUid = ReadString(arguments, "latestForTargetAgentUid");
            query.LatestBetweenAgentAUid = ReadString(arguments, "latestBetweenAgentAUid");
            query.LatestBetweenAgentBUid = ReadString(arguments, "latestBetweenAgentBUid");
            query.LatestActionableForTargetAgentUid = ReadString(arguments, "latestActionableForTargetAgentUid");
            query.LatestReadyForTargetAgentUid = ReadString(arguments, "latestReadyForTargetAgentUid");
            query.LatestReadyVerificationForTargetAgentUid = ReadString(arguments, "latestReadyVerificationForTargetAgentUid");
            query.LatestReadyVerificationPickupForTargetAgentUid = ReadString(arguments, "latestReadyVerificationPickupForTargetAgentUid");
            query.GroupReadyVerificationHandoffsForTargetAgentUid = ReadString(arguments, "groupReadyVerificationHandoffsForTargetAgentUid");
            query.GroupReadyVerificationPickupHandoffsForTargetAgentUid = ReadString(arguments, "groupReadyVerificationPickupHandoffsForTargetAgentUid");
            query.BestReadyVerificationBatchForTargetAgentUid = ReadString(arguments, "bestReadyVerificationBatchForTargetAgentUid");
            query.BestReadyVerificationWorkForTargetAgentUid = ReadString(arguments, "bestReadyVerificationWorkForTargetAgentUid");
            query.BestReadyVerificationPickupWorkForTargetAgentUid = ReadString(arguments, "bestReadyVerificationPickupWorkForTargetAgentUid");
            query.Status = ReadString(arguments, "status");

            if (arguments.TryGetProperty("latestOnly", out var latestOnlyElement) &&
                latestOnlyElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                query.LatestOnly = latestOnlyElement.GetBoolean();
            }

            if (!string.IsNullOrWhiteSpace(query.LatestForTargetAgentUid) ||
                !string.IsNullOrWhiteSpace(query.LatestReadyForTargetAgentUid) ||
                !string.IsNullOrWhiteSpace(query.LatestReadyVerificationForTargetAgentUid) ||
                !string.IsNullOrWhiteSpace(query.LatestReadyVerificationPickupForTargetAgentUid) ||
                !string.IsNullOrWhiteSpace(query.GroupReadyVerificationPickupHandoffsForTargetAgentUid) ||
                !string.IsNullOrWhiteSpace(query.BestReadyVerificationBatchForTargetAgentUid) ||
                !string.IsNullOrWhiteSpace(query.BestReadyVerificationWorkForTargetAgentUid) ||
                !string.IsNullOrWhiteSpace(query.BestReadyVerificationPickupWorkForTargetAgentUid) ||
                !string.IsNullOrWhiteSpace(query.LatestActionableForTargetAgentUid) ||
                (!string.IsNullOrWhiteSpace(query.LatestBetweenAgentAUid) &&
                 !string.IsNullOrWhiteSpace(query.LatestBetweenAgentBUid)))
            {
                query.LatestOnly = true;
            }

            if (arguments.TryGetProperty("limit", out var limitElement) &&
                limitElement.ValueKind == JsonValueKind.Number &&
                limitElement.TryGetInt32(out var limit))
            {
                query.Limit = Math.Clamp(limit, 1, 100);
            }

            return query;
        }

        private static string? ReadString(JsonElement arguments, string propertyName)
        {
            return arguments.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
    }

    private sealed record VerificationHandoffContext(
        string? AcceptanceId,
        string? TargetId,
        string TargetKind,
        string TargetFamily);

    private sealed record VerificationPickupHandoffContext(
        string SelectionKind,
        string GroupKey,
        string? EntryArtifactId,
        string DominantPickupPressure,
        PickupFactorValues PickupFactors);

    private sealed record PickupFactorValues(
        double ImpactScore,
        double LowCostScore,
        double RelatednessScore,
        double BlockerPenaltyScore,
        double AgeUrgencyScore);
}
