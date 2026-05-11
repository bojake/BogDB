using System;

namespace BogDb.Extensions.HttpFS;

/// <summary>
/// Configuration for HTTP/S3 connections.
/// C++ parity: <c>http_config.h/cpp</c> + <c>s3fs_config.h/cpp</c>
///
/// Values are resolved in priority order:
///   1. Explicit property set on this object
///   2. BogDatabase extension option (e.g. SET s3_access_key_id = '...')
///   3. Environment variable (AWS_ACCESS_KEY_ID, etc.)
///   4. Default value
/// </summary>
public sealed class HttpFSConfig
{
    // ── HTTP Settings ─────────────────────────────────────────────────────────

    /// <summary>Connection/request timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of retry attempts for transient failures.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay between retries (doubled on each retry = exponential backoff).</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>HTTP proxy URL (e.g. http://proxy:8080). Null = no proxy.</summary>
    public string? ProxyUrl { get; set; }

    // ── S3 Authentication ─────────────────────────────────────────────────────

    /// <summary>AWS access key ID. Falls back to AWS_ACCESS_KEY_ID env var.</summary>
    public string? S3AccessKeyId { get; set; }

    /// <summary>AWS secret access key. Falls back to AWS_SECRET_ACCESS_KEY env var.</summary>
    public string? S3SecretAccessKey { get; set; }

    /// <summary>AWS session token for temporary credentials. Falls back to AWS_SESSION_TOKEN.</summary>
    public string? S3SessionToken { get; set; }

    /// <summary>AWS region. Falls back to AWS_REGION, then "us-east-1".</summary>
    public string? S3Region { get; set; }

    /// <summary>S3 endpoint override. Falls back to AWS_S3_ENDPOINT, then "s3.amazonaws.com".</summary>
    public string? S3Endpoint { get; set; }

    /// <summary>Use path-style access (bucket in path instead of subdomain). For MinIO/localstack.</summary>
    public bool S3UsePathStyle { get; set; }

    /// <summary>Returns true if S3 credentials are configured.</summary>
    public bool HasS3Credentials =>
        !string.IsNullOrEmpty(ResolvedAccessKeyId) &&
        !string.IsNullOrEmpty(ResolvedSecretAccessKey);

    // ── Resolved values (property > env var > default) ───────────────────────

    public string? ResolvedAccessKeyId =>
        S3AccessKeyId ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");

    public string? ResolvedSecretAccessKey =>
        S3SecretAccessKey ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");

    public string? ResolvedSessionToken =>
        S3SessionToken ?? Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");

    public string ResolvedRegion =>
        !string.IsNullOrEmpty(S3Region) ? S3Region
        : Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";

    public string ResolvedEndpoint =>
        !string.IsNullOrEmpty(S3Endpoint) ? S3Endpoint
        : Environment.GetEnvironmentVariable("AWS_S3_ENDPOINT") ?? "s3.amazonaws.com";

    /// <summary>Populate config from BogDatabase extension options if available.</summary>
    public void LoadFromDatabase(BogDb.Core.Main.BogDatabase db)
    {
        S3AccessKeyId     ??= TryGetOption(db, "s3_access_key_id");
        S3SecretAccessKey ??= TryGetOption(db, "s3_secret_access_key");
        S3SessionToken    ??= TryGetOption(db, "s3_session_token");

        var region = TryGetOption(db, "s3_region");
        if (!string.IsNullOrEmpty(region)) S3Region = region!;

        var endpoint = TryGetOption(db, "s3_endpoint");
        if (!string.IsNullOrEmpty(endpoint)) S3Endpoint = endpoint!;

        var pathStyle = TryGetOption(db, "s3_url_style");
        if (string.Equals(pathStyle, "path", StringComparison.OrdinalIgnoreCase))
            S3UsePathStyle = true;
    }

    private static string? TryGetOption(BogDb.Core.Main.BogDatabase db, string name)
    {
        try { return db.GetExtensionOptionValue(name)?.ToString(); }
        catch (System.Collections.Generic.KeyNotFoundException) { return null; }
    }

    /// <summary>Default config with env-var fallbacks only.</summary>
    public static HttpFSConfig Default => new();
}
