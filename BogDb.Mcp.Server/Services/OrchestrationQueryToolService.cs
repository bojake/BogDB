using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BogDb.Mcp.Server.Services;

public sealed class OrchestrationQueryToolService
{
    private const int DefaultRowLimit = 100;
    private const int DefaultTimeoutMs = 10_000;

    private readonly BogDbQueryToolService _queryService;
    private readonly string _workspaceRoot;

    public OrchestrationQueryToolService(BogDbQueryToolService queryService, string? workspaceRoot = null)
    {
        _queryService = queryService;
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(workspaceRoot);
    }

    public Task<QueryToolResult> QueryPendingGatesAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var flowId = JsonArgumentReader.GetOptionalString(arguments, "flowId");
        var repoId = JsonArgumentReader.GetOptionalString(arguments, "repoId");
        var stageId = JsonArgumentReader.GetOptionalString(arguments, "stageId");
        var rowLimit = JsonArgumentReader.GetOptionalInt32(arguments, "rowLimit") ?? DefaultRowLimit;
        var timeoutMs = JsonArgumentReader.GetOptionalInt32(arguments, "timeoutMs") ?? DefaultTimeoutMs;

        const string cypher = """
            MATCH (flow:OrchestrationFlow)-[:HAS_STAGE]->(stage:OrchestrationStage)-[:HAS_GATE]->(gate:OrchestrationGate)
            WHERE NOT EXISTS {
                MATCH (acceptEdge:AcceptanceRecord)-[:ACCEPTS]->(gate)
                WHERE acceptEdge.acceptance_status IN ['accepted', 'released']
            }
              AND NOT EXISTS {
                MATCH (acceptProp:AcceptanceRecord)
                WHERE acceptProp.target_kind = 'gate'
                  AND acceptProp.target_id = gate.gate_id
                  AND acceptProp.acceptance_status IN ['accepted', 'released']
              }
            RETURN
              flow.flow_id AS flow_id,
              flow.target_repo_id AS target_repo_id,
              stage.stage_id AS stage_id,
              gate.gate_id AS gate_id,
              gate.title AS gate_title,
              gate.gate_kind AS gate_kind
            ORDER BY gate_id
            """;

        return ExecuteAndFilterAsync(
            databasePath,
            cypher,
            rowLimit,
            timeoutMs,
            cancellationToken,
            row => MatchesString(row, "flow_id", flowId)
                && MatchesString(row, "target_repo_id", repoId)
                && MatchesString(row, "stage_id", stageId));
    }

    public Task<QueryToolResult> QueryReleaseReadyGatesAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var flowId = JsonArgumentReader.GetOptionalString(arguments, "flowId");
        var repoId = JsonArgumentReader.GetOptionalString(arguments, "repoId");
        var ownerRole = JsonArgumentReader.GetOptionalString(arguments, "ownerRole");
        var rowLimit = JsonArgumentReader.GetOptionalInt32(arguments, "rowLimit") ?? DefaultRowLimit;
        var timeoutMs = JsonArgumentReader.GetOptionalInt32(arguments, "timeoutMs") ?? DefaultTimeoutMs;

        const string cypher = """
            MATCH (flow:OrchestrationFlow)-[:HAS_STAGE]->(stage:OrchestrationStage)-[:HAS_GATE]->(gate:OrchestrationGate)
            WHERE EXISTS { MATCH (:OrchestrationLane)-[:FLOWS_TO]->(gate) }
              AND NOT EXISTS {
                MATCH (lane:OrchestrationLane)-[:FLOWS_TO]->(gate)
                WHERE NOT EXISTS {
                  MATCH (acceptEdge:AcceptanceRecord)-[:ACCEPTS]->(lane)
                  WHERE acceptEdge.acceptance_status IN ['accepted', 'released']
                }
                  AND NOT EXISTS {
                    MATCH (acceptProp:AcceptanceRecord)
                    WHERE acceptProp.target_kind = 'lane'
                      AND acceptProp.target_id = lane.lane_id
                      AND acceptProp.acceptance_status IN ['accepted', 'released']
                  }
              }
            RETURN
              flow.flow_id AS flow_id,
              flow.target_repo_id AS target_repo_id,
              stage.stage_id AS stage_id,
              gate.gate_id AS gate_id,
              gate.title AS gate_title,
              gate.gate_kind AS gate_kind,
              gate.owner_role AS owner_role
            ORDER BY gate_id
            """;

        return ExecuteAndFilterAsync(
            databasePath,
            cypher,
            rowLimit,
            timeoutMs,
            cancellationToken,
            row => MatchesString(row, "flow_id", flowId)
                && MatchesString(row, "target_repo_id", repoId)
                && MatchesString(row, "owner_role", ownerRole));
    }

    public Task<QueryToolResult> QueryBlockedWorkAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var flowId = JsonArgumentReader.GetOptionalString(arguments, "flowId");
        var repoId = JsonArgumentReader.GetOptionalString(arguments, "repoId");
        var targetAgentUid = JsonArgumentReader.GetOptionalString(arguments, "targetAgentUid");
        var rowLimit = JsonArgumentReader.GetOptionalInt32(arguments, "rowLimit") ?? DefaultRowLimit;
        var timeoutMs = JsonArgumentReader.GetOptionalInt32(arguments, "timeoutMs") ?? DefaultTimeoutMs;

        const string cypher = """
            MATCH (flow:OrchestrationFlow)-[:HAS_STAGE]->(stage:OrchestrationStage)-[:HAS_LANE]->(lane:OrchestrationLane)
            MATCH (work:WorkItem)-[:IMPLEMENTS_FLOW_UNIT]->(lane)
            WHERE work.status = 'blocked'
              AND work.blocker_code IN [
                'upstream_gate_pending',
                'upstream_stage_unreleased',
                'acceptance_pending'
              ]
            RETURN
              flow.flow_id AS flow_id,
              flow.target_repo_id AS target_repo_id,
              stage.stage_id AS stage_id,
              work.work_item_id AS work_item_id,
              work.title AS title,
              work.blocker_code AS blocker_code,
              work.summary AS summary,
              work.target_agent_uid AS target_agent_uid
            ORDER BY work_item_id
            """;

        return ExecuteAndFilterAsync(
            databasePath,
            cypher,
            rowLimit,
            timeoutMs,
            cancellationToken,
            row => MatchesString(row, "flow_id", flowId)
                && MatchesString(row, "target_repo_id", repoId)
                && MatchesString(row, "target_agent_uid", targetAgentUid));
    }

    public Task<QueryToolResult> QueryLaneAcceptanceGapsAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var flowId = JsonArgumentReader.GetOptionalString(arguments, "flowId");
        var repoId = JsonArgumentReader.GetOptionalString(arguments, "repoId");
        var stageId = JsonArgumentReader.GetOptionalString(arguments, "stageId");
        var rowLimit = JsonArgumentReader.GetOptionalInt32(arguments, "rowLimit") ?? DefaultRowLimit;
        var timeoutMs = JsonArgumentReader.GetOptionalInt32(arguments, "timeoutMs") ?? DefaultTimeoutMs;

        const string cypher = """
            MATCH (flow:OrchestrationFlow)-[:HAS_STAGE]->(stage:OrchestrationStage)-[:HAS_LANE]->(lane:OrchestrationLane)
            OPTIONAL MATCH (work:WorkItem)-[:IMPLEMENTS_FLOW_UNIT]->(lane)
            OPTIONAL MATCH (acceptEdge:AcceptanceRecord)-[:ACCEPTS]->(lane)
            RETURN
              flow.flow_id AS flow_id,
              flow.target_repo_id AS target_repo_id,
              stage.stage_id AS stage_id,
              lane.lane_id AS lane_id,
              lane.title AS lane_title,
              work.work_item_id AS work_item_id,
              work.status AS work_status,
              acceptEdge.acceptance_status AS edge_acceptance_status
            ORDER BY lane_id, work_item_id
            """;

        return ExecuteLaneAcceptanceGapQueryAsync(
            databasePath,
            cypher,
            rowLimit,
            timeoutMs,
            cancellationToken,
            row => MatchesString(row, "flow_id", flowId)
                && MatchesString(row, "target_repo_id", repoId)
                && MatchesString(row, "stage_id", stageId));
    }

    public Task<QueryToolResult> QueryAcceptanceIngestStatusAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var acceptanceId = JsonArgumentReader.GetOptionalString(arguments, "acceptanceId");
        var targetId = JsonArgumentReader.GetOptionalString(arguments, "targetId");
        var targetKind = JsonArgumentReader.GetOptionalString(arguments, "targetKind");
        var rowLimit = JsonArgumentReader.GetOptionalInt32(arguments, "rowLimit") ?? DefaultRowLimit;
        var timeoutMs = JsonArgumentReader.GetOptionalInt32(arguments, "timeoutMs") ?? DefaultTimeoutMs;

        const string cypher = """
            MATCH (ingest:AcceptanceIngestRecord)
            RETURN
              ingest.acceptance_id AS acceptance_id,
              ingest.target_id AS target_id,
              ingest.target_kind AS target_kind,
              ingest.acceptance_status AS acceptance_status,
              ingest.ingested_at_utc AS ingested_at_utc,
              ingest.source AS source,
              ingest.artifact_path AS artifact_path,
              ingest.index_path AS index_path
            ORDER BY ingested_at_utc DESC, acceptance_id
            """;

        return ExecuteAndFilterAsync(
            databasePath,
            cypher,
            rowLimit,
            timeoutMs,
            cancellationToken,
            row => MatchesString(row, "acceptance_id", acceptanceId)
                && MatchesString(row, "target_id", targetId)
                && MatchesString(row, "target_kind", targetKind));
    }

    public Task<QueryToolResult> QueryAcceptanceVerificationStatusAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var acceptanceId = JsonArgumentReader.GetOptionalString(arguments, "acceptanceId");
        var targetId = JsonArgumentReader.GetOptionalString(arguments, "targetId");
        var targetKind = JsonArgumentReader.GetOptionalString(arguments, "targetKind");
        var rowLimit = JsonArgumentReader.GetOptionalInt32(arguments, "rowLimit") ?? DefaultRowLimit;
        var timeoutMs = JsonArgumentReader.GetOptionalInt32(arguments, "timeoutMs") ?? DefaultTimeoutMs;

        const string cypher = """
            MATCH (verification:AcceptanceVerificationRecord)
            RETURN
              verification.acceptance_id AS acceptance_id,
              verification.target_id AS target_id,
              verification.target_kind AS target_kind,
              verification.ingest_status AS ingest_status,
              verification.observed_at_utc AS observed_at_utc,
              verification.ingested_at_utc AS ingested_at_utc,
              verification.observed_by_agent_uid AS observed_by_agent_uid,
              verification.source AS source,
              verification.artifact_path AS artifact_path,
              verification.index_path AS index_path,
              verification.notes AS notes
            ORDER BY observed_at_utc DESC, acceptance_id
            """;

        return ExecuteAndFilterAsync(
            databasePath,
            cypher,
            rowLimit,
            timeoutMs,
            cancellationToken,
            row => MatchesString(row, "acceptance_id", acceptanceId)
                && MatchesString(row, "target_id", targetId)
                && MatchesString(row, "target_kind", targetKind));
    }

    public async Task<QueryToolResult> QueryAcceptanceIngestsAwaitingLocalVerificationAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var acceptanceId = JsonArgumentReader.GetOptionalString(arguments, "acceptanceId");
        var targetId = JsonArgumentReader.GetOptionalString(arguments, "targetId");
        var targetKind = JsonArgumentReader.GetOptionalString(arguments, "targetKind");
        var rowLimit = JsonArgumentReader.GetOptionalInt32(arguments, "rowLimit") ?? DefaultRowLimit;
        var timeoutMs = JsonArgumentReader.GetOptionalInt32(arguments, "timeoutMs") ?? DefaultTimeoutMs;

        const string cypher = """
            MATCH (ingest:AcceptanceIngestRecord)
            RETURN
              ingest.acceptance_id AS acceptance_id,
              ingest.target_id AS target_id,
              ingest.target_kind AS target_kind,
              ingest.acceptance_status AS acceptance_status,
              ingest.ingested_at_utc AS ingested_at_utc,
              ingest.source AS source,
              ingest.artifact_path AS artifact_path,
              ingest.index_path AS index_path
            ORDER BY ingested_at_utc DESC, acceptance_id
            """;

        var result = await _queryService.ExecuteAsync(
            databasePath,
            cypher,
            parameters: null,
            rowLimit: Math.Max(rowLimit * 10, rowLimit),
            timeoutMs: timeoutMs,
            cancellationToken: cancellationToken);

        if (!result.Success)
            return result;

        var locallyVerifiedAcceptanceIds = await LoadVerificationAcceptanceIdsAsync(databasePath, timeoutMs, cancellationToken);
        var filteredRows = result.Rows
            .Where(row => !locallyVerifiedAcceptanceIds.Contains(ReadString(row, "acceptance_id") ?? string.Empty))
            .Where(row => MatchesString(row, "acceptance_id", acceptanceId)
                && MatchesString(row, "target_id", targetId)
                && MatchesString(row, "target_kind", targetKind))
            .Take(rowLimit)
            .ToArray();

        return new QueryToolResult(
            Success: true,
            Columns: result.Columns,
            Rows: filteredRows,
            RowCount: filteredRows.Length,
            Truncated: result.Truncated || result.Rows.Count > filteredRows.Length,
            ElapsedMs: result.ElapsedMs,
            Error: null);
    }

    private async Task<HashSet<string>> LoadVerificationAcceptanceIdsAsync(
        string databasePath,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        const string verificationCypher = """
            MATCH (verification:AcceptanceVerificationRecord)
            RETURN verification.acceptance_id AS acceptance_id
            """;

        var durableIds = new HashSet<string>(StringComparer.Ordinal);
        var verificationResult = await _queryService.ExecuteAsync(
            databasePath,
            verificationCypher,
            parameters: null,
            rowLimit: DefaultRowLimit * 10,
            timeoutMs: timeoutMs,
            cancellationToken: cancellationToken);

        if (verificationResult.Success)
        {
            foreach (var acceptanceId in verificationResult.Rows
                         .Select(row => ReadString(row, "acceptance_id"))
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                durableIds.Add(acceptanceId!);
            }
        }

        foreach (var acceptanceId in LoadLocalVerificationAcceptanceIds())
        {
            durableIds.Add(acceptanceId);
        }

        return durableIds;
    }

    private async Task<QueryToolResult> ExecuteLaneAcceptanceGapQueryAsync(
        string databasePath,
        string cypher,
        int rowLimit,
        int timeoutMs,
        CancellationToken cancellationToken,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        var result = await _queryService.ExecuteAsync(
            databasePath,
            cypher,
            parameters: null,
            rowLimit: Math.Max(rowLimit * 10, rowLimit),
            timeoutMs: timeoutMs,
            cancellationToken: cancellationToken);

        if (!result.Success)
            return result;

        const string propertyAcceptanceCypher = """
            MATCH (acceptProp:AcceptanceRecord)
            WHERE acceptProp.target_kind = 'lane'
              AND acceptProp.acceptance_status IN ['accepted', 'released']
            RETURN
              acceptProp.target_id AS lane_id,
              acceptProp.acceptance_status AS property_acceptance_status
            """;

        var propertyAcceptanceResult = await _queryService.ExecuteAsync(
            databasePath,
            propertyAcceptanceCypher,
            parameters: null,
            rowLimit: Math.Max(rowLimit * 10, rowLimit),
            timeoutMs: timeoutMs,
            cancellationToken: cancellationToken);

        if (!propertyAcceptanceResult.Success)
            return propertyAcceptanceResult;

        var propertyAcceptedLaneIds = propertyAcceptanceResult.Rows
            .Where(row => IsAcceptedStatus(row, "property_acceptance_status"))
            .Select(row => row.TryGetValue("lane_id", out var value) ? value?.ToString() : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);

        var groupedRows = result.Rows
            .Where(predicate)
            .GroupBy(row => row["lane_id"]?.ToString(), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group =>
            {
                var accepted = group.Any(row =>
                    IsAcceptedStatus(row, "edge_acceptance_status")) ||
                    propertyAcceptedLaneIds.Contains(group.Key!);

                var completedWorkItemIds = group
                    .Where(row =>
                    {
                        var status = row.TryGetValue("work_status", out var value) ? value?.ToString() : null;
                        return status is not null &&
                               (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(status, "done", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(status, "reviewed", StringComparison.OrdinalIgnoreCase));
                    })
                    .Select(row => row.TryGetValue("work_item_id", out var value) ? value?.ToString() : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .Cast<object?>()
                    .ToArray();

                return new
                {
                    group,
                    accepted,
                    completedWorkItemIds
                };
            })
            .Where(item => !item.accepted && item.completedWorkItemIds.Length > 0)
            .Take(rowLimit)
            .Select(item =>
            {
                var first = item.group.First();
                return (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["flow_id"] = first["flow_id"],
                    ["target_repo_id"] = first["target_repo_id"],
                    ["stage_id"] = first["stage_id"],
                    ["lane_id"] = first["lane_id"],
                    ["lane_title"] = first["lane_title"],
                    ["work_item_ids"] = item.completedWorkItemIds
                };
            })
            .ToArray();

        return result with
        {
            Rows = groupedRows,
            RowCount = groupedRows.Length
        };
    }

    private HashSet<string> LoadLocalVerificationAcceptanceIds()
    {
        var indexPath = ResolveLocalVerificationIndexPath();
        if (indexPath is null || !File.Exists(indexPath))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var json = File.ReadAllText(indexPath);
        var index = JsonSerializer.Deserialize<AcceptanceVerificationIndex>(json);
        return index?.Resources?
            .Select(resource => resource.AcceptanceId)
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
    }

    private string? ResolveLocalVerificationIndexPath()
    {
        var relativePath = Path.Combine(".bo", "orchestration", "acceptance-verifications", "index.json");
        var fullPath = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));
        if (!fullPath.StartsWith(_workspaceRoot, StringComparison.Ordinal))
            return null;

        return fullPath;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) ? value?.ToString() : null;

    private async Task<QueryToolResult> ExecuteAndFilterAsync(
        string databasePath,
        string cypher,
        int rowLimit,
        int timeoutMs,
        CancellationToken cancellationToken,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        var result = await _queryService.ExecuteAsync(
            databasePath,
            cypher,
            parameters: null,
            rowLimit: rowLimit,
            timeoutMs: timeoutMs,
            cancellationToken: cancellationToken);

        if (!result.Success)
            return result;

        var filteredRows = result.Rows
            .Where(predicate)
            .ToArray();

        return result with
        {
            Rows = filteredRows,
            RowCount = filteredRows.Length
        };
    }

    private static bool MatchesString(IReadOnlyDictionary<string, object?> row, string key, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return true;

        return row.TryGetValue(key, out var value) &&
               string.Equals(value?.ToString(), expected, StringComparison.Ordinal);
    }

    private static bool IsAcceptedStatus(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value))
            return false;

        var status = value?.ToString();
        return string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "released", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AcceptanceVerificationIndex
    {
        [JsonPropertyName("resources")]
        public List<AcceptanceVerificationIndexEntry>? Resources { get; set; }
    }

    private sealed class AcceptanceVerificationIndexEntry
    {
        [JsonPropertyName("acceptance_id")]
        public string? AcceptanceId { get; set; }
    }
}
