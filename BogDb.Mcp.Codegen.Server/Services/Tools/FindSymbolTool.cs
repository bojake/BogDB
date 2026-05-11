using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_find_symbol — Locate symbol definitions by name, kind, repo, or file.
/// Returns matched symbols with file location, signature, and docstring.
/// </summary>
public static class FindSymbolTool
{
    public const string Name = "codegen_find_symbol";
    public const string Description =
        "Find symbol definitions (class, function, type, interface, etc.) by name or filter. " +
        "Returns qualified name, kind, signature, docstring, file path, and location.";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            name     = new { type = "string", description = "Symbol name or partial name to search for" },
            kind     = new { type = "string", description = "Symbol kind filter: class, function, type, const, interface, enum, method, property" },
            repo     = new { type = "string", description = "Filter to a specific repo name" },
            file     = new { type = "string", description = "Filter to a specific file path" },
            limit    = new { type = "integer", description = "Max results (default 50)", minimum = 1, maximum = 500 },
        },
        required = new[] { "name" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var name  = GetString(arguments, "name");
        var kind  = GetOptionalString(arguments, "kind");
        var repo  = GetOptionalString(arguments, "repo");
        var file  = GetOptionalString(arguments, "file");
        var limit = GetOptionalInt(arguments, "limit") ?? 50;

        // Build the MATCH clause dynamically
        var cypher = "MATCH (f:File)-[:DEFINES_SYMBOL]->(s:Symbol)";
        var filters = new List<string> { $"s.name CONTAINS '{Escape(name)}'" };

        if (!string.IsNullOrEmpty(kind))
            filters.Add($"s.kind = '{Escape(kind)}'");

        if (!string.IsNullOrEmpty(file))
            filters.Add($"f.path CONTAINS '{Escape(file)}'");

        if (!string.IsNullOrEmpty(repo))
        {
            cypher = "MATCH (r:Repo)-[:CONTAINS_PACKAGE]->(:Package)-[:CONTAINS_MODULE]->(:Module)-[:CONTAINS_FILE]->(f:File)-[:DEFINES_SYMBOL]->(s:Symbol)";
            filters.Add($"r.name = '{Escape(repo)}'");
        }

        cypher += $" WHERE {string.Join(" AND ", filters)}";
        cypher += $" RETURN s.id AS id, s.name AS name, s.qualifiedName AS qualifiedName, s.kind AS kind, " +
                  $"s.signature AS signature, s.docstring AS docstring, s.startLine AS startLine, " +
                  $"s.endLine AS endLine, f.path AS filePath";
        cypher += $" ORDER BY s.qualifiedName LIMIT {limit}";

        var result = conn.Query(cypher);
        if (!result.IsSuccess)
            return new { success = false, error = result.ErrorMessage, symbols = Array.Empty<object>() };

        var symbols = new List<object>();
        while (result.HasNext())
        {
            var row = result.GetNext().GetAsDictionary();
            symbols.Add(new
            {
                id            = row.GetValueOrDefault("id")?.ToString(),
                name          = row.GetValueOrDefault("name")?.ToString(),
                qualifiedName = row.GetValueOrDefault("qualifiedName")?.ToString(),
                kind          = row.GetValueOrDefault("kind")?.ToString(),
                signature     = row.GetValueOrDefault("signature")?.ToString(),
                docstring     = row.GetValueOrDefault("docstring")?.ToString(),
                startLine     = row.GetValueOrDefault("startLine"),
                endLine       = row.GetValueOrDefault("endLine"),
                filePath      = row.GetValueOrDefault("filePath")?.ToString(),
            });
        }

        return new { success = true, count = symbols.Count, symbols };
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetOptionalInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
