using System.Text.Json;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Mcp.Server.Services;

public sealed class OrchestrationAcceptanceIngestToolService
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

    public Task<OrchestrationAcceptanceIngestResult> IngestAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var artifactPath = JsonArgumentReader.GetOptionalString(arguments, "artifactPath");
        var indexPath = JsonArgumentReader.GetOptionalString(arguments, "indexPath");

        if (string.IsNullOrWhiteSpace(artifactPath) && string.IsNullOrWhiteSpace(indexPath))
            throw new InvalidOperationException("Either 'artifactPath' or 'indexPath' is required.");

        return Task.Run(() =>
        {
            var artifacts = !string.IsNullOrWhiteSpace(artifactPath)
                ? [LoadArtifact(artifactPath, "artifact", null)]
                : LoadArtifactsFromIndex(indexPath!);

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
            connection.EnsureNodeTable("AcceptanceIngestRecord", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["ingest_id"] = LogicalTypeID.STRING,
                ["acceptance_id"] = LogicalTypeID.STRING,
                ["target_id"] = LogicalTypeID.STRING,
                ["target_kind"] = LogicalTypeID.STRING,
                ["acceptance_status"] = LogicalTypeID.STRING,
                ["ingested_at_utc"] = LogicalTypeID.STRING,
                ["source"] = LogicalTypeID.STRING,
                ["artifact_path"] = LogicalTypeID.STRING,
                ["index_path"] = LogicalTypeID.STRING
            });

            foreach (var artifact in artifacts)
            {
                connection.UpsertNodeById("AcceptanceRecord", artifact.AcceptanceId, new Dictionary<string, object?>
                {
                    ["acceptance_id"] = artifact.AcceptanceId,
                    ["target_id"] = artifact.TargetId,
                    ["target_kind"] = artifact.TargetKind,
                    ["acceptance_status"] = artifact.AcceptanceStatus,
                    ["recorded_at_utc"] = artifact.RecordedAtUtc,
                    ["recorded_by_agent_uid"] = artifact.RecordedByAgentUid,
                    ["source_work_item_id"] = artifact.SourceWorkItemId,
                    ["notes"] = artifact.Notes
                });

                var ingestId = $"ingest:{artifact.AcceptanceId}";
                connection.UpsertNodeById("AcceptanceIngestRecord", ingestId, new Dictionary<string, object?>
                {
                    ["ingest_id"] = ingestId,
                    ["acceptance_id"] = artifact.AcceptanceId,
                    ["target_id"] = artifact.TargetId,
                    ["target_kind"] = artifact.TargetKind,
                    ["acceptance_status"] = artifact.AcceptanceStatus,
                    ["ingested_at_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["source"] = artifact.Source,
                    ["artifact_path"] = artifact.ArtifactPath,
                    ["index_path"] = artifact.IndexPath
                });
            }

            connection.Commit();

            return new OrchestrationAcceptanceIngestResult(
                Success: true,
                IngestedCount: artifacts.Count,
                AcceptanceIds: artifacts.Select(item => item.AcceptanceId).ToArray(),
                TargetIds: artifacts.Select(item => item.TargetId).Distinct(StringComparer.Ordinal).ToArray(),
                Source: !string.IsNullOrWhiteSpace(artifactPath) ? "artifact" : "index");
        }, cancellationToken);
    }

    private static List<AcceptanceArtifactRecord> LoadArtifactsFromIndex(string indexPath)
    {
        var fullIndexPath = Path.GetFullPath(indexPath);
        if (!File.Exists(fullIndexPath))
            throw new InvalidOperationException($"Acceptance index '{indexPath}' does not exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(fullIndexPath));
        if (!document.RootElement.TryGetProperty("resources", out var resourcesElement) ||
            resourcesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Acceptance index '{indexPath}' is missing a resources array.");
        }

        var publishRoot = ResolvePublishRootFromIndex(fullIndexPath);
        var artifacts = new List<AcceptanceArtifactRecord>();
        foreach (var resourceElement in resourcesElement.EnumerateArray())
        {
            if (!resourceElement.TryGetProperty("relative_path", out var relativePathElement) ||
                relativePathElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"Acceptance index '{indexPath}' contains a resource without relative_path.");
            }

            var relativePath = relativePathElement.GetString()!;
            var fullArtifactPath = Path.GetFullPath(Path.Combine(publishRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            artifacts.Add(LoadArtifact(fullArtifactPath, "index", fullIndexPath));
        }

        return artifacts;
    }

    private static string ResolvePublishRootFromIndex(string indexPath)
    {
        var acceptanceDirectory = Path.GetDirectoryName(indexPath)
            ?? throw new InvalidOperationException($"Acceptance index '{indexPath}' does not have a valid parent directory.");
        var workspaceDirectory = Directory.GetParent(acceptanceDirectory)?.FullName
            ?? throw new InvalidOperationException($"Acceptance index '{indexPath}' does not have a valid workspace parent.");
        var publishRoot = Directory.GetParent(workspaceDirectory)?.FullName
            ?? throw new InvalidOperationException($"Acceptance index '{indexPath}' does not have a valid publish root parent.");
        return publishRoot;
    }

    private static AcceptanceArtifactRecord LoadArtifact(string artifactPath, string source, string? indexPath)
    {
        var fullArtifactPath = Path.GetFullPath(artifactPath);
        if (!File.Exists(fullArtifactPath))
            throw new InvalidOperationException($"Acceptance artifact '{artifactPath}' does not exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(fullArtifactPath));
        if (!document.RootElement.TryGetProperty("acceptance", out var acceptanceElement) ||
            acceptanceElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Acceptance artifact '{artifactPath}' is missing an acceptance payload.");
        }

        var acceptanceId = GetRequiredString(acceptanceElement, "acceptance_id", artifactPath);
        var targetId = GetRequiredString(acceptanceElement, "target_id", artifactPath);
        var targetKind = GetRequiredString(acceptanceElement, "target_kind", artifactPath).ToLowerInvariant();
        var acceptanceStatus = GetRequiredString(acceptanceElement, "acceptance_status", artifactPath).ToLowerInvariant();
        var recordedAtUtc = GetRequiredString(acceptanceElement, "recorded_at_utc", artifactPath);
        var recordedByAgentUid = GetOptionalString(acceptanceElement, "recorded_by_agent_uid");
        var sourceWorkItemId = GetOptionalString(acceptanceElement, "source_work_item_id");
        var notes = GetOptionalString(acceptanceElement, "notes");

        if (!AllowedTargetKinds.Contains(targetKind))
            throw new InvalidOperationException($"Acceptance artifact '{artifactPath}' contains unsupported target_kind '{targetKind}'.");

        if (!AllowedAcceptanceStatuses.Contains(acceptanceStatus))
            throw new InvalidOperationException($"Acceptance artifact '{artifactPath}' contains unsupported acceptance_status '{acceptanceStatus}'.");

        return new AcceptanceArtifactRecord(
            AcceptanceId: acceptanceId,
            TargetId: targetId,
            TargetKind: targetKind,
            AcceptanceStatus: acceptanceStatus,
            RecordedAtUtc: recordedAtUtc,
            RecordedByAgentUid: recordedByAgentUid,
            SourceWorkItemId: sourceWorkItemId,
            Notes: notes,
            Source: source,
            ArtifactPath: fullArtifactPath,
            IndexPath: indexPath);
    }

    private static string GetRequiredString(JsonElement element, string propertyName, string artifactPath)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Acceptance artifact '{artifactPath}' is missing required string property '{propertyName}'.");
        return property.GetString()!;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;
        if (property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Acceptance artifact contains non-string value for '{propertyName}'.");
        return property.GetString();
    }

    private sealed record AcceptanceArtifactRecord(
        string AcceptanceId,
        string TargetId,
        string TargetKind,
        string AcceptanceStatus,
        string RecordedAtUtc,
        string? RecordedByAgentUid,
        string? SourceWorkItemId,
        string? Notes,
        string Source,
        string ArtifactPath,
        string? IndexPath);
}

public sealed record OrchestrationAcceptanceIngestResult(
    bool Success,
    int IngestedCount,
    IReadOnlyList<string> AcceptanceIds,
    IReadOnlyList<string> TargetIds,
    string Source);
