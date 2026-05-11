using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_boundary_analysis — What boundaries does this symbol or file cross?
/// Returns boundary interactions (db, http, filesystem, queue, cache, auth, ...)
/// with operation types and responsibility summary.
/// </summary>
public static class BoundaryAnalysisTool
{
    public const string Name = "codegen_boundary_analysis";
    public const string Description =
        "Analyze what execution boundaries a file or symbol crosses (database, HTTP, filesystem, queue, cache, auth, etc.). " +
        "Returns boundary interactions with operation types and a responsibility summary. " +
        "Answers: 'Does this function touch the DB? Does it make HTTP calls? How many boundary types does it cross?'";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            filePath   = new { type = "string", description = "File path (full or partial) to analyze" },
            symbolName = new { type = "string", description = "Symbol name to analyze (alternative to filePath)" },
        },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var filePath   = GetOptionalString(arguments, "filePath");
        var symbolName = GetOptionalString(arguments, "symbolName");

        if (string.IsNullOrEmpty(filePath) && string.IsNullOrEmpty(symbolName))
            return new { success = false, error = "Provide either filePath or symbolName." };

        // 1. Get boundary interactions for the file
        string boundaryCypher;
        if (!string.IsNullOrEmpty(filePath))
        {
            boundaryCypher = $@"
                MATCH (f:File)-[:CROSSES_BOUNDARY]->(bi:BoundaryInteraction)
                WHERE f.path CONTAINS '{Escape(filePath)}'
                RETURN bi.boundary_type AS boundaryType, bi.operation_type AS operationType,
                       bi.target_name AS targetName, bi.effect_mode AS effectMode,
                       bi.confidence AS confidence, f.path AS filePath
                ORDER BY bi.boundary_type, bi.operation_type";
        }
        else
        {
            boundaryCypher = $@"
                MATCH (f:File)-[:DEFINES_SYMBOL]->(s:Symbol)
                WHERE s.qualifiedName CONTAINS '{Escape(symbolName!)}'
                   OR s.name = '{Escape(symbolName!)}'
                WITH f LIMIT 1
                MATCH (f)-[:CROSSES_BOUNDARY]->(bi:BoundaryInteraction)
                RETURN bi.boundary_type AS boundaryType, bi.operation_type AS operationType,
                       bi.target_name AS targetName, bi.effect_mode AS effectMode,
                       bi.confidence AS confidence, f.path AS filePath
                ORDER BY bi.boundary_type, bi.operation_type";
        }

        var result = conn.Query(boundaryCypher);
        var boundaries = new List<object>();
        var boundaryTypes = new HashSet<string>();
        string? resolvedFile = null;

        if (result.IsSuccess)
        {
            while (result.HasNext())
            {
                var row = result.GetNext().GetAsDictionary();
                var bt = row.GetValueOrDefault("boundaryType")?.ToString() ?? "";
                boundaryTypes.Add(bt);
                resolvedFile ??= row.GetValueOrDefault("filePath")?.ToString();
                boundaries.Add(new
                {
                    boundaryType  = bt,
                    operationType = row.GetValueOrDefault("operationType")?.ToString(),
                    targetName    = row.GetValueOrDefault("targetName")?.ToString(),
                    effectMode    = row.GetValueOrDefault("effectMode")?.ToString(),
                    confidence    = row.GetValueOrDefault("confidence"),
                });
            }
        }

        // 2. Get responsibility profile if available
        object? responsibilityProfile = null;
        if (!string.IsNullOrEmpty(resolvedFile))
        {
            var rpCypher = $@"
                MATCH (f:File)-[:HAS_RESPONSIBILITY_PROFILE]->(rp:ResponsibilityProfile)
                WHERE f.path CONTAINS '{Escape(resolvedFile)}'
                RETURN rp.boundary_type_count AS boundaryTypeCount,
                       rp.dependency_category_count AS dependencyCategoryCount,
                       rp.side_effect_class_count AS sideEffectClassCount,
                       rp.responsibility_spread_score AS responsibilitySpreadScore,
                       rp.dominant_responsibilities_json AS dominantResponsibilities
                LIMIT 1";

            var rpResult = conn.Query(rpCypher);
            if (rpResult.IsSuccess && rpResult.HasNext())
            {
                var row = rpResult.GetNext().GetAsDictionary();
                responsibilityProfile = new
                {
                    boundaryTypeCount          = row.GetValueOrDefault("boundaryTypeCount"),
                    dependencyCategoryCount    = row.GetValueOrDefault("dependencyCategoryCount"),
                    sideEffectClassCount       = row.GetValueOrDefault("sideEffectClassCount"),
                    responsibilitySpreadScore  = row.GetValueOrDefault("responsibilitySpreadScore"),
                    dominantResponsibilities   = row.GetValueOrDefault("dominantResponsibilities")?.ToString(),
                };
            }
        }

        return new
        {
            success = true,
            query = !string.IsNullOrEmpty(filePath) ? $"file:{filePath}" : $"symbol:{symbolName}",
            filePath = resolvedFile,
            summary = new
            {
                boundaryCount     = boundaries.Count,
                boundaryTypeCount = boundaryTypes.Count,
                boundaryTypes     = boundaryTypes.OrderBy(b => b).ToList(),
            },
            boundaries,
            responsibilityProfile,
        };
    }

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
