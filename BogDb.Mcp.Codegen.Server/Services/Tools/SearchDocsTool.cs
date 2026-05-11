using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_search_docs — Search runbooks and architecture docs.
/// </summary>
public static class SearchDocsTool
{
    public const string Name = "codegen_search_docs";
    public const string Description =
        "Search architecture documents (ADRs, RFCs, design docs) and runbooks by keyword. " +
        "Answers: 'Is there a design doc for the auth system?'";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Search keyword or phrase" },
            scope = new { type = "string", description = "Optional scope filter (e.g. 'auth', 'payments')" },
        },
        required = new[] { "query" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var query = GetString(arguments, "query");
        var scope = GetOptionalString(arguments, "scope");

        // Search ArchDocs
        var archCypher = $@"
            MATCH (d:ArchDoc)
            WHERE d.title CONTAINS '{Escape(query)}'
               OR d.scope CONTAINS '{Escape(query)}'
               {(!string.IsNullOrEmpty(scope) ? $"OR d.scope CONTAINS '{Escape(scope)}'" : "")}
            RETURN d.id AS id, d.title AS title, d.url AS url, d.scope AS scope, d.format AS format,
                   'arch_doc' AS docType
            ORDER BY d.title
            LIMIT 50";

        // Search Runbooks
        var runbookCypher = $@"
            MATCH (rb:Runbook)
            WHERE rb.title CONTAINS '{Escape(query)}'
               OR rb.scope CONTAINS '{Escape(query)}'
               {(!string.IsNullOrEmpty(scope) ? $"OR rb.scope CONTAINS '{Escape(scope)}'" : "")}
            RETURN rb.id AS id, rb.title AS title, rb.url AS url, rb.scope AS scope,
                   rb.lastVerified AS lastVerified, 'runbook' AS docType
            ORDER BY rb.title
            LIMIT 50";

        var results = new List<object>();

        var archResult = conn.Query(archCypher);
        if (archResult.IsSuccess)
        {
            while (archResult.HasNext())
            {
                var row = archResult.GetNext().GetAsDictionary();
                results.Add(new
                {
                    id      = row.GetValueOrDefault("id")?.ToString(),
                    title   = row.GetValueOrDefault("title")?.ToString(),
                    url     = row.GetValueOrDefault("url")?.ToString(),
                    scope   = row.GetValueOrDefault("scope")?.ToString(),
                    format  = row.GetValueOrDefault("format")?.ToString(),
                    docType = "arch_doc",
                });
            }
        }

        var runbookResult = conn.Query(runbookCypher);
        if (runbookResult.IsSuccess)
        {
            while (runbookResult.HasNext())
            {
                var row = runbookResult.GetNext().GetAsDictionary();
                results.Add(new
                {
                    id           = row.GetValueOrDefault("id")?.ToString(),
                    title        = row.GetValueOrDefault("title")?.ToString(),
                    url          = row.GetValueOrDefault("url")?.ToString(),
                    scope        = row.GetValueOrDefault("scope")?.ToString(),
                    lastVerified = row.GetValueOrDefault("lastVerified")?.ToString(),
                    docType      = "runbook",
                });
            }
        }

        return new { success = true, query, count = results.Count, documents = results };
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
