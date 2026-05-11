using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_feature_coverage — Find all symbols, files, and services gated by a feature flag.
/// </summary>
public static class FeatureCoverageTool
{
    public const string Name = "codegen_feature_coverage";
    public const string Description =
        "Find all code paths gated by a feature flag: symbols that reference the flag, " +
        "their files, and owning services. Answers: 'What code is behind enable_v2_payments?'";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            flagKey = new { type = "string", description = "Feature flag key (e.g. 'enable_v2_payments')" },
        },
        required = new[] { "flagKey" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var flagKey = GetString(arguments, "flagKey");

        // 1. Find the flag
        var flagCypher = $@"
            MATCH (ff:FeatureFlag)
            WHERE ff.key = '{Escape(flagKey)}'
            RETURN ff.id AS id, ff.key AS key, ff.provider AS provider,
                   ff.defaultState AS defaultState, ff.segments AS segments
            LIMIT 1";

        var flagResult = conn.Query(flagCypher);
        object? flagInfo = null;
        if (flagResult.IsSuccess && flagResult.HasNext())
        {
            var row = flagResult.GetNext().GetAsDictionary();
            flagInfo = new
            {
                id           = row.GetValueOrDefault("id")?.ToString(),
                key          = row.GetValueOrDefault("key")?.ToString(),
                provider     = row.GetValueOrDefault("provider")?.ToString(),
                defaultState = row.GetValueOrDefault("defaultState")?.ToString(),
                segments     = row.GetValueOrDefault("segments")?.ToString(),
            };
        }

        if (flagInfo == null)
            return new { success = false, error = $"Feature flag '{flagKey}' not found." };

        // 2. Find gated symbols
        var symbolCypher = $@"
            MATCH (s:Symbol)-[:GATED_BY]->(ff:FeatureFlag)
            WHERE ff.key = '{Escape(flagKey)}'
            MATCH (f:File)-[:DEFINES_SYMBOL]->(s)
            RETURN s.name AS symbolName, s.qualifiedName AS qualifiedName, s.kind AS kind,
                   f.path AS filePath
            ORDER BY s.qualifiedName
            LIMIT 200";

        var symbolResult = conn.Query(symbolCypher);
        var gatedSymbols = new List<object>();
        var gatedFiles = new HashSet<string>();

        if (symbolResult.IsSuccess)
        {
            while (symbolResult.HasNext())
            {
                var row = symbolResult.GetNext().GetAsDictionary();
                var fp = row.GetValueOrDefault("filePath")?.ToString();
                gatedSymbols.Add(new
                {
                    symbolName    = row.GetValueOrDefault("symbolName")?.ToString(),
                    qualifiedName = row.GetValueOrDefault("qualifiedName")?.ToString(),
                    kind          = row.GetValueOrDefault("kind")?.ToString(),
                    filePath      = fp,
                });
                if (!string.IsNullOrEmpty(fp)) gatedFiles.Add(fp);
            }
        }

        return new
        {
            success = true,
            flag = flagInfo,
            summary = new
            {
                gatedSymbolCount = gatedSymbols.Count,
                gatedFileCount   = gatedFiles.Count,
            },
            gatedSymbols,
            gatedFiles = gatedFiles.OrderBy(f => f).ToList(),
        };
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
