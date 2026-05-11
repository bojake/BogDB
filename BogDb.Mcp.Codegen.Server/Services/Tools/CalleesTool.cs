using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_callees — Find transitive dependencies/callees of a symbol.
/// </summary>
public static class CalleesTool
{
    public const string Name = "codegen_callees";
    public const string Description =
        "Find all symbols that a given symbol calls/references, with configurable traversal depth. " +
        "Answers: 'What does this function depend on?'";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            symbolName = new { type = "string", description = "Qualified or simple name of the source symbol" },
            depth      = new { type = "integer", description = "Max traversal depth (default 3)", minimum = 1, maximum = 10 },
            repo       = new { type = "string", description = "Filter callees to a specific repo" },
        },
        required = new[] { "symbolName" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var symbolName = GetString(arguments, "symbolName");
        var depth      = GetOptionalInt(arguments, "depth") ?? 3;
        var repo       = GetOptionalString(arguments, "repo");

        var cypher = $@"
            MATCH (source:Symbol)
            WHERE source.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR source.name = '{Escape(symbolName)}'
            WITH source LIMIT 1
            MATCH (source)-[:REFERENCES_SYMBOL*1..{depth}]->(callee:Symbol)
            MATCH (f:File)-[:DEFINES_SYMBOL]->(callee)";

        if (!string.IsNullOrEmpty(repo))
        {
            cypher += $@"
            MATCH (r:Repo)-[:CONTAINS_PACKAGE]->(:Package)-[:CONTAINS_MODULE]->(:Module)-[:CONTAINS_FILE]->(f)
            WHERE r.name = '{Escape(repo)}'";
        }

        cypher += @"
            RETURN DISTINCT callee.id AS id, callee.name AS name, callee.qualifiedName AS qualifiedName,
                   callee.kind AS kind, callee.signature AS signature, f.path AS filePath
            ORDER BY callee.qualifiedName
            LIMIT 100";

        var result = conn.Query(cypher);
        if (!result.IsSuccess)
            return new { success = false, error = result.ErrorMessage, callees = Array.Empty<object>() };

        var callees = new List<object>();
        while (result.HasNext())
        {
            var row = result.GetNext().GetAsDictionary();
            callees.Add(new
            {
                id            = row.GetValueOrDefault("id")?.ToString(),
                name          = row.GetValueOrDefault("name")?.ToString(),
                qualifiedName = row.GetValueOrDefault("qualifiedName")?.ToString(),
                kind          = row.GetValueOrDefault("kind")?.ToString(),
                signature     = row.GetValueOrDefault("signature")?.ToString(),
                filePath      = row.GetValueOrDefault("filePath")?.ToString(),
            });
        }

        return new { success = true, sourceSymbol = symbolName, depth, count = callees.Count, callees };
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetOptionalInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
