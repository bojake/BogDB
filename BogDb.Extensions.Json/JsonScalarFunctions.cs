using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BogDb.Extensions.Json;

/// <summary>
/// JSON scalar functions — C++ parity with bogdb-master/extension/json.
/// Uses System.Text.Json for all parsing/generation.
/// </summary>
internal static class JsonScalarFunctions
{
    // ── json_valid(json_string) → BOOL ────────────────────────────────────────
    public static object? JsonValid(object?[] args)
    {
        if (args.Length == 0 || args[0] is null) return null;
        var s = args[0]?.ToString();
        if (s == null) return null;
        try { using var _ = JsonDocument.Parse(s); return true; }
        catch (JsonException) { return false; }
    }

    // ── json_array_length(json_string) → INT64 ───────────────────────────────
    public static object? JsonArrayLength(object?[] args)
    {
        if (args.Length == 0 || args[0] is null) return 0L;
        var s = args[0]?.ToString();
        if (s == null) return 0L;
        try
        {
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? (long)doc.RootElement.GetArrayLength() : 0L;
        }
        catch { return 0L; }
    }

    // ── json_type(json_string) → STRING ──────────────────────────────────────
    public static object? JsonType(object?[] args)
    {
        if (args.Length == 0 || args[0] is null) return null;
        var s = args[0]?.ToString();
        if (s == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(s);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Object => "OBJECT",
                JsonValueKind.Array  => "ARRAY",
                JsonValueKind.String => "VARCHAR",
                JsonValueKind.Number => doc.RootElement.TryGetInt64(out _) ? "UBIGINT" : "DOUBLE",
                JsonValueKind.True   => "BOOLEAN",
                JsonValueKind.False  => "BOOLEAN",
                JsonValueKind.Null   => "NULL",
                _                    => "UNKNOWN"
            };
        }
        catch { return null; }
    }

    // ── json_extract(json_string, path) → value ──────────────────────────────
    public static object? JsonExtract(object?[] args)
    {
        if (args.Length < 2 || args[0] is null || args[1] is null) return null;
        var json = args[0]?.ToString();
        var path = args[1]?.ToString();
        if (json == null || path == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement;
            var segments = path.Split(new[] { '.', '$', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
                {
                    if (index >= 0 && index < current.GetArrayLength())
                        current = current[index];
                    else return null;
                }
                else if (current.ValueKind == JsonValueKind.Object)
                {
                    if (current.TryGetProperty(segment, out var prop))
                        current = prop;
                    else return null;
                }
                else return null;
            }
            return ElementToValue(current);
        }
        catch { return null; }
    }

    // ── json_keys(json_string) → LIST[STRING] ────────────────────────────────
    public static object? JsonKeys(object?[] args)
    {
        if (args.Length == 0 || args[0] is null) return null;
        var s = args[0]?.ToString();
        if (s == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(s);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var keys = new List<object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
                keys.Add(prop.Name);
            return keys;
        }
        catch { return null; }
    }

    // ── json_contains(json_string, json_string) → BOOL ───────────────────────
    // Returns true if the first JSON value contains the second.
    public static object? JsonContains(object?[] args)
    {
        if (args.Length < 2 || args[0] is null || args[1] is null) return null;
        var haystack = args[0]?.ToString();
        var needle = args[1]?.ToString();
        if (haystack == null || needle == null) return null;
        try
        {
            using var docH = JsonDocument.Parse(haystack);
            using var docN = JsonDocument.Parse(needle);
            return (object?)JsonContainsElement(docH.RootElement, docN.RootElement);
        }
        catch { return false; }
    }

    private static bool JsonContainsElement(JsonElement haystack, JsonElement needle)
    {
        if (needle.ValueKind == JsonValueKind.Object && haystack.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in needle.EnumerateObject())
            {
                if (!haystack.TryGetProperty(prop.Name, out var hVal))
                    return false;
                if (!JsonContainsElement(hVal, prop.Value))
                    return false;
            }
            return true;
        }
        if (needle.ValueKind == JsonValueKind.Array && haystack.ValueKind == JsonValueKind.Array)
        {
            foreach (var nItem in needle.EnumerateArray())
            {
                bool found = false;
                foreach (var hItem in haystack.EnumerateArray())
                {
                    if (JsonContainsElement(hItem, nItem)) { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }
        return haystack.GetRawText() == needle.GetRawText();
    }

    // ── json_merge_patch(json1, json2) → merged_json ─────────────────────────
    // RFC 7396 merge patch: json2 values override json1 values.
    public static object? JsonMergePatch(object?[] args)
    {
        if (args.Length < 2 || args[0] is null || args[1] is null) return null;
        var s1 = args[0]?.ToString();
        var s2 = args[1]?.ToString();
        if (s1 == null || s2 == null) return null;
        try
        {
            using var doc1 = JsonDocument.Parse(s1);
            using var doc2 = JsonDocument.Parse(s2);
            var merged = MergePatch(doc1.RootElement, doc2.RootElement);
            return merged;
        }
        catch { return null; }
    }

    private static string MergePatch(JsonElement target, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object)
            return patch.GetRawText();

        var result = new Dictionary<string, JsonElement>();
        if (target.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in target.EnumerateObject())
                result[prop.Name] = prop.Value;
        }
        foreach (var prop in patch.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Null)
                result.Remove(prop.Name);
            else if (result.TryGetValue(prop.Name, out var existing))
            {
                var mergedStr = MergePatch(existing, prop.Value);
                result[prop.Name] = JsonDocument.Parse(mergedStr).RootElement.Clone();
            }
            else
                result[prop.Name] = prop.Value;
        }
        // Serialize back
        var sb = new StringBuilder("{");
        var first = true;
        foreach (var kv in result)
        {
            if (!first) sb.Append(',');
            sb.Append(JsonSerializer.Serialize(kv.Key));
            sb.Append(':');
            sb.Append(kv.Value.GetRawText());
            first = false;
        }
        sb.Append('}');
        return sb.ToString();
    }

    // ── json_array(val1, val2, ...) → json_array_string ──────────────────────
    public static object? JsonArray(object?[] args)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(ValueToJson(args[i]));
        }
        sb.Append(']');
        return sb.ToString();
    }

    // ── json_object(key1, val1, key2, val2, ...) → json_object_string ────────
    public static object? JsonObject(object?[] args)
    {
        var sb = new StringBuilder("{");
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonSerializer.Serialize(args[i]?.ToString() ?? ""));
            sb.Append(':');
            sb.Append(ValueToJson(args[i + 1]));
        }
        sb.Append('}');
        return sb.ToString();
    }

    // ── json_quote(value) → json_string ──────────────────────────────────────
    // Wraps a value in JSON quoting.
    public static object? JsonQuote(object?[] args)
    {
        if (args.Length == 0 || args[0] is null) return "null";
        return ValueToJson(args[0]);
    }

    // ── json_structure(json_string) → type_description_string ────────────────
    // Returns a BogDb-style type description of the JSON structure.
    public static object? JsonStructure(object?[] args)
    {
        if (args.Length == 0 || args[0] is null) return null;
        var s = args[0]?.ToString();
        if (s == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(s);
            return DescribeStructure(doc.RootElement);
        }
        catch { return null; }
    }

    private static string DescribeStructure(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Object =>
                "STRUCT(" + string.Join(", ",
                    el.EnumerateObject().Select(p => p.Name + " " + DescribeStructure(p.Value))) + ")",
            JsonValueKind.Array =>
                el.GetArrayLength() > 0
                    ? DescribeStructure(el[0]) + "[]"
                    : "JSON[]",
            JsonValueKind.String => "VARCHAR",
            JsonValueKind.Number => el.TryGetInt64(out _) ? "UBIGINT" : "DOUBLE",
            JsonValueKind.True or JsonValueKind.False => "BOOLEAN",
            JsonValueKind.Null => "NULL",
            _ => "JSON"
        };
    }

    // ── to_json(value) / cast_to_json(value) → json_string ───────────────────
    public static object? ToJson(object?[] args)
    {
        if (args.Length == 0) return null;
        return ValueToJson(args[0]);
    }

    // ── row_to_json(struct_val) → json_string ────────────────────────────────
    // Converts a struct/map to JSON. In C#, maps are Dictionary<string, object?>.
    public static object? RowToJson(object?[] args)
    {
        if (args.Length == 0 || args[0] is null) return null;
        if (args[0] is Dictionary<string, object?> dict)
        {
            var sb = new StringBuilder("{");
            var first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                sb.Append(JsonSerializer.Serialize(kv.Key));
                sb.Append(':');
                sb.Append(ValueToJson(kv.Value));
                first = false;
            }
            sb.Append('}');
            return sb.ToString();
        }
        return ValueToJson(args[0]);
    }

    // ── array_to_json(list_val) → json_string ────────────────────────────────
    public static object? ArrayToJson(object?[] args)
    {
        if (args.Length == 0 || args[0] is null) return null;
        if (args[0] is IEnumerable<object?> list)
        {
            var sb = new StringBuilder("[");
            var first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                sb.Append(ValueToJson(item));
                first = false;
            }
            sb.Append(']');
            return sb.ToString();
        }
        return ValueToJson(args[0]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static object? ElementToValue(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? (object?)l : el.GetDouble(),
            JsonValueKind.True   => true,
            JsonValueKind.False  => false,
            JsonValueKind.Null   => null,
            _                    => el.GetRawText()
        };
    }

    private static string ValueToJson(object? val)
    {
        if (val == null) return "null";
        if (val is bool b) return b ? "true" : "false";
        if (val is long l) return l.ToString();
        if (val is int i) return i.ToString();
        if (val is double d) return d.ToString("G");
        if (val is float f) return f.ToString("G");
        if (val is decimal m) return m.ToString("G");
        if (val is string s)
        {
            // If already valid JSON, return as-is; otherwise quote as string
            try { using var _ = JsonDocument.Parse(s); return s; }
            catch { return JsonSerializer.Serialize(s); }
        }
        if (val is IEnumerable<object?> list)
        {
            var sb = new StringBuilder("[");
            var first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                sb.Append(ValueToJson(item));
                first = false;
            }
            sb.Append(']');
            return sb.ToString();
        }
        if (val is Dictionary<string, object?> dict)
        {
            var sb = new StringBuilder("{");
            var first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                sb.Append(JsonSerializer.Serialize(kv.Key));
                sb.Append(':');
                sb.Append(ValueToJson(kv.Value));
                first = false;
            }
            sb.Append('}');
            return sb.ToString();
        }
        return JsonSerializer.Serialize(val.ToString());
    }
}
