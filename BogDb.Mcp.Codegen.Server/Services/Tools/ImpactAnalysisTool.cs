using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_impact_analysis — Given a symbol, determine the blast radius:
/// affected symbols, files, services, and deploy units.
/// </summary>
public static class ImpactAnalysisTool
{
    public const string Name = "codegen_impact_analysis";
    public const string Description =
        "Analyze the impact of changing a symbol. Returns affected symbols (transitive callers), " +
        "their files, owning services, and deploy units. Answers: 'If I change this type, what breaks?'";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            symbolName = new { type = "string", description = "Qualified or simple name of the symbol to analyze" },
            depth      = new { type = "integer", description = "Max caller traversal depth (default 5)", minimum = 1, maximum = 10 },
        },
        required = new[] { "symbolName" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var symbolName = GetString(arguments, "symbolName");
        var depth      = GetOptionalInt(arguments, "depth") ?? 5;

        // 1. Find affected symbols (transitive callers)
        var symbolCypher = $@"
            MATCH (target:Symbol)
            WHERE target.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR target.name = '{Escape(symbolName)}'
            WITH target LIMIT 1
            MATCH (caller:Symbol)-[:REFERENCES_SYMBOL*1..{depth}]->(target)
            MATCH (f:File)-[:DEFINES_SYMBOL]->(caller)
            RETURN DISTINCT caller.id AS id, caller.name AS name, caller.qualifiedName AS qualifiedName,
                   caller.kind AS kind, f.path AS filePath
            ORDER BY caller.qualifiedName
            LIMIT 200";

        var symbolResult = conn.Query(symbolCypher);
        var affectedSymbols = new List<object>();
        var affectedFiles = new HashSet<string>();

        if (symbolResult.IsSuccess)
        {
            while (symbolResult.HasNext())
            {
                var row = symbolResult.GetNext().GetAsDictionary();
                affectedSymbols.Add(new
                {
                    id            = row.GetValueOrDefault("id")?.ToString(),
                    name          = row.GetValueOrDefault("name")?.ToString(),
                    qualifiedName = row.GetValueOrDefault("qualifiedName")?.ToString(),
                    kind          = row.GetValueOrDefault("kind")?.ToString(),
                    filePath      = row.GetValueOrDefault("filePath")?.ToString(),
                });
                var fp = row.GetValueOrDefault("filePath")?.ToString();
                if (!string.IsNullOrEmpty(fp)) affectedFiles.Add(fp);
            }
        }

        // 2. Find affected services (services whose repos contain affected files)
        var serviceCypher = $@"
            MATCH (target:Symbol)
            WHERE target.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR target.name = '{Escape(symbolName)}'
            WITH target LIMIT 1
            MATCH (caller:Symbol)-[:REFERENCES_SYMBOL*1..{depth}]->(target)
            MATCH (f:File)-[:DEFINES_SYMBOL]->(caller)
            MATCH (:Module)-[:CONTAINS_FILE]->(f)
            MATCH (pkg:Package)-[:CONTAINS_MODULE]->(:Module)-[:CONTAINS_FILE]->(f)
            MATCH (r:Repo)-[:CONTAINS_PACKAGE]->(pkg)
            OPTIONAL MATCH (svc:Service)-[:DEPLOYED_AS]->(du:DeployUnit)
            WHERE du.repo = r.name
            RETURN DISTINCT svc.name AS serviceName, du.name AS deployUnit, r.name AS repo
            LIMIT 50";

        var serviceResult = conn.Query(serviceCypher);
        var affectedServices = new List<object>();
        if (serviceResult.IsSuccess)
        {
            while (serviceResult.HasNext())
            {
                var row = serviceResult.GetNext().GetAsDictionary();
                var svcName = row.GetValueOrDefault("serviceName")?.ToString();
                if (!string.IsNullOrEmpty(svcName))
                {
                    affectedServices.Add(new
                    {
                        service    = svcName,
                        deployUnit = row.GetValueOrDefault("deployUnit")?.ToString(),
                        repo       = row.GetValueOrDefault("repo")?.ToString(),
                    });
                }
            }
        }

        return new
        {
            success = true,
            targetSymbol = symbolName,
            summary = new
            {
                affectedSymbolCount = affectedSymbols.Count,
                affectedFileCount   = affectedFiles.Count,
                affectedServiceCount = affectedServices.Count,
            },
            affectedSymbols,
            affectedFiles = affectedFiles.OrderBy(f => f).ToList(),
            affectedServices,
        };
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static int? GetOptionalInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
