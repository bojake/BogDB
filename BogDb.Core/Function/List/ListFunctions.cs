using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Function.List;

/// <summary>
/// List scalar functions operating on <see cref="List{T}"/> values.
/// C++ parity: src/function/list_functions.cpp
/// </summary>
internal static class ListFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── Creation ───────────────────────────────────────────────────────────
        r["list_creation"] = a => (object?)new List<object?>(a);
        r["array_value"]   = r["list_creation"];

        // ── Access ─────────────────────────────────────────────────────────────
        r["list_element"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 2) return null;
            var idx = (int)TypeCoercionHelper.ToInt64(a[1]);
            // BogDb uses 1-based indexing; support negative (from end)
            if (idx > 0) idx -= 1;
            else if (idx < 0) idx = list.Count + idx;
            return idx >= 0 && idx < list.Count ? list[idx] : null;
        };
        r["list_extract"] = r["list_element"];
        r["array_extract"] = r["list_element"];

        // ── Size ───────────────────────────────────────────────────────────────
        r["list_len"]     = a => Len(a);
        r["array_length"] = r["list_len"];
        r["len"]          = r["list_len"];
        r["cardinality"]  = r["list_len"];

        // ── Predicates ─────────────────────────────────────────────────────────
        r["list_contains"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 2) return null;
            var val = a[1];
            return (object?)list.Any(x => StructuralEquals(x, val));
        };
        r["array_contains"] = r["list_contains"];
        r["list_has"]       = r["list_contains"];

        r["list_position"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 2) return null;
            for (int i = 0; i < list.Count; i++)
                if (StructuralEquals(list[i], a[1])) return (object?)(long)(i + 1);
            return 0L;
        };
        r["list_indexof"] = r["list_position"];

        // ── Slice ──────────────────────────────────────────────────────────────
        r["list_slice"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 3) return null;
            var start = (int)TypeCoercionHelper.ToInt64(a[1]) - 1; // 1-based
            var end   = (int)TypeCoercionHelper.ToInt64(a[2]);
            start = System.Math.Max(0, start);
            end   = System.Math.Min(list.Count, end);
            return (object?)list.Skip(start).Take(end - start).ToList();
        };
        r["array_slice"] = r["list_slice"];
        r["list_slice_from"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 2) return null;
            var start = (int)TypeCoercionHelper.ToInt64(a[1]) - 1;
            start = System.Math.Max(0, start);
            return (object?)list.Skip(start).ToList();
        };

        // ── Concat / range ────────────────────────────────────────────────────
        r["list_concat"] = a =>
        {
            var l1 = AsList(a, 0); var l2 = AsList(a, 1);
            if (l1 == null || l2 == null) return null;
            return (object?)l1.Concat(l2).ToList();
        };
        r["list_cat"]    = r["list_concat"];
        r["array_concat"] = r["list_concat"];

        r["range"] = a =>
        {
            if (a.Length < 2) return null;
            var start = TypeCoercionHelper.ToInt64(a[0]);
            var end   = TypeCoercionHelper.ToInt64(a[1]);
            var step  = a.Length >= 3 ? TypeCoercionHelper.ToInt64(a[2]) : 1L;
            if (step == 0) return null;
            var result = new List<object?>();
            for (var i = start; step > 0 ? i <= end : i >= end; i += step)
                result.Add(i);
            return (object?)result;
        };
        r["generate_series"] = r["range"];

        // ── Sort ───────────────────────────────────────────────────────────────
        r["list_sort"] = a =>
        {
            var list = AsList(a, 0); if (list == null) return null;
            return (object?)list.Select(TypeCoercionHelper.Normalize)
                               .OrderBy(x => x, BogDbComparer.Instance).ToList();
        };
        r["list_reverse_sort"] = a =>
        {
            var list = AsList(a, 0); if (list == null) return null;
            return (object?)list.Select(TypeCoercionHelper.Normalize)
                               .OrderByDescending(x => x, BogDbComparer.Instance).ToList();
        };
        r["array_sort"]     = r["list_sort"];
        r["list_sort_desc"] = r["list_reverse_sort"];
        r["list_reverse"] = a =>
        {
            var list = AsList(a, 0); if (list == null) return null;
            return (object?)((IEnumerable<object?>)list).Reverse().ToList();
        };

        // list_transform(list, funcName) — apply a registered scalar fn to each element
        r["list_transform"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 2) return null;
            var fnName = TypeCoercionHelper.ToBogDbString(a[1]) ?? "";
            return (object?)list.Select(x => FunctionDispatcher.Invoke(fnName, [x])).ToList();
        };
        r["list_apply"] = r["list_transform"];

        // ── Set / membership helpers ─────────────────────────────────────────
        r["list_has_all"] = a =>
        {
            var list = AsList(a, 0); var needle = AsList(a, 1);
            if (list == null || needle == null) return null;
            return (object?)needle.All(x => list.Any(candidate => StructuralEquals(candidate, x)));
        };
        r["list_has_any"] = a =>
        {
            var list = AsList(a, 0); var needle = AsList(a, 1);
            if (list == null || needle == null) return null;
            return (object?)needle.Any(x => list.Any(candidate => StructuralEquals(candidate, x)));
        };

        r["list_union"] = a =>
        {
            var l1 = AsList(a, 0); var l2 = AsList(a, 1);
            if (l1 == null || l2 == null) return null;
            var res = new List<object?>();
            AppendDistinct(res, l1);
            AppendDistinct(res, l2);
            return (object?)res;
        };
        r["list_intersect"] = a =>
        {
            var l1 = AsList(a, 0); var l2 = AsList(a, 1);
            if (l1 == null || l2 == null) return null;
            var result = new List<object?>();
            foreach (var item in l1.Select(TypeCoercionHelper.Normalize))
            {
                if (l2.Any(other => StructuralEquals(other, item)) &&
                    !result.Any(existing => StructuralEquals(existing, item)))
                {
                    result.Add(item);
                }
            }

            return (object?)result;
        };
        r["list_except"] = a =>
        {
            var l1 = AsList(a, 0); var l2 = AsList(a, 1);
            if (l1 == null || l2 == null) return null;
            return (object?)l1.Select(TypeCoercionHelper.Normalize)
                .Where(x => !l2.Any(other => StructuralEquals(other, x)))
                .ToList();
        };

        // ── Set operations ────────────────────────────────────────────────────
        r["list_unique"]   = a =>
        {
            var list = AsList(a, 0); if (list == null) return null;
            var result = new List<object?>();
            AppendDistinct(result, list);
            return (object?)result;
        };
        r["list_distinct"] = r["list_unique"];
        r["array_unique"]  = r["list_unique"];

        // ── Aggregates ────────────────────────────────────────────────────────
        r["list_sum"] = a =>
        {
            var list = AsList(a, 0); if (list == null) return null;
            return (object?)list.Sum(x => TypeCoercionHelper.ToDouble(x));
        };
        r["list_avg"] = a =>
        {
            var list = AsList(a, 0); if (list == null || list.Count == 0) return null;
            return (object?)(list.Sum(x => TypeCoercionHelper.ToDouble(x)) / list.Count);
        };
        r["list_min"] = a =>
        {
            var list = AsList(a, 0); if (list == null || list.Count == 0) return null;
            return list.Select(TypeCoercionHelper.ToDouble).Min();
        };
        r["list_max"] = a =>
        {
            var list = AsList(a, 0); if (list == null || list.Count == 0) return null;
            return (object?)list.Select(TypeCoercionHelper.ToDouble).Max();
        };
        r["list_product"] = a =>
        {
            var list = AsList(a, 0); if (list == null) return null;
            return (object?)list.Aggregate(1.0, (acc, x) => acc * TypeCoercionHelper.ToDouble(x));
        };

        // ── Append / prepend ──────────────────────────────────────────────────
        r["list_append"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 2) return null;
            var copy = new List<object?>(list) { a[1] };
            return (object?)copy;
        };
        r["list_prepend"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 2) return null;
            var copy = new List<object?> { a[1] }; copy.AddRange(list);
            return (object?)copy;
        };

        r["list_remove"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 2) return null;
            var copy = new List<object?>();
            bool removed = false;
            foreach (var item in list)
            {
                if (!removed && StructuralEquals(item, a[1]))
                {
                    removed = true;
                    continue;
                }
                copy.Add(item);
            }
            return (object?)copy;
        };
        r["list_remove_all"] = a =>
        {
            var list = AsList(a, 0); if (list == null || a.Length < 2) return null;
            return (object?)list.Where(x => !StructuralEquals(x, a[1])).ToList();
        };

        r["list_flatten"] = a =>
        {
            var list = AsList(a, 0); if (list == null) return null;
            var flat = new List<object?>();
            foreach (var item in list)
            {
                if (item is System.Collections.IEnumerable ie && item is not string)
                {
                    foreach (var v in ie.Cast<object?>())
                        flat.Add(v);
                }
                else
                {
                    flat.Add(item);
                }
            }
            return (object?)flat;
        };

        // ── To string ─────────────────────────────────────────────────────────
        r["list_to_string"] = a =>
        {
            var list = AsList(a, 0); if (list == null) return null;
            var sep = a.Length >= 2 ? (TypeCoercionHelper.ToBogDbString(a[1]) ?? ",") : ",";
            return (object?)string.Join(sep, list.Select(x => TypeCoercionHelper.ToBogDbString(x) ?? ""));
        };
        r["array_to_string"] = r["list_to_string"];

        // ── C++ parity aliases ────────────────────────────────────────────────
        r["array_indexof"]  = r["list_position"];  // C++ array_indexof alias
        r["array_position"] = r["list_position"];  // C++ array_position alias
        r["array_prepend"]  = r["list_prepend"];   // C++ array_prepend alias
        r["array_append"]   = r["list_append"];    // C++ array_append alias

        // list_any_value(list) — returns the first non-null element
        r["list_any_value"] = a =>
        {
            var list = AsList(a, 0); if (list == null) return null;
            return list.FirstOrDefault(x => x != null);
        };
    }

    private static List<object?>? AsList(object?[] a, int idx)
    {
        if (idx >= a.Length) return null;
        return a[idx] switch
        {
            List<object?> l => l,
            System.Collections.IEnumerable ie and not string
                => ie.Cast<object?>().ToList(),
            _ => null
        };
    }

    private static object? Len(object?[] a)
    {
        var list = AsList(a, 0);
        return list != null ? (object?)(long)list.Count : null;
    }

    private static void AppendDistinct(List<object?> target, IEnumerable<object?> items)
    {
        foreach (var item in items.Select(TypeCoercionHelper.Normalize))
        {
            if (!target.Any(existing => StructuralEquals(existing, item)))
                target.Add(item);
        }
    }

    private static bool StructuralEquals(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        left = TypeCoercionHelper.Normalize(left);
        right = TypeCoercionHelper.Normalize(right);

        if (TryGetDictionaryEntries(left, out var leftEntries) &&
            TryGetDictionaryEntries(right, out var rightEntries))
        {
            if (leftEntries.Count != rightEntries.Count)
                return false;

            foreach (var (key, value) in leftEntries)
            {
                if (!rightEntries.TryGetValue(key, out var otherValue) || !StructuralEquals(value, otherValue))
                    return false;
            }

            return true;
        }

        if (left is not string && right is not string &&
            left is System.Collections.IEnumerable leftEnumerable &&
            right is System.Collections.IEnumerable rightEnumerable)
        {
            var leftItems = leftEnumerable.Cast<object?>().ToList();
            var rightItems = rightEnumerable.Cast<object?>().ToList();
            if (leftItems.Count != rightItems.Count)
                return false;

            for (var i = 0; i < leftItems.Count; i++)
            {
                if (!StructuralEquals(leftItems[i], rightItems[i]))
                    return false;
            }

            return true;
        }

        if (IsNumeric(left) && IsNumeric(right))
            return Convert.ToDouble(left) == Convert.ToDouble(right);

        return Equals(left, right);
    }

    private static bool TryGetDictionaryEntries(object value, out Dictionary<string, object?> entries)
    {
        entries = new Dictionary<string, object?>(StringComparer.Ordinal);

        switch (value)
        {
            case IDictionary<string, object?> typedDictionary:
                foreach (var entry in typedDictionary)
                    entries[entry.Key] = entry.Value;
                return true;
            case IDictionary<object, object?> objectDictionary:
                foreach (var entry in objectDictionary)
                    entries[entry.Key?.ToString() ?? "null"] = entry.Value;
                return true;
            case IEnumerable<KeyValuePair<string, object?>> stringPairs:
                foreach (var entry in stringPairs)
                    entries[entry.Key] = entry.Value;
                return true;
            case IEnumerable<KeyValuePair<object?, object?>> objectPairs:
                foreach (var entry in objectPairs)
                    entries[entry.Key?.ToString() ?? "null"] = entry.Value;
                return true;
            default:
                return false;
        }
    }

    private static bool IsNumeric(object value) => value switch
    {
        byte or sbyte or short or ushort or int or uint or long or ulong or
        float or double or decimal => true,
        _ => false
    };
}

/// <summary>
/// Cross-type comparer that provides total ordering consistent with BogDb C++ value semantics:
/// NULL &lt; INT64 &lt; DOUBLE &lt; BOOL &lt; STRING &lt; LIST &lt; other.
/// Used by list_sort / list_reverse_sort.
/// </summary>
internal sealed class BogDbComparer : System.Collections.Generic.IComparer<object?>
{
    public static readonly BogDbComparer Instance = new();

    public int Compare(object? x, object? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        // Same numeric family — use numeric comparison
        if (x is long lx && y is long ly) return lx.CompareTo(ly);
        if ((x is long || x is double) && (y is long || y is double))
        {
            var dx = x is double d1 ? d1 : (double)(long)x;
            var dy = y is double d2 ? d2 : (double)(long)y;
            return dx.CompareTo(dy);
        }
        // Bool
        if (x is bool bx && y is bool by) return bx.CompareTo(by);
        // String
        if (x is string sx && y is string sy) return string.CompareOrdinal(sx, sy);
        // Mixed: use type-rank then toString fallback
        return GetRank(x).CompareTo(GetRank(y)) != 0
            ? GetRank(x).CompareTo(GetRank(y))
            : string.CompareOrdinal(x.ToString(), y.ToString());
    }

    private static int GetRank(object? v) => v switch
    {
        null   => 0, long => 1, int => 1, short => 1, sbyte => 1,
        ulong  => 1, uint => 1, ushort => 1, byte => 1,
        double => 2, float => 2, decimal => 2,
        bool   => 3, string => 4, _ => 5
    };
}
