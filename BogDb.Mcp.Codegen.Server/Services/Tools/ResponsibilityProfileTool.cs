using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_responsibility_profile — How many responsibilities does this file have?
/// Returns the BO-derived responsibility profile with spread score and dominant roles.
/// </summary>
public static class ResponsibilityProfileTool
{
    public const string Name = "codegen_responsibility_profile";
    public const string Description =
        "Get the responsibility profile for a file: boundary type count, dependency categories, " +
        "side-effect classes, responsibility spread score, and dominant responsibilities (validation, persistence, " +
        "transport, orchestration, policy, mapping, security, caching, auditing). " +
        "Answers: 'Is this a god-module? Should it be split? What kinds of work is it doing?'";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            filePath   = new { type = "string", description = "File path (full or partial)" },
            symbolName = new { type = "string", description = "Symbol name (alternative to filePath)" },
        },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var filePath   = GetOptionalString(arguments, "filePath");
        var symbolName = GetOptionalString(arguments, "symbolName");

        if (string.IsNullOrEmpty(filePath) && string.IsNullOrEmpty(symbolName))
            return new { success = false, error = "Provide either filePath or symbolName." };

        string cypher;
        if (!string.IsNullOrEmpty(filePath))
        {
            cypher = $@"
                MATCH (f:File)-[:HAS_RESPONSIBILITY_PROFILE]->(rp:ResponsibilityProfile)
                WHERE f.path CONTAINS '{Escape(filePath)}'
                RETURN rp.boundary_type_count AS boundaryTypeCount,
                       rp.dependency_category_count AS dependencyCategoryCount,
                       rp.capability_cluster_count AS capabilityClusterCount,
                       rp.side_effect_class_count AS sideEffectClassCount,
                       rp.responsibility_spread_score AS responsibilitySpreadScore,
                       rp.dominant_responsibilities_json AS dominantResponsibilities,
                       rp.confidence AS confidence, f.path AS filePath
                LIMIT 1";
        }
        else
        {
            cypher = $@"
                MATCH (f:File)-[:DEFINES_SYMBOL]->(s:Symbol)
                WHERE s.qualifiedName CONTAINS '{Escape(symbolName!)}'
                   OR s.name = '{Escape(symbolName!)}'
                WITH f LIMIT 1
                MATCH (f)-[:HAS_RESPONSIBILITY_PROFILE]->(rp:ResponsibilityProfile)
                RETURN rp.boundary_type_count AS boundaryTypeCount,
                       rp.dependency_category_count AS dependencyCategoryCount,
                       rp.capability_cluster_count AS capabilityClusterCount,
                       rp.side_effect_class_count AS sideEffectClassCount,
                       rp.responsibility_spread_score AS responsibilitySpreadScore,
                       rp.dominant_responsibilities_json AS dominantResponsibilities,
                       rp.confidence AS confidence, f.path AS filePath
                LIMIT 1";
        }

        var result = conn.Query(cypher);
        if (!result.IsSuccess || !result.HasNext())
            return new { success = false, error = "No responsibility profile found. Has this repo been indexed with BO?" };

        var row = result.GetNext().GetAsDictionary();
        var spreadScore = Convert.ToDouble(row.GetValueOrDefault("responsibilitySpreadScore") ?? 0.0);

        return new
        {
            success  = true,
            filePath = row.GetValueOrDefault("filePath")?.ToString(),
            responsibilityProfile = new
            {
                boundaryTypeCount        = row.GetValueOrDefault("boundaryTypeCount"),
                dependencyCategoryCount  = row.GetValueOrDefault("dependencyCategoryCount"),
                capabilityClusterCount   = row.GetValueOrDefault("capabilityClusterCount"),
                sideEffectClassCount     = row.GetValueOrDefault("sideEffectClassCount"),
                responsibilitySpreadScore = spreadScore,
                dominantResponsibilities = row.GetValueOrDefault("dominantResponsibilities")?.ToString(),
                confidence               = row.GetValueOrDefault("confidence"),
            },
            assessment = spreadScore >= 4.0 ? "HIGH — strong candidate for decomposition"
                       : spreadScore >= 2.0 ? "MODERATE — monitor for growth"
                       : "LOW — focused responsibilities",
        };
    }

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
