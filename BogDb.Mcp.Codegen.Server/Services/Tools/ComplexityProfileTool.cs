using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_complexity_profile — How complex is this file?
/// Returns the BO-derived complexity profile (cognitive, cyclomatic, nesting, fan-in/out).
/// </summary>
public static class ComplexityProfileTool
{
    public const string Name = "codegen_complexity_profile";
    public const string Description =
        "Get the complexity profile for a file: cognitive complexity, cyclomatic complexity, nesting depth, " +
        "parameter count, branch count, side-effect count, fan-in, and fan-out. " +
        "Answers: 'Is this file a hotspot? What's the cognitive load?'";

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
                MATCH (f:File)-[:HAS_COMPLEXITY_PROFILE]->(cp:ComplexityProfile)
                WHERE f.path CONTAINS '{Escape(filePath)}'
                RETURN cp.loc AS loc, cp.cognitive_complexity AS cognitiveComplexity,
                       cp.cyclomatic_complexity AS cyclomaticComplexity,
                       cp.nesting_depth AS nestingDepth, cp.parameter_count AS parameterCount,
                       cp.branch_count AS branchCount, cp.side_effect_count AS sideEffectCount,
                       cp.fan_in AS fanIn, cp.fan_out AS fanOut,
                       cp.confidence AS confidence, f.path AS filePath
                LIMIT 1";
        }
        else
        {
            cypher = $@"
                MATCH (f:File)-[:DEFINES_SYMBOL]->(s:Symbol)
                WHERE s.qualifiedName CONTAINS '{Escape(symbolName!)}'
                   OR s.name = '{Escape(symbolName!)}'
                WITH f LIMIT 1
                MATCH (f)-[:HAS_COMPLEXITY_PROFILE]->(cp:ComplexityProfile)
                RETURN cp.loc AS loc, cp.cognitive_complexity AS cognitiveComplexity,
                       cp.cyclomatic_complexity AS cyclomaticComplexity,
                       cp.nesting_depth AS nestingDepth, cp.parameter_count AS parameterCount,
                       cp.branch_count AS branchCount, cp.side_effect_count AS sideEffectCount,
                       cp.fan_in AS fanIn, cp.fan_out AS fanOut,
                       cp.confidence AS confidence, f.path AS filePath
                LIMIT 1";
        }

        var result = conn.Query(cypher);
        if (!result.IsSuccess || !result.HasNext())
            return new { success = false, error = "No complexity profile found. Has this repo been indexed with BO?" };

        var row = result.GetNext().GetAsDictionary();

        return new
        {
            success  = true,
            filePath = row.GetValueOrDefault("filePath")?.ToString(),
            complexityProfile = new
            {
                loc                  = row.GetValueOrDefault("loc"),
                cognitiveComplexity  = row.GetValueOrDefault("cognitiveComplexity"),
                cyclomaticComplexity = row.GetValueOrDefault("cyclomaticComplexity"),
                nestingDepth         = row.GetValueOrDefault("nestingDepth"),
                parameterCount       = row.GetValueOrDefault("parameterCount"),
                branchCount          = row.GetValueOrDefault("branchCount"),
                sideEffectCount      = row.GetValueOrDefault("sideEffectCount"),
                fanIn                = row.GetValueOrDefault("fanIn"),
                fanOut               = row.GetValueOrDefault("fanOut"),
                confidence           = row.GetValueOrDefault("confidence"),
            },
        };
    }

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
