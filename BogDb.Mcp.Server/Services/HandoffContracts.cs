using System.Text.Json.Serialization;

namespace BogDb.Mcp.Server.Services;

internal sealed class HandoffIndex
{
    [JsonPropertyName("protocol_version")]
    public string? ProtocolVersion { get; set; }

    [JsonPropertyName("generated_at_utc")]
    public string? GeneratedAtUtc { get; set; }

    [JsonPropertyName("resources")]
    public List<HandoffIndexEntry> Resources { get; set; } = [];
}

internal sealed class HandoffIndexEntry
{
    [JsonPropertyName("artifact_id")]
    public string ArtifactId { get; set; } = string.Empty;

    [JsonPropertyName("resource_uri")]
    public string ResourceUri { get; set; } = string.Empty;

    [JsonPropertyName("handoff_kind")]
    public string HandoffKind { get; set; } = string.Empty;

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = string.Empty;

    [JsonPropertyName("relative_path")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("producer")]
    public string? Producer { get; set; }

    [JsonPropertyName("created_by_agent_uid")]
    public string? CreatedByAgentUid { get; set; }

    [JsonPropertyName("target_agent_uid")]
    public string? TargetAgentUid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("blocker_codes")]
    public List<string>? BlockerCodes { get; set; }

    [JsonPropertyName("actionability_score")]
    public double? ActionabilityScore { get; set; }
}
