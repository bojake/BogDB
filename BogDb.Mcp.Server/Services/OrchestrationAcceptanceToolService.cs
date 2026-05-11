using System.Text.Json;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Mcp.Server.Services;

public sealed class OrchestrationAcceptanceToolService
{
    private static readonly HashSet<string> AllowedTargetKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "lane",
        "gate",
        "stage"
    };

    private static readonly HashSet<string> AllowedAcceptanceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "reviewed",
        "accepted",
        "released",
        "rejected"
    };

    public Task<OrchestrationAcceptanceWriteResult> RecordAcceptanceAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var acceptanceId = JsonArgumentReader.GetRequiredString(arguments, "acceptanceId");
        var targetId = JsonArgumentReader.GetRequiredString(arguments, "targetId");
        var targetKind = JsonArgumentReader.GetRequiredString(arguments, "targetKind");
        var acceptanceStatus = JsonArgumentReader.GetRequiredString(arguments, "acceptanceStatus");
        var recordedAtUtc = JsonArgumentReader.GetOptionalString(arguments, "recordedAtUtc") ?? DateTimeOffset.UtcNow.ToString("O");
        var recordedByAgentUid = JsonArgumentReader.GetOptionalString(arguments, "recordedByAgentUid");
        var sourceWorkItemId = JsonArgumentReader.GetOptionalString(arguments, "sourceWorkItemId");
        var notes = JsonArgumentReader.GetOptionalString(arguments, "notes");

        if (!AllowedTargetKinds.Contains(targetKind))
            throw new InvalidOperationException($"Argument 'targetKind' must be one of: {string.Join(", ", AllowedTargetKinds)}.");

        if (!AllowedAcceptanceStatuses.Contains(acceptanceStatus))
            throw new InvalidOperationException($"Argument 'acceptanceStatus' must be one of: {string.Join(", ", AllowedAcceptanceStatuses)}.");

        return Task.Run(() =>
        {
            using var database = BogDatabase.Open(databasePath);
            using var connection = new BogConnection(database);
            connection.BeginWriteTransaction();

            connection.EnsureNodeTable("AcceptanceRecord", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["acceptance_id"] = LogicalTypeID.STRING,
                ["target_id"] = LogicalTypeID.STRING,
                ["target_kind"] = LogicalTypeID.STRING,
                ["acceptance_status"] = LogicalTypeID.STRING,
                ["recorded_at_utc"] = LogicalTypeID.STRING,
                ["recorded_by_agent_uid"] = LogicalTypeID.STRING,
                ["source_work_item_id"] = LogicalTypeID.STRING,
                ["notes"] = LogicalTypeID.STRING
            });

            connection.UpsertNodeById("AcceptanceRecord", acceptanceId, new Dictionary<string, object?>
            {
                ["acceptance_id"] = acceptanceId,
                ["target_id"] = targetId,
                ["target_kind"] = targetKind.ToLowerInvariant(),
                ["acceptance_status"] = acceptanceStatus.ToLowerInvariant(),
                ["recorded_at_utc"] = recordedAtUtc,
                ["recorded_by_agent_uid"] = recordedByAgentUid,
                ["source_work_item_id"] = sourceWorkItemId,
                ["notes"] = notes
            });

            connection.Commit();

            return new OrchestrationAcceptanceWriteResult(
                Success: true,
                AcceptanceId: acceptanceId,
                TargetId: targetId,
                TargetKind: targetKind.ToLowerInvariant(),
                AcceptanceStatus: acceptanceStatus.ToLowerInvariant(),
                RecordedAtUtc: recordedAtUtc);
        }, cancellationToken);
    }
}

public sealed record OrchestrationAcceptanceWriteResult(
    bool Success,
    string AcceptanceId,
    string TargetId,
    string TargetKind,
    string AcceptanceStatus,
    string RecordedAtUtc);
