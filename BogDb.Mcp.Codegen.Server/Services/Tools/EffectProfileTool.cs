using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_effect_profile — What side effects does this file or symbol have?
/// Returns the BO-derived effect profile (reads_state, writes_state, emits_events, etc.).
/// </summary>
public static class EffectProfileTool
{
    public const string Name = "codegen_effect_profile";
    public const string Description =
        "Get the side-effect profile for a file: does it read/write state, emit events, call external services, " +
        "have retry/transaction/auth/validation/caching/logging logic? " +
        "Answers: 'Is this function pure? Does it have auth logic? What side effects are present?'";

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
                MATCH (f:File)-[:HAS_EFFECT_PROFILE]->(ep:EffectProfile)
                WHERE f.path CONTAINS '{Escape(filePath)}'
                RETURN ep.reads_state AS readsState, ep.writes_state AS writesState,
                       ep.emits_events AS emitsEvents, ep.calls_external_service AS callsExternalService,
                       ep.mutates_input AS mutatesInput, ep.has_retry_logic AS hasRetryLogic,
                       ep.has_transaction_logic AS hasTransactionLogic, ep.has_auth_logic AS hasAuthLogic,
                       ep.has_validation_logic AS hasValidationLogic, ep.has_caching_logic AS hasCachingLogic,
                       ep.has_logging_logic AS hasLoggingLogic,
                       ep.side_effect_classes_json AS sideEffectClasses,
                       ep.confidence AS confidence, f.path AS filePath
                LIMIT 1";
        }
        else
        {
            cypher = $@"
                MATCH (f:File)-[:DEFINES_SYMBOL]->(s:Symbol)
                WHERE s.qualifiedName CONTAINS '{Escape(symbolName!)}'
                   OR s.name = '{Escape(symbolName!)}'
                WITH f LIMIT 1
                MATCH (f)-[:HAS_EFFECT_PROFILE]->(ep:EffectProfile)
                RETURN ep.reads_state AS readsState, ep.writes_state AS writesState,
                       ep.emits_events AS emitsEvents, ep.calls_external_service AS callsExternalService,
                       ep.mutates_input AS mutatesInput, ep.has_retry_logic AS hasRetryLogic,
                       ep.has_transaction_logic AS hasTransactionLogic, ep.has_auth_logic AS hasAuthLogic,
                       ep.has_validation_logic AS hasValidationLogic, ep.has_caching_logic AS hasCachingLogic,
                       ep.has_logging_logic AS hasLoggingLogic,
                       ep.side_effect_classes_json AS sideEffectClasses,
                       ep.confidence AS confidence, f.path AS filePath
                LIMIT 1";
        }

        var result = conn.Query(cypher);
        if (!result.IsSuccess || !result.HasNext())
            return new { success = false, error = "No effect profile found. Has this repo been indexed with BO?" };

        var row = result.GetNext().GetAsDictionary();

        return new
        {
            success  = true,
            filePath = row.GetValueOrDefault("filePath")?.ToString(),
            effectProfile = new
            {
                readsState           = row.GetValueOrDefault("readsState"),
                writesState          = row.GetValueOrDefault("writesState"),
                emitsEvents          = row.GetValueOrDefault("emitsEvents"),
                callsExternalService = row.GetValueOrDefault("callsExternalService"),
                mutatesInput         = row.GetValueOrDefault("mutatesInput"),
                hasRetryLogic        = row.GetValueOrDefault("hasRetryLogic"),
                hasTransactionLogic  = row.GetValueOrDefault("hasTransactionLogic"),
                hasAuthLogic         = row.GetValueOrDefault("hasAuthLogic"),
                hasValidationLogic   = row.GetValueOrDefault("hasValidationLogic"),
                hasCachingLogic      = row.GetValueOrDefault("hasCachingLogic"),
                hasLoggingLogic      = row.GetValueOrDefault("hasLoggingLogic"),
                sideEffectClasses    = row.GetValueOrDefault("sideEffectClasses")?.ToString(),
                confidence           = row.GetValueOrDefault("confidence"),
            },
        };
    }

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
