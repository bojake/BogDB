using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BogDb.Core.Common;

/// <summary>
/// Normalizes raw CLR values emitted by external data sources (JSON, CSV, future connectors)
/// to canonical BogDb CLR representations before comparison, projection, or result extraction.
///
/// Canonical type mapping:
///   JSON integer  → long
///   JSON float    → double
///   JSON string   → string
///   JSON bool     → bool
///   JSON null     → null
///   int/short     → long   (widened to INT64)
///   float         → double (widened to DOUBLE)
///   Everything else → identity
///
/// C++ parity: equivalent to bogdb-cpp implicit type promotion in expression evaluation.
/// </summary>
public static class TypeCoercionHelper
{
    // ── Primary normalizer ────────────────────────────────────────────────────

    /// <summary>
    /// Returns a canonical CLR value for <paramref name="value"/>.
    /// Safe to call on already-canonical values — returns them unchanged.
    /// </summary>
    public static object? Normalize(object? value)
    {
        return value switch
        {
            null => null,

            // System.Text.Json — JsonNode subtypes
            JsonValue jv => NormalizeJsonValue(jv),
            JsonArray _ => value.ToString(),          // arrays → string representation
            JsonObject _ => value.ToString(),         // objects → string representation
            JsonNode jn => NormalizeJsonNode(jn),     // fallback for any other JsonNode

            // System.Text.Json — JsonElement (used by JsonSerializer / JsonDocument)
            JsonElement je => NormalizeJsonElement(je),

            // Narrow numeric types → widen to canonical BogDb types
            int i32 => (long)i32,
            short i16 => (long)i16,
            sbyte i8 => (long)i8,
            byte u8 => (long)u8,
            uint u32 => (long)u32,
            float f32 => (double)f32,
            decimal dec => (double)dec,

            // Canonical types — identity
            long _ => value,
            double _ => value,
            string _ => value,
            bool _ => value,

            // Unknown types — preserve as-is; let the downstream throw meaningful errors
            _ => value
        };
    }

    // ── Typed extraction helpers ──────────────────────────────────────────────

    /// <summary>Extracts an <see cref="long"/> from any value via coercion.</summary>
    public static long ToInt64(object? value)
    {
        var n = Normalize(value);
        return n switch
        {
            long l => l,
            double d => (long)d,
            string s when long.TryParse(s, out var parsed) => parsed,
            bool b => b ? 1L : 0L,
            _ => Convert.ToInt64(n)
        };
    }

    /// <summary>Extracts a <see cref="double"/> from any value via coercion.</summary>
    public static double ToDouble(object? value)
    {
        var n = Normalize(value);
        return n switch
        {
            double d => d,
            long l => (double)l,
            string s when double.TryParse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            bool b => b ? 1.0 : 0.0,
            _ => Convert.ToDouble(n)
        };
    }

    /// <summary>Extracts a <see cref="string"/> from any value.</summary>
    public static string? ToBogDbString(object? value)
    {
        var n = Normalize(value);
        return n switch
        {
            null => null,
            string s => s,
            Dictionary<string, object?> dict => FormatDictionary(dict.Select(kv =>
                new KeyValuePair<object?, object?>(kv.Key, kv.Value))),
            Dictionary<object, object?> dict => FormatDictionary(dict),
            IEnumerable<KeyValuePair<object?, object?>> pairs => FormatDictionary(pairs),
            IEnumerable<KeyValuePair<string, object?>> pairs => FormatDictionary(
                pairs.Select(kv => new KeyValuePair<object?, object?>(kv.Key, kv.Value))),
            IEnumerable list and not string => FormatList(list),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => n.ToString()
        };
    }

    /// <summary>Parses a runtime interval value from a CLR value or interval text.</summary>
    public static bool TryParseInterval(object? value, out BogDbInterval result)
    {
        var n = Normalize(value);
        switch (n)
        {
            case BogDbInterval interval:
                result = interval;
                return true;
            case string s:
                return BogDbInterval.TryParse(s, out result);
            default:
                result = default;
                return false;
        }
    }

    /// <summary>Extracts a <see cref="bool"/> from any value via coercion.</summary>
    public static bool ToBool(object? value)
    {
        var n = Normalize(value);
        return n switch
        {
            bool b => b,
            long l => l != 0,
            double d => d != 0.0,
            string s => s.Length > 0 && s != "false" && s != "0",
            _ => Convert.ToBoolean(n)
        };
    }

    // ── JsonNode helpers ──────────────────────────────────────────────────────

    private static object? NormalizeJsonValue(JsonValue jv)
    {
        // Try in order of most-common types
        if (jv.TryGetValue(out long l64))  return l64;
        if (jv.TryGetValue(out int i32))   return (long)i32;
        if (jv.TryGetValue(out double d))  return d;
        if (jv.TryGetValue(out float f))   return (double)f;
        if (jv.TryGetValue(out bool b))    return b;
        if (jv.TryGetValue(out string? s)) return s;
        // Fallback: serialise the raw JSON token
        return jv.ToJsonString();
    }

    private static object? NormalizeJsonNode(JsonNode jn)
    {
        // Try to determine scalar via the underlying element
        try
        {
            var el = jn.GetValue<JsonElement>();
            return NormalizeJsonElement(el);
        }
        catch
        {
            return jn.ToJsonString();
        }
    }

    private static object? NormalizeJsonElement(JsonElement je)
    {
        return je.ValueKind switch
        {
            JsonValueKind.Null      => null,
            JsonValueKind.True      => true,
            JsonValueKind.False     => false,
            JsonValueKind.Number    => NormalizeJsonNumber(je),
            JsonValueKind.String    => je.GetString(),
            _ => je.GetRawText()
        };
    }

    private static object NormalizeJsonNumber(JsonElement je)
    {
        if (je.TryGetInt64(out var l64))  return l64;
        if (je.TryGetDouble(out var dbl)) return dbl;
        return je.GetRawText();
    }

    private static string FormatList(IEnumerable values)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        var first = true;
        foreach (var value in values)
        {
            if (!first)
                sb.Append(',');
            sb.Append(FormatCompositeValue(value));
            first = false;
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string FormatDictionary(IEnumerable<KeyValuePair<object?, object?>> pairs)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        var first = true;
        foreach (var pair in pairs)
        {
            if (!first)
                sb.Append(',');
            sb.Append(JsonSerializer.Serialize(ToBogDbString(pair.Key) ?? string.Empty));
            sb.Append(':');
            sb.Append(FormatCompositeValue(pair.Value));
            first = false;
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string FormatCompositeValue(object? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            null => "null",
            string s => JsonSerializer.Serialize(s),
            Dictionary<string, object?> dict => FormatDictionary(dict.Select(kv =>
                new KeyValuePair<object?, object?>(kv.Key, kv.Value))),
            Dictionary<object, object?> dict => FormatDictionary(dict),
            IEnumerable<KeyValuePair<object?, object?>> pairs => FormatDictionary(pairs),
            IEnumerable<KeyValuePair<string, object?>> pairs => FormatDictionary(
                pairs.Select(kv => new KeyValuePair<object?, object?>(kv.Key, kv.Value))),
            IEnumerable list and not string => FormatList(list),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => normalized?.ToString() ?? string.Empty
        };
    }
}
