using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_symbol_neighborhood — The primary BO context tool.
/// Shows everything about a symbol's relationships: callers, callees, type dependencies,
/// contracts, boundaries, effect profile, complexity, and responsibility.
/// </summary>
public static class SymbolNeighborhoodTool
{
    public const string Name = "codegen_symbol_neighborhood";
    public const string Description =
        "Get the full BO-enriched neighborhood for a symbol: its contract, callers, callees, type dependencies, " +
        "boundary crossings, effect profile, complexity profile, and responsibility profile. " +
        "This is the primary tool for understanding the full context needed to safely edit a symbol.";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            symbolName = new { type = "string", description = "Qualified or simple name of the symbol" },
            depth      = new { type = "integer", description = "Caller/callee traversal depth (default 2)", minimum = 1, maximum = 5 },
        },
        required = new[] { "symbolName" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var symbolName = GetString(arguments, "symbolName");
        var depth      = GetOptionalInt(arguments, "depth") ?? 2;

        // 1. Symbol definition
        var symbolCypher = $@"
            MATCH (f:File)-[:DEFINES_SYMBOL]->(s:Symbol)
            WHERE s.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR s.name = '{Escape(symbolName)}'
            RETURN s.id AS id, s.name AS name, s.qualifiedName AS qualifiedName,
                   s.kind AS kind, s.signature AS signature,
                   s.startLine AS startLine, s.endLine AS endLine,
                   f.path AS filePath
            LIMIT 1";

        var symResult = conn.Query(symbolCypher);
        if (!symResult.IsSuccess || !symResult.HasNext())
            return new { success = false, error = $"Symbol not found: {symbolName}" };

        var symRow = symResult.GetNext().GetAsDictionary();
        var resolvedFile = symRow.GetValueOrDefault("filePath")?.ToString() ?? "";
        var symbolDef = new
        {
            id            = symRow.GetValueOrDefault("id")?.ToString(),
            name          = symRow.GetValueOrDefault("name")?.ToString(),
            qualifiedName = symRow.GetValueOrDefault("qualifiedName")?.ToString(),
            kind          = symRow.GetValueOrDefault("kind")?.ToString(),
            signature     = symRow.GetValueOrDefault("signature")?.ToString(),
            startLine     = symRow.GetValueOrDefault("startLine"),
            endLine       = symRow.GetValueOrDefault("endLine"),
            filePath      = resolvedFile,
        };

        // 2. Contract
        var contractCypher = $@"
            MATCH (s:Symbol)-[:HAS_CONTRACT]->(c:Contract)
            WHERE s.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR s.name = '{Escape(symbolName)}'
            RETURN c.input_types_json AS inputTypes, c.output_types_json AS outputTypes,
                   c.async_mode AS asyncMode, c.throws_or_error_modes_json AS errorModes,
                   c.confidence AS confidence
            LIMIT 1";

        var contractResult = conn.Query(contractCypher);
        object? contract = null;
        if (contractResult.IsSuccess && contractResult.HasNext())
        {
            var row = contractResult.GetNext().GetAsDictionary();
            contract = new
            {
                inputTypes  = row.GetValueOrDefault("inputTypes")?.ToString(),
                outputTypes = row.GetValueOrDefault("outputTypes")?.ToString(),
                asyncMode   = row.GetValueOrDefault("asyncMode")?.ToString(),
                errorModes  = row.GetValueOrDefault("errorModes")?.ToString(),
                confidence  = row.GetValueOrDefault("confidence"),
            };
        }

        // 3. Callers (via BO_CALLS)
        var callersCypher = $@"
            MATCH (target:Symbol)
            WHERE target.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR target.name = '{Escape(symbolName)}'
            WITH target LIMIT 1
            MATCH (caller:Symbol)-[:BO_CALLS*1..{depth}]->(target)
            MATCH (f:File)-[:DEFINES_SYMBOL]->(caller)
            RETURN DISTINCT caller.name AS name, caller.qualifiedName AS qualifiedName,
                   caller.kind AS kind, f.path AS filePath
            ORDER BY caller.qualifiedName
            LIMIT 50";

        var callers = CollectSymbols(conn.Query(callersCypher));

        // 4. Callees (via BO_CALLS)
        var calleesCypher = $@"
            MATCH (source:Symbol)
            WHERE source.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR source.name = '{Escape(symbolName)}'
            WITH source LIMIT 1
            MATCH (source)-[:BO_CALLS*1..{depth}]->(callee:Symbol)
            MATCH (f:File)-[:DEFINES_SYMBOL]->(callee)
            RETURN DISTINCT callee.name AS name, callee.qualifiedName AS qualifiedName,
                   callee.kind AS kind, f.path AS filePath
            ORDER BY callee.qualifiedName
            LIMIT 50";

        var callees = CollectSymbols(conn.Query(calleesCypher));

        // 5. Type dependencies (BO_USES_TYPE)
        var typeDepsCypher = $@"
            MATCH (source:Symbol)-[:BO_USES_TYPE]->(target:Symbol)
            WHERE source.qualifiedName CONTAINS '{Escape(symbolName)}'
               OR source.name = '{Escape(symbolName)}'
            RETURN DISTINCT target.name AS name, target.qualifiedName AS qualifiedName,
                   target.kind AS kind
            ORDER BY target.qualifiedName
            LIMIT 50";

        var typeDeps = CollectSymbols(conn.Query(typeDepsCypher));

        // 6. Boundary crossings for the file
        var boundCypher = $@"
            MATCH (f:File)-[:CROSSES_BOUNDARY]->(bi:BoundaryInteraction)
            WHERE f.path = '{Escape(resolvedFile)}'
            RETURN bi.boundary_type AS boundaryType, bi.operation_type AS operationType,
                   bi.target_name AS targetName
            ORDER BY bi.boundary_type";

        var boundResult = conn.Query(boundCypher);
        var boundaries = new List<object>();
        if (boundResult.IsSuccess)
        {
            while (boundResult.HasNext())
            {
                var row = boundResult.GetNext().GetAsDictionary();
                boundaries.Add(new
                {
                    boundaryType  = row.GetValueOrDefault("boundaryType")?.ToString(),
                    operationType = row.GetValueOrDefault("operationType")?.ToString(),
                    targetName    = row.GetValueOrDefault("targetName")?.ToString(),
                });
            }
        }

        // 7. Effect profile for the file
        var effectCypher = $@"
            MATCH (f:File)-[:HAS_EFFECT_PROFILE]->(ep:EffectProfile)
            WHERE f.path = '{Escape(resolvedFile)}'
            RETURN ep.reads_state AS readsState, ep.writes_state AS writesState,
                   ep.emits_events AS emitsEvents, ep.calls_external_service AS callsExternalService,
                   ep.has_auth_logic AS hasAuthLogic, ep.has_validation_logic AS hasValidationLogic,
                   ep.side_effect_classes_json AS sideEffectClasses
            LIMIT 1";

        var effectResult = conn.Query(effectCypher);
        object? effectProfile = null;
        if (effectResult.IsSuccess && effectResult.HasNext())
        {
            var row = effectResult.GetNext().GetAsDictionary();
            effectProfile = new
            {
                readsState           = row.GetValueOrDefault("readsState"),
                writesState          = row.GetValueOrDefault("writesState"),
                emitsEvents          = row.GetValueOrDefault("emitsEvents"),
                callsExternalService = row.GetValueOrDefault("callsExternalService"),
                hasAuthLogic         = row.GetValueOrDefault("hasAuthLogic"),
                hasValidationLogic   = row.GetValueOrDefault("hasValidationLogic"),
                sideEffectClasses    = row.GetValueOrDefault("sideEffectClasses")?.ToString(),
            };
        }

        // 8. Complexity profile for the file
        var complexCypher = $@"
            MATCH (f:File)-[:HAS_COMPLEXITY_PROFILE]->(cp:ComplexityProfile)
            WHERE f.path = '{Escape(resolvedFile)}'
            RETURN cp.loc AS loc, cp.cognitive_complexity AS cognitiveComplexity,
                   cp.cyclomatic_complexity AS cyclomaticComplexity,
                   cp.nesting_depth AS nestingDepth, cp.fan_in AS fanIn, cp.fan_out AS fanOut
            LIMIT 1";

        var complexResult = conn.Query(complexCypher);
        object? complexityProfile = null;
        if (complexResult.IsSuccess && complexResult.HasNext())
        {
            var row = complexResult.GetNext().GetAsDictionary();
            complexityProfile = new
            {
                loc                  = row.GetValueOrDefault("loc"),
                cognitiveComplexity  = row.GetValueOrDefault("cognitiveComplexity"),
                cyclomaticComplexity = row.GetValueOrDefault("cyclomaticComplexity"),
                nestingDepth         = row.GetValueOrDefault("nestingDepth"),
                fanIn                = row.GetValueOrDefault("fanIn"),
                fanOut               = row.GetValueOrDefault("fanOut"),
            };
        }

        // 9. Responsibility profile for the file
        var respCypher = $@"
            MATCH (f:File)-[:HAS_RESPONSIBILITY_PROFILE]->(rp:ResponsibilityProfile)
            WHERE f.path = '{Escape(resolvedFile)}'
            RETURN rp.responsibility_spread_score AS spreadScore,
                   rp.dominant_responsibilities_json AS dominantResponsibilities,
                   rp.boundary_type_count AS boundaryTypeCount
            LIMIT 1";

        var respResult = conn.Query(respCypher);
        object? responsibilityProfile = null;
        if (respResult.IsSuccess && respResult.HasNext())
        {
            var row = respResult.GetNext().GetAsDictionary();
            responsibilityProfile = new
            {
                spreadScore              = row.GetValueOrDefault("spreadScore"),
                dominantResponsibilities = row.GetValueOrDefault("dominantResponsibilities")?.ToString(),
                boundaryTypeCount        = row.GetValueOrDefault("boundaryTypeCount"),
            };
        }

        // Compute affected file set for context burden estimate
        var affectedFiles = new HashSet<string>();
        if (!string.IsNullOrEmpty(resolvedFile)) affectedFiles.Add(resolvedFile);
        foreach (var c in callers)
            if (c is { } obj && GetFilePath(obj) is { } fp)
                affectedFiles.Add(fp);
        foreach (var c in callees)
            if (c is { } obj && GetFilePath(obj) is { } fp)
                affectedFiles.Add(fp);

        return new
        {
            success = true,
            symbol = symbolDef,
            contract,
            neighborhood = new
            {
                callerCount     = callers.Count,
                calleeCount     = callees.Count,
                typeDependencyCount = typeDeps.Count,
                boundaryCount   = boundaries.Count,
                estimatedSafeEditFiles = affectedFiles.Count,
            },
            callers,
            callees,
            typeDependencies = typeDeps,
            boundaries,
            effectProfile,
            complexityProfile,
            responsibilityProfile,
        };
    }

    private static List<object> CollectSymbols(BogDb.Core.Main.QueryResult.QueryResult result)
    {
        var items = new List<object>();
        if (!result.IsSuccess) return items;
        while (result.HasNext())
        {
            var row = result.GetNext().GetAsDictionary();
            items.Add(new
            {
                name          = row.GetValueOrDefault("name")?.ToString(),
                qualifiedName = row.GetValueOrDefault("qualifiedName")?.ToString(),
                kind          = row.GetValueOrDefault("kind")?.ToString(),
                filePath      = row.GetValueOrDefault("filePath")?.ToString(),
            });
        }
        return items;
    }

    private static string? GetFilePath(object obj)
    {
        var type = obj.GetType();
        var prop = type.GetProperty("filePath");
        return prop?.GetValue(obj)?.ToString();
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static int? GetOptionalInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
