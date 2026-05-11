using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_callers — Find transitive callers of a symbol via variable-length
/// REFERENCES_SYMBOL traversal.
/// </summary>
public static class CallersTool
{
    public const string Name = "codegen_callers";
    public const string Description =
        "Find all symbols that call/reference a given symbol, with configurable traversal depth. " +
        "Returns the call chain including intermediate symbols.";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            symbolName = new { type = "string", description = "Qualified or simple name of the target symbol" },
            depth      = new { type = "integer", description = "Max traversal depth (default 3)", minimum = 1, maximum = 10 },
            repo       = new { type = "string", description = "Filter callers to a specific repo" },
        },
        required = new[] { "symbolName" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var symbolName = GetString(arguments, "symbolName");
        var depth      = GetOptionalInt(arguments, "depth") ?? 3;
        var repo       = GetOptionalString(arguments, "repo");

        var cypher = $@"
            MATCH (target:Symbol)
            WHERE target.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR target.name = '{Escape(symbolName)}'
            WITH target LIMIT 1
            MATCH (caller:Symbol)-[:REFERENCES_SYMBOL*1..{depth}]->(target)
            MATCH (f:File)-[:DEFINES_SYMBOL]->(caller)";

        if (!string.IsNullOrEmpty(repo))
        {
            cypher += $@"
            MATCH (r:Repo)-[:CONTAINS_PACKAGE]->(:Package)-[:CONTAINS_MODULE]->(:Module)-[:CONTAINS_FILE]->(f)
            WHERE r.name = '{Escape(repo)}'";
        }

        cypher += @"
            RETURN DISTINCT caller.id AS id, caller.name AS name, caller.qualifiedName AS qualifiedName,
                   caller.kind AS kind, caller.signature AS signature, f.path AS filePath
            ORDER BY caller.qualifiedName
            LIMIT 100";

        var result = conn.Query(cypher);
        if (!result.IsSuccess)
            return new { success = false, error = result.ErrorMessage, callers = Array.Empty<object>() };

        var callers = CollectRows(result);
        return new { success = true, targetSymbol = symbolName, depth, count = callers.Count, callers };
    }

    private static List<object> CollectRows(BogDb.Core.Main.QueryResult.QueryResult result)
    {
        var rows = new List<object>();
        while (result.HasNext())
        {
            var row = result.GetNext().GetAsDictionary();
            rows.Add(new
            {
                id            = row.GetValueOrDefault("id")?.ToString(),
                name          = row.GetValueOrDefault("name")?.ToString(),
                qualifiedName = row.GetValueOrDefault("qualifiedName")?.ToString(),
                kind          = row.GetValueOrDefault("kind")?.ToString(),
                signature     = row.GetValueOrDefault("signature")?.ToString(),
                filePath      = row.GetValueOrDefault("filePath")?.ToString(),
            });
        }
        return rows;
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetOptionalInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
