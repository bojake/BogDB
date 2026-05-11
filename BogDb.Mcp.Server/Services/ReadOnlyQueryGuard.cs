using System.Text.RegularExpressions;

namespace BogDb.Mcp.Server.Services;

internal static partial class ReadOnlyQueryGuard
{
    [GeneratedRegex(@"\b(CREATE|MERGE|DELETE|SET|COPY|DROP|ALTER|INSTALL|UNINSTALL|UPDATE|BEGIN|COMMIT|ROLLBACK|CHECKPOINT)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MutatingKeywordRegex();

    public static void EnsureReadOnly(string cypher)
    {
        if (string.IsNullOrWhiteSpace(cypher))
            throw new InvalidOperationException("Cypher query cannot be empty.");

        var trimmed = cypher.TrimStart();
        if (trimmed.StartsWith("LOAD FROM", StringComparison.OrdinalIgnoreCase))
            return;

        if (MutatingKeywordRegex().IsMatch(cypher))
            throw new InvalidOperationException("bogdb_query is read-only in the first MCP slice.");
    }
}
