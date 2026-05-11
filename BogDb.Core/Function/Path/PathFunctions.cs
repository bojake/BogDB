using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Path;

/// <summary>
/// Path scalar functions: nodes(), rels(), length(), properties().
/// In BogDB, a path/recursive-rel is represented as a Dictionary with
/// "_nodes" and "_rels" list entries, or as a raw List of items.
///
/// C++ parity: src/function/path/
/// </summary>
internal static class PathFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── Core path accessors ───────────────────────────────────────────────

        r["nodes"] = a =>
        {
            if (a.Length < 1) return null;
            return (object?)ExtractPathField(a[0], "_nodes", "nodes");
        };

        r["rels"] = r["relationships"] = a =>
        {
            if (a.Length < 1) return null;
            return (object?)ExtractPathField(a[0], "_rels", "rels", "relationships");
        };

        // length(path) — number of relationships in the path
        r["length"] = a =>
        {
            if (a.Length < 1) return null;
            var rels = ExtractPathField(a[0], "_rels", "rels", "relationships");
            if (rels != null)
                return (object?)(long)rels.Count;

            // Fallback: string length for compatibility with string-length usage
            if (a[0] is string s)
                return (object?)(long)s.Length;
            return null;
        };

        // properties(node/rel) — returns the property map of a node or rel dict
        r["properties"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            if (a[0] is Dictionary<string, object?> sd)
                return (object?)sd.Where(kv => !kv.Key.StartsWith('_'))
                                  .ToDictionary(kv => kv.Key, kv => kv.Value,
                                                StringComparer.OrdinalIgnoreCase);
            if (a[0] is Dictionary<string, object> sod)
                return (object?)sod.Where(kv => !kv.Key.StartsWith('_'))
                                   .ToDictionary(kv => kv.Key, kv => (object?)kv.Value,
                                                 StringComparer.OrdinalIgnoreCase);
            return null;
        };

        // is_trail / is_acyclic — path semantic predicates
        r["is_trail"] = a =>
        {
            if (a.Length < 1) return null;
            var rels = ExtractPathField(a[0], "_rels", "rels");
            if (rels == null) return null;
            var seen = new HashSet<object?>();
            return (object?)rels.All(rel => seen.Add(TypeCoercionHelper.Normalize(rel)));
        };

        r["is_acyclic"] = a =>
        {
            if (a.Length < 1) return null;
            var nodes = ExtractPathField(a[0], "_nodes", "nodes");
            if (nodes == null) return null;
            var seen = new HashSet<object?>();
            return (object?)nodes.All(n => seen.Add(TypeCoercionHelper.Normalize(n)));
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<object?>? ExtractPathField(object? value, params string[] keys)
    {
        if (value == null) return null;

        // Dict with named fields (e.g. {"_nodes": [...], "_rels": [...]})
        if (value is Dictionary<string, object?> sd)
        {
            foreach (var key in keys)
                if (sd.TryGetValue(key, out var v) && v is IEnumerable<object?> ie)
                    return ie.ToList();
        }
        if (value is Dictionary<string, object> sod)
        {
            foreach (var key in keys)
                if (sod.TryGetValue(key, out var v) && v is IEnumerable<object?> ie)
                    return ie.ToList();
        }

        // Already a list — treat as nodes() identity
        if (value is List<object?> list)
            return list;
        if (value is System.Collections.IEnumerable ie2 && value is not string)
            return ie2.Cast<object?>().ToList();

        return null;
    }
}
