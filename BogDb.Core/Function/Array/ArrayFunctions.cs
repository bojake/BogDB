using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Array;

/// <summary>
/// Fixed-size array (ARRAY) scalar functions.
/// In BogDB, ARRAY values are represented as List&lt;object?&gt;.
/// Vector math functions (cosine similarity, cross-product, etc.) treat list
/// elements as doubles.
///
/// C++ parity: src/function/array/
/// </summary>
internal static class ArrayFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── Construction ──────────────────────────────────────────────────────

        // array_value(v1, v2, ...) — create fixed array from args
        r["array_value"] = a =>
            (object?)a.Select(x => (object?)TypeCoercionHelper.Normalize(x)).ToList();

        // ── Element access ────────────────────────────────────────────────────

        r["array_extract"] = r["array_element"] = a =>
        {
            var arr = ToDoubles(a, 0);
            if (arr == null || a.Length < 2) return null;
            var idx = (int)TypeCoercionHelper.ToInt64(a[1]) - 1; // 1-based
            return idx >= 0 && idx < arr.Count ? (object?)arr[idx] : null;
        };

        // ── Contains / membership ─────────────────────────────────────────────

        r["array_contains"] = r["array_has"] = a =>
        {
            var arr = AsList(a, 0);
            if (arr == null || a.Length < 2) return null;
            var needle = TypeCoercionHelper.Normalize(a[1]);
            return (object?)arr.Any(x => Equals(TypeCoercionHelper.Normalize(x), needle));
        };

        // ── Dimensions ────────────────────────────────────────────────────────

        r["array_length"] = a =>
        {
            var arr = AsList(a, 0);
            return arr != null ? (object?)(long)arr.Count : null;
        };

        // ── Comparison / sorting ──────────────────────────────────────────────

        r["array_min"] = a =>
        {
            var arr = ToDoubles(a, 0);
            return arr?.Count > 0 ? (object?)arr.Min() : null;
        };

        r["array_max"] = a =>
        {
            var arr = ToDoubles(a, 0);
            return arr?.Count > 0 ? (object?)arr.Max() : null;
        };

        r["array_sum"] = a =>
        {
            var arr = ToDoubles(a, 0);
            return arr?.Count > 0 ? (object?)arr.Sum() : null;
        };

        r["array_avg"] = a =>
        {
            var arr = ToDoubles(a, 0);
            return arr?.Count > 0 ? (object?)(arr.Sum() / arr.Count) : null;
        };

        // ── Vector math ───────────────────────────────────────────────────────

        // array_inner_product / array_dot_product
        r["array_inner_product"] = r["array_dot_product"] = a =>
        {
            var x = ToDoubles(a, 0); var y = ToDoubles(a, 1);
            if (x == null || y == null || x.Count != y.Count) return null;
            return (object?)x.Zip(y).Sum(pair => pair.First * pair.Second);
        };

        // array_cosine_similarity — dot(x,y) / (|x| * |y|)
        r["array_cosine_similarity"] = a =>
        {
            var x = ToDoubles(a, 0); var y = ToDoubles(a, 1);
            if (x == null || y == null || x.Count != y.Count) return null;
            double dot = x.Zip(y).Sum(p => p.First * p.Second);
            double normX = Math.Sqrt(x.Sum(v => v * v));
            double normY = Math.Sqrt(y.Sum(v => v * v));
            return normX == 0 || normY == 0 ? (object?)0.0 : (object?)(dot / (normX * normY));
        };

        // array_squared_distance — sum((xi - yi)^2)
        r["array_squared_distance"] = a =>
        {
            var x = ToDoubles(a, 0); var y = ToDoubles(a, 1);
            if (x == null || y == null || x.Count != y.Count) return null;
            return (object?)x.Zip(y).Sum(p => (p.First - p.Second) * (p.First - p.Second));
        };

        // array_distance — sqrt(squared_distance)
        r["array_distance"] = r["array_l2_distance"] = a =>
        {
            var x = ToDoubles(a, 0); var y = ToDoubles(a, 1);
            if (x == null || y == null || x.Count != y.Count) return null;
            double sq = x.Zip(y).Sum(p => (p.First - p.Second) * (p.First - p.Second));
            return (object?)Math.Sqrt(sq);
        };

        // array_cross_product — 3-element cross product
        r["array_cross_product"] = a =>
        {
            var x = ToDoubles(a, 0); var y = ToDoubles(a, 1);
            if (x == null || y == null || x.Count != 3 || y.Count != 3) return null;
            return (object?)new List<object?>
            {
                (object?)(x[1]*y[2] - x[2]*y[1]),
                (object?)(x[2]*y[0] - x[0]*y[2]),
                (object?)(x[0]*y[1] - x[1]*y[0])
            };
        };

        // array_l1_distance — sum(|xi - yi|)
        r["array_l1_distance"] = a =>
        {
            var x = ToDoubles(a, 0); var y = ToDoubles(a, 1);
            if (x == null || y == null || x.Count != y.Count) return null;
            return (object?)x.Zip(y).Sum(p => Math.Abs(p.First - p.Second));
        };

        // array_cosine_distance — 1 - cosine_similarity
        r["array_cosine_distance"] = a =>
        {
            var x = ToDoubles(a, 0); var y = ToDoubles(a, 1);
            if (x == null || y == null || x.Count != y.Count) return null;
            double dot = x.Zip(y).Sum(p => p.First * p.Second);
            double nX = Math.Sqrt(x.Sum(v => v * v)), nY = Math.Sqrt(y.Sum(v => v * v));
            double sim = nX == 0 || nY == 0 ? 0.0 : dot / (nX * nY);
            return (object?)(1.0 - sim);
        };

        // ── Quantize / normalize ──────────────────────────────────────────────

        r["array_normalize"] = a =>
        {
            var arr = ToDoubles(a, 0);
            if (arr == null) return null;
            double norm = Math.Sqrt(arr.Sum(v => v * v));
            if (norm == 0) return (object?)arr.Select(x => (object?)0.0).ToList();
            return (object?)arr.Select(v => (object?)(v / norm)).ToList();
        };

        // ── Transform / set ops ───────────────────────────────────────────────

        r["array_concat"] = r["array_cat"] = a =>
        {
            var l1 = AsList(a, 0); var l2 = AsList(a, 1);
            if (l1 == null || l2 == null) return null;
            return (object?)l1.Concat(l2).ToList();
        };

        r["array_push_front"] = a =>
        {
            var arr = AsList(a, 0); if (arr == null || a.Length < 2) return null;
            var copy = new List<object?> { a[1] };
            copy.AddRange(arr);
            return (object?)copy;
        };

        r["array_push_back"] = r["array_append"] = a =>
        {
            var arr = AsList(a, 0); if (arr == null || a.Length < 2) return null;
            var copy = new List<object?>(arr) { a[1] };
            return (object?)copy;
        };

        r["array_pop_front"] = a =>
        {
            var arr = AsList(a, 0);
            return arr?.Count > 0 ? (object?)arr.Skip(1).ToList() : null;
        };

        r["array_pop_back"] = a =>
        {
            var arr = AsList(a, 0);
            return arr?.Count > 0 ? (object?)arr.Take(arr.Count - 1).ToList() : null;
        };

        r["array_reverse"] = a =>
        {
            var arr = AsList(a, 0);
            return arr != null ? (object?)arr.AsEnumerable().Reverse().ToList() : null;
        };

        r["array_unique"] = a =>
        {
            var arr = AsList(a, 0);
            return arr != null
                ? (object?)arr.Select(TypeCoercionHelper.Normalize).Distinct().ToList()
                : null;
        };

        r["array_apply"] = r["list_apply"] = a =>
        {
            // No lambda support at this layer; identity passthrough
            var arr = AsList(a, 0);
            return arr != null ? (object?)arr : null;
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<object?>? AsList(object?[] a, int idx)
    {
        if (idx >= a.Length || a[idx] == null) return null;
        return a[idx] switch
        {
            List<object?> l                              => l,
            System.Collections.IEnumerable ie and not string => ie.Cast<object?>().ToList(),
            _                                            => null
        };
    }

    private static List<double>? ToDoubles(object?[] a, int idx)
    {
        var list = AsList(a, idx);
        if (list == null) return null;
        try { return list.Select(x => TypeCoercionHelper.ToDouble(x)).ToList(); }
        catch { return null; }
    }
}
