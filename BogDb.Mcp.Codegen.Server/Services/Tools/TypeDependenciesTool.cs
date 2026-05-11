using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_type_dependencies — What types does this symbol depend on?
/// Returns symbols referenced via BO_USES_TYPE, BO_CALLS, BO_INSTANTIATES edges.
/// </summary>
public static class TypeDependenciesTool
{
    public const string Name = "codegen_type_dependencies";
    public const string Description =
        "Find the type dependencies for a symbol: what types it uses, what functions it calls, and " +
        "what classes it instantiates (from BO's tree-sitter-resolved dependency edges). " +
        "Answers: 'What domain model symbols are touched by this service/adaptor?'";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            symbolName = new { type = "string", description = "Qualified or simple name of the symbol" },
            limit      = new { type = "integer", description = "Max results per edge type (default 50)", minimum = 1, maximum = 200 },
        },
        required = new[] { "symbolName" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var symbolName = GetString(arguments, "symbolName");
        var limit      = GetOptionalInt(arguments, "limit") ?? 50;

        // USES_TYPE edges
        var usesTypeCypher = $@"
            MATCH (source:Symbol)-[r:BO_USES_TYPE]->(target:Symbol)
            WHERE source.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR source.name = '{Escape(symbolName)}'
            MATCH (f:File)-[:DEFINES_SYMBOL]->(target)
            RETURN target.name AS name, target.qualifiedName AS qualifiedName,
                   target.kind AS kind, f.path AS filePath,
                   r.evidence AS evidence, r.confidence AS confidence
            ORDER BY target.qualifiedName
            LIMIT {limit}";

        var usesTypeResult = conn.Query(usesTypeCypher);
        var usesType = CollectDeps(usesTypeResult);

        // CALLS edges
        var callsCypher = $@"
            MATCH (source:Symbol)-[r:BO_CALLS]->(target:Symbol)
            WHERE source.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR source.name = '{Escape(symbolName)}'
            MATCH (f:File)-[:DEFINES_SYMBOL]->(target)
            RETURN target.name AS name, target.qualifiedName AS qualifiedName,
                   target.kind AS kind, f.path AS filePath,
                   r.evidence AS evidence, r.confidence AS confidence
            ORDER BY target.qualifiedName
            LIMIT {limit}";

        var callsResult = conn.Query(callsCypher);
        var calls = CollectDeps(callsResult);

        // INSTANTIATES edges
        var instantiatesCypher = $@"
            MATCH (source:Symbol)-[r:BO_INSTANTIATES]->(target:Symbol)
            WHERE source.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR source.name = '{Escape(symbolName)}'
            MATCH (f:File)-[:DEFINES_SYMBOL]->(target)
            RETURN target.name AS name, target.qualifiedName AS qualifiedName,
                   target.kind AS kind, f.path AS filePath,
                   r.evidence AS evidence, r.confidence AS confidence
            ORDER BY target.qualifiedName
            LIMIT {limit}";

        var instantiatesResult = conn.Query(instantiatesCypher);
        var instantiates = CollectDeps(instantiatesResult);

        // Reverse: who depends on this symbol?
        var reverseCallersCypher = $@"
            MATCH (caller:Symbol)-[r:BO_CALLS]->(target:Symbol)
            WHERE target.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR target.name = '{Escape(symbolName)}'
            MATCH (f:File)-[:DEFINES_SYMBOL]->(caller)
            RETURN caller.name AS name, caller.qualifiedName AS qualifiedName,
                   caller.kind AS kind, f.path AS filePath,
                   r.evidence AS evidence, r.confidence AS confidence
            ORDER BY caller.qualifiedName
            LIMIT {limit}";

        var reverseResult = conn.Query(reverseCallersCypher);
        var calledBy = CollectDeps(reverseResult);

        return new
        {
            success = true,
            symbol = symbolName,
            summary = new
            {
                usesTypeCount      = usesType.Count,
                callsCount         = calls.Count,
                instantiatesCount  = instantiates.Count,
                calledByCount      = calledBy.Count,
                totalDependencies  = usesType.Count + calls.Count + instantiates.Count,
            },
            usesType,
            calls,
            instantiates,
            calledBy,
        };
    }

    private static List<object> CollectDeps(BogDb.Core.Main.QueryResult.QueryResult result)
    {
        var deps = new List<object>();
        if (!result.IsSuccess) return deps;
        while (result.HasNext())
        {
            var row = result.GetNext().GetAsDictionary();
            deps.Add(new
            {
                name          = row.GetValueOrDefault("name")?.ToString(),
                qualifiedName = row.GetValueOrDefault("qualifiedName")?.ToString(),
                kind          = row.GetValueOrDefault("kind")?.ToString(),
                filePath      = row.GetValueOrDefault("filePath")?.ToString(),
                evidence      = row.GetValueOrDefault("evidence")?.ToString(),
                confidence    = row.GetValueOrDefault("confidence"),
            });
        }
        return deps;
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static int? GetOptionalInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
