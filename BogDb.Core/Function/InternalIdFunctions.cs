using System;
using System.Collections.Generic;

namespace BogDb.Core.Function.InternalId;

/// <summary>
/// C++ parity: <c>src/function/internal_id/</c>
///
/// Internal-ID functions operate on the physical storage offset of a node or relationship,
/// providing access to BogDb's internal node/rel identifiers.
///
/// Functions:
///   offset(node)            → INT64: the 0-based storage offset of the node
///   internal_id(node)       → STRING: canonical "{tableId}:{offset}" representation
///   internal_id_equal(a, b) → BOOL: true if both have the same internal ID
/// </summary>
public static class InternalIdFunctions
{
    public static void Register(Dictionary<string, Func<object?[], object?>> funcs)
    {
        // offset(node) → extract the integer offset part of an internal ID
        // In BogDB, nodes expose their ID via the property bag key "_id" or as a long.
        funcs["offset"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            return ExtractOffset(args[0]);
        };

        // internal_id(node) → string like "0:42" (tableId:offset)
        funcs["internal_id"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            var id = args[0];
            // If already a string in "table:offset" form, return as-is
            if (id is string s && s.Contains(':')) return s;
            // If a long, treat as offset in table 0
            if (id is long l) return $"0:{l}";
            if (id is int i) return $"0:{i}";
            // Dictionary node: look for _id key
            if (id is Dictionary<string, object?> dict)
            {
                if (dict.TryGetValue("_id", out var vid)) return vid?.ToString() ?? "0:0";
                if (dict.TryGetValue("id", out var id2))
                {
                    var off = ExtractOffset(id2);
                    return $"0:{off}";
                }
            }
            return $"0:{id}";
        };

        // internal_id_equal(a, b) → true if both refer to the same internal location
        funcs["internal_id_equal"] = args =>
        {
            if (args.Length < 2 || args[0] == null || args[1] == null) return false;
            var a = ExtractIdString(args[0]);
            var b = ExtractIdString(args[1]);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        };

        // node_offset(node) — alias used in some C++ tests
        funcs["node_offset"] = funcs["offset"];
    }

    private static long? ExtractOffset(object? value) => value switch
    {
        long l   => l,
        int i    => i,
        string s => s.Contains(':') && long.TryParse(s.Split(':')[^1], out var p) ? p : null,
        Dictionary<string, object?> d =>
            d.TryGetValue("_id", out var v) ? ExtractOffset(v) :
            d.TryGetValue("id",  out var v2) ? ExtractOffset(v2) : null,
        _ => null,
    };

    private static string? ExtractIdString(object? value) => value switch
    {
        string s when s.Contains(':') => s,
        long l   => $"0:{l}",
        int i    => $"0:{i}",
        Dictionary<string, object?> d =>
            d.TryGetValue("_id", out var v) ? v?.ToString() : null,
        _ => value?.ToString(),
    };
}
