using System.Globalization;

namespace BogDb.Core.Extraction;

/// <summary>
/// Canonical transport formatter for extracted graph node identities.
/// Extraction payloads must not depend on ad hoc ToString() behavior.
/// </summary>
public static class GraphExternalIdFormatter
{
    private const string NodePrefix = "node:";

    public static string FormatNodeId(string tableName, object nodeId)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));
        ArgumentNullException.ThrowIfNull(nodeId);

        var normalizedId = NormalizeNodeId(nodeId);
        return $"{NodePrefix}{EscapeSegment(tableName)}:{EscapeSegment(normalizedId)}";
    }

    public static bool TryParseNodeId(
        string externalId,
        out string tableName,
        out string nodeId)
    {
        tableName = string.Empty;
        nodeId = string.Empty;

        if (string.IsNullOrWhiteSpace(externalId) ||
            !externalId.StartsWith(NodePrefix, StringComparison.Ordinal))
            return false;

        var payload = externalId[NodePrefix.Length..];
        var separatorIndex = payload.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == payload.Length - 1)
            return false;

        tableName = UnescapeSegment(payload[..separatorIndex]);
        nodeId = UnescapeSegment(payload[(separatorIndex + 1)..]);
        return tableName.Length > 0 && nodeId.Length > 0;
    }

    private static string NormalizeNodeId(object nodeId)
    {
        var normalized = nodeId switch
        {
            string s => s,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => nodeId.ToString()
        };

        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Node id must produce a non-empty string.", nameof(nodeId));

        return normalized;
    }

    private static string EscapeSegment(string value) => Uri.EscapeDataString(value);

    private static string UnescapeSegment(string value) => Uri.UnescapeDataString(value);
}
