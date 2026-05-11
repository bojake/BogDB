using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Map;

/// <summary>
/// Map scalar functions.
/// C++ parity: src/function/map/*.cpp
/// </summary>
internal static class MapFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        r["map"] = a =>
        {
            if (a.Length < 2) return null;
            var keys = AsList(a[0]);
            var vals = AsList(a[1]);
            if (keys == null || vals == null) return null;
            var count = System.Math.Min(keys.Count, vals.Count);
            var map = new List<KeyValuePair<object?, object?>>(count);
            for (int i = 0; i < count; i++)
            {
                map.Add(new KeyValuePair<object?, object?>(TypeCoercionHelper.Normalize(keys[i]), vals[i]));
            }
            return (object?)map;
        };

        r["map_extract"] = a =>
        {
            if (a.Length < 2) return null;
            var key = TypeCoercionHelper.Normalize(a[1]);
            if (a[0] is Dictionary<string, object?> sd)
            {
                return (object?)new List<object?> { sd.TryGetValue(key?.ToString() ?? "", out var v) ? v : null };
            }
            if (a[0] is Dictionary<object, object?> od)
            {
                return (object?)new List<object?> { od.TryGetValue(key ?? "", out var v) ? v : null };
            }
            if (a[0] is IEnumerable<KeyValuePair<object?, object?>> list)
            {
                var result = list.Where(kv => Equals(TypeCoercionHelper.Normalize(kv.Key), key))
                                 .Select(kv => kv.Value).ToList();
                return (object?)result;
            }
            return null;
        };
        r["element_at"] = r["map_extract"];

        r["map_keys"] = a =>
        {
            if (a.Length < 1) return null;
            if (a[0] is Dictionary<string, object?> sd)
                return (object?)sd.Keys.Cast<object?>().ToList();
            if (a[0] is Dictionary<object, object?> od)
                return (object?)od.Keys.Cast<object?>().ToList();
            if (a[0] is IEnumerable<KeyValuePair<object?, object?>> list)
                return (object?)list.Select(kv => kv.Key).ToList();
            return null;
        };

        r["map_values"] = a =>
        {
            if (a.Length < 1) return null;
            if (a[0] is Dictionary<string, object?> sd)
                return (object?)sd.Values.Cast<object?>().ToList();
            if (a[0] is Dictionary<object, object?> od)
                return (object?)od.Values.Cast<object?>().ToList();
            if (a[0] is IEnumerable<KeyValuePair<object?, object?>> list)
                return (object?)list.Select(kv => kv.Value).ToList();
            return null;
        };

        // P1-052 additions
        r["map_contains"] = a =>
        {
            if (a.Length < 2) return null;
            var key = TypeCoercionHelper.Normalize(a[1]);
            if (a[0] is Dictionary<string, object?> sd)
                return (object?)sd.ContainsKey(key?.ToString() ?? "");
            if (a[0] is Dictionary<object, object?> od)
                return (object?)od.ContainsKey(key ?? "");
            if (a[0] is IEnumerable<KeyValuePair<object?, object?>> list)
                return (object?)list.Any(kv => Equals(TypeCoercionHelper.Normalize(kv.Key), key));
            return false;
        };
        r["map_has_key"] = r["map_contains"];

        r["map_size"] = a =>
        {
            if (a.Length < 1) return null;
            if (a[0] is Dictionary<string, object?> sd) return (object?)(long)sd.Count;
            if (a[0] is Dictionary<object, object?> od) return (object?)(long)od.Count;
            if (a[0] is IEnumerable<KeyValuePair<object?, object?>> list) return (object?)(long)list.Count();
            return null;
        };
        r["map_cardinality"] = r["map_size"];

        r["map_entries"] = a =>
        {
            if (a.Length < 1) return null;
            IEnumerable<KeyValuePair<object?, object?>>? pairs = null;
            if (a[0] is Dictionary<string, object?> sd)
                pairs = sd.Select(kv => new KeyValuePair<object?, object?>(kv.Key, kv.Value));
            else if (a[0] is Dictionary<object, object?> od)
                pairs = od.Select(kv => new KeyValuePair<object?, object?>(kv.Key, kv.Value));
            else if (a[0] is IEnumerable<KeyValuePair<object?, object?>> lp)
                pairs = lp;
            if (pairs == null) return null;
            return (object?)pairs.Select(kv => (object?)new Dictionary<string, object?> { ["key"] = kv.Key, ["value"] = kv.Value }).ToList();
        };
    }

    private static List<object?>? AsList(object? value)
    {
        return value switch
        {
            List<object?> l => l,
            System.Collections.IEnumerable ie and not string => ie.Cast<object?>().ToList(),
            _ => null
        };
    }
}
