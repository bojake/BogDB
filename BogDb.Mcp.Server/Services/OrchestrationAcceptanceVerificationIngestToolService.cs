using System.Text.Json;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Mcp.Server.Services;

public sealed class OrchestrationAcceptanceVerificationIngestToolService
{
    private static readonly HashSet<string> AllowedTargetKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "lane",
        "gate",
        "stage"
    };

    private static readonly HashSet<string> AllowedIngestStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ingested",
        "missing",
        "error"
    };

    public Task<OrchestrationAcceptanceVerificationIngestResult> IngestAsync(JsonElement arguments, CancellationToken cancellationToken)
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

            connection.EnsureNodeTable("AcceptanceVerificationRecord", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["verification_id"] = LogicalTypeID.STRING,
                ["acceptance_id"] = LogicalTypeID.STRING,
                ["target_id"] = LogicalTypeID.STRING,
                ["target_kind"] = LogicalTypeID.STRING,
                ["ingest_status"] = LogicalTypeID.STRING,
                ["observed_at_utc"] = LogicalTypeID.STRING,
                ["ingested_at_utc"] = LogicalTypeID.STRING,
                ["observed_by_agent_uid"] = LogicalTypeID.STRING,
                ["source"] = LogicalTypeID.STRING,
                ["artifact_path"] = LogicalTypeID.STRING,
                ["index_path"] = LogicalTypeID.STRING,
                ["notes"] = LogicalTypeID.STRING
            });

            foreach (var artifact in artifacts)
            {
                var verificationId = $"verification:{artifact.AcceptanceId}";
                connection.UpsertNodeById("AcceptanceVerificationRecord", verificationId, new Dictionary<string, object?>
                {
                    ["verification_id"] = verificationId,
                    ["acceptance_id"] = artifact.AcceptanceId,
                    ["target_id"] = artifact.TargetId,
                    ["target_kind"] = artifact.TargetKind,
                    ["ingest_status"] = artifact.IngestStatus,
                    ["observed_at_utc"] = artifact.ObservedAtUtc,
                    ["ingested_at_utc"] = artifact.IngestedAtUtc,
                    ["observed_by_agent_uid"] = artifact.ObservedByAgentUid,
                    ["source"] = artifact.Source,
                    ["artifact_path"] = artifact.ArtifactPath,
                    ["index_path"] = artifact.IndexPath,
                    ["notes"] = artifact.Notes
                });
            }

            connection.Commit();

            return new OrchestrationAcceptanceVerificationIngestResult(
                Success: true,
                IngestedCount: artifacts.Count,
                AcceptanceIds: artifacts.Select(item => item.AcceptanceId).ToArray(),
                TargetIds: artifacts.Select(item => item.TargetId).Distinct(StringComparer.Ordinal).ToArray(),
                Source: !string.IsNullOrWhiteSpace(artifactPath) ? "artifact" : "index");
        }, cancellationToken);
    }

    private static List<AcceptanceVerificationArtifactRecord> LoadArtifactsFromIndex(string indexPath)
    {
        var fullIndexPath = Path.GetFullPath(indexPath);
        if (!File.Exists(fullIndexPath))
            throw new InvalidOperationException($"Acceptance verification index '{indexPath}' does not exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(fullIndexPath));
        if (!document.RootElement.TryGetProperty("resources", out var resourcesElement) ||
            resourcesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Acceptance verification index '{indexPath}' is missing a resources array.");
        }

        var workspaceRoot = ResolveWorkspaceRootFromIndex(fullIndexPath);
        var artifacts = new List<AcceptanceVerificationArtifactRecord>();
        foreach (var resourceElement in resourcesElement.EnumerateArray())
        {
            if (!resourceElement.TryGetProperty("relative_path", out var relativePathElement) ||
                relativePathElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"Acceptance verification index '{indexPath}' contains a resource without relative_path.");
            }

            var relativePath = relativePathElement.GetString()!;
            var fullArtifactPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            artifacts.Add(LoadArtifact(fullArtifactPath, "index", fullIndexPath));
        }

        return artifacts;
    }

    private static string ResolveWorkspaceRootFromIndex(string indexPath)
    {
        var verificationDirectory = Path.GetDirectoryName(indexPath)
            ?? throw new InvalidOperationException($"Acceptance verification index '{indexPath}' does not have a valid parent directory.");
        var orchestrationDirectory = Directory.GetParent(verificationDirectory)?.FullName
            ?? throw new InvalidOperationException($"Acceptance verification index '{indexPath}' does not have a valid orchestration parent.");
        var boDirectory = Directory.GetParent(orchestrationDirectory)?.FullName
            ?? throw new InvalidOperationException($"Acceptance verification index '{indexPath}' does not have a valid .bo parent.");
        var workspaceRoot = Directory.GetParent(boDirectory)?.FullName
            ?? throw new InvalidOperationException($"Acceptance verification index '{indexPath}' does not have a valid workspace root parent.");
        return workspaceRoot;
    }

    private static AcceptanceVerificationArtifactRecord LoadArtifact(string artifactPath, string source, string? indexPath)
    {
        var fullArtifactPath = Path.GetFullPath(artifactPath);
        if (!File.Exists(fullArtifactPath))
            throw new InvalidOperationException($"Acceptance verification artifact '{artifactPath}' does not exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(fullArtifactPath));
        if (!document.RootElement.TryGetProperty("verification", out var verificationElement) ||
            verificationElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Acceptance verification artifact '{artifactPath}' is missing a verification payload.");
        }

        var acceptanceId = GetRequiredString(verificationElement, "acceptance_id", artifactPath);
        var targetId = GetRequiredString(verificationElement, "target_id", artifactPath);
        var targetKind = GetRequiredString(verificationElement, "target_kind", artifactPath).ToLowerInvariant();
        var ingestStatus = GetRequiredString(verificationElement, "ingest_status", artifactPath).ToLowerInvariant();
        var observedAtUtc = GetRequiredString(verificationElement, "observed_at_utc", artifactPath);
        var ingestedAtUtc = GetOptionalString(verificationElement, "ingested_at_utc");
        var observedByAgentUid = GetOptionalString(verificationElement, "observed_by_agent_uid");
        var notes = GetOptionalString(verificationElement, "notes");

        if (!AllowedTargetKinds.Contains(targetKind))
            throw new InvalidOperationException($"Acceptance verification artifact '{artifactPath}' contains unsupported target_kind '{targetKind}'.");

        if (!AllowedIngestStatuses.Contains(ingestStatus))
            throw new InvalidOperationException($"Acceptance verification artifact '{artifactPath}' contains unsupported ingest_status '{ingestStatus}'.");

        return new AcceptanceVerificationArtifactRecord(
            AcceptanceId: acceptanceId,
            TargetId: targetId,
            TargetKind: targetKind,
            IngestStatus: ingestStatus,
            ObservedAtUtc: observedAtUtc,
            IngestedAtUtc: ingestedAtUtc,
            ObservedByAgentUid: observedByAgentUid,
            Source: source,
            ArtifactPath: fullArtifactPath,
            IndexPath: indexPath,
            Notes: notes);
    }

    private static string GetRequiredString(JsonElement element, string propertyName, string artifactPath)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Acceptance verification artifact '{artifactPath}' is missing required string property '{propertyName}'.");
        return property.GetString()!;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;
        if (property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Acceptance verification artifact contains non-string value for '{propertyName}'.");
        return property.GetString();
    }

    private sealed record AcceptanceVerificationArtifactRecord(
        string AcceptanceId,
        string TargetId,
        string TargetKind,
        string IngestStatus,
        string ObservedAtUtc,
        string? IngestedAtUtc,
        string? ObservedByAgentUid,
        string Source,
        string ArtifactPath,
        string? IndexPath,
        string? Notes);
}

public sealed record OrchestrationAcceptanceVerificationIngestResult(
    bool Success,
    int IngestedCount,
    IReadOnlyList<string> AcceptanceIds,
    IReadOnlyList<string> TargetIds,
    string Source);
