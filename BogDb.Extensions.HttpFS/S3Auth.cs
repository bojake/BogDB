using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace BogDb.Extensions.HttpFS;

/// <summary>
/// AWS Signature Version 4 request signer.
/// C++ parity: <c>s3fs.cpp::createS3Header()</c> + <c>crypto.cpp</c>
///
/// Uses .NET's built-in <see cref="HMACSHA256"/> and <see cref="SHA256"/> instead of
/// the C++ engine's hand-rolled mbedtls implementation.
///
/// Reference: https://docs.aws.amazon.com/general/latest/gr/sigv4_signing.html
/// </summary>
public static class S3Auth
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private const string Service   = "s3";

    /// <summary>
    /// Sign an <see cref="HttpRequestMessage"/> with AWS SigV4 headers.
    /// Mutates the request by adding Authorization, x-amz-date, x-amz-content-sha256,
    /// and optionally x-amz-security-token headers.
    /// </summary>
    public static void SignRequest(HttpRequestMessage request, HttpFSConfig config)
    {
        var accessKey = config.ResolvedAccessKeyId;
        var secretKey = config.ResolvedSecretAccessKey;
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            return; // No credentials — skip signing (public bucket)

        var now = DateTime.UtcNow;
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var amzDate   = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var region    = config.ResolvedRegion;

        var uri  = request.RequestUri!;
        var host = uri.Host;
        var path = uri.AbsolutePath;

        // Payload hash (empty body for GET/HEAD, or hash of body for PUT)
        var payloadHash = "UNSIGNED-PAYLOAD";
        if (request.Content != null)
        {
            var body = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            payloadHash = HexEncode(Sha256Hash(body));
        }

        // Set required headers
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.Host = host;

        if (!string.IsNullOrEmpty(config.ResolvedSessionToken))
            request.Headers.TryAddWithoutValidation("x-amz-security-token", config.ResolvedSessionToken);

        // ── Step 1: Canonical Request ─────────────────────────────────────────
        var method = request.Method.Method.ToUpperInvariant();
        var canonicalUri = Uri.EscapeDataString(path).Replace("%2F", "/");
        var canonicalQueryString = BuildCanonicalQueryString(uri);

        // Collect and sort headers
        var headerMap = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        headerMap["host"] = host;
        headerMap["x-amz-content-sha256"] = payloadHash;
        headerMap["x-amz-date"] = amzDate;
        if (!string.IsNullOrEmpty(config.ResolvedSessionToken))
            headerMap["x-amz-security-token"] = config.ResolvedSessionToken!;

        // Add any Range header (important for range reads)
        if (request.Headers.Range != null)
            headerMap["range"] = request.Headers.Range.ToString();

        var canonicalHeaders = string.Join("",
            headerMap.Select(kv => $"{kv.Key.ToLowerInvariant()}:{kv.Value.Trim()}\n"));
        var signedHeaders = string.Join(";",
            headerMap.Keys.Select(k => k.ToLowerInvariant()));

        var canonicalRequest = string.Join("\n",
            method,
            canonicalUri,
            canonicalQueryString,
            canonicalHeaders,
            signedHeaders,
            payloadHash);

        // ── Step 2: String to Sign ────────────────────────────────────────────
        var credentialScope = $"{dateStamp}/{region}/{Service}/aws4_request";
        var stringToSign = string.Join("\n",
            Algorithm,
            amzDate,
            credentialScope,
            HexEncode(Sha256Hash(Encoding.UTF8.GetBytes(canonicalRequest))));

        // ── Step 3: Signing Key ───────────────────────────────────────────────
        var signingKey = GetSigningKey(secretKey!, dateStamp, region);

        // ── Step 4: Signature ─────────────────────────────────────────────────
        var signature = HexEncode(HmacSha256(signingKey, stringToSign));

        // ── Step 5: Authorization Header ──────────────────────────────────────
        var authHeader = $"{Algorithm} Credential={accessKey}/{credentialScope}, " +
                         $"SignedHeaders={signedHeaders}, Signature={signature}";
        request.Headers.TryAddWithoutValidation("Authorization", authHeader);
    }

    // ── Crypto Helpers (using .NET built-ins) ────────────────────────────────

    private static byte[] Sha256Hash(byte[] data)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(data);
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static byte[] GetSigningKey(string secretKey, string dateStamp, string region)
    {
        var kSecret  = Encoding.UTF8.GetBytes("AWS4" + secretKey);
        var kDate    = HmacSha256(kSecret, dateStamp);
        var kRegion  = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, Service);
        return HmacSha256(kService, "aws4_request");
    }

    private static string HexEncode(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 2);
        foreach (var b in data) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string BuildCanonicalQueryString(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query) || uri.Query == "?") return "";

        var pairs = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p =>
            {
                var eq = p.IndexOf('=');
                return eq < 0
                    ? (Uri.EscapeDataString(p), "")
                    : (Uri.EscapeDataString(p[..eq]), Uri.EscapeDataString(p[(eq+1)..]));
            })
            .OrderBy(p => p.Item1, StringComparer.Ordinal)
            .ThenBy(p => p.Item2, StringComparer.Ordinal);

        return string.Join("&", pairs.Select(p => $"{p.Item1}={p.Item2}"));
    }
}
