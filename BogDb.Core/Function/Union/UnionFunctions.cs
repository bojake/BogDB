using System;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Union;

/// <summary>
/// Union scalar functions.
/// C++ parity: src/function/union/*.cpp
/// </summary>
internal static class UnionFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        r["union_value"] = a =>
        {
            if (a.Length < 1) return null;
            return (object?)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["tag"] = "value",
                ["value"] = a[0]
            };
        };

        r["union_tag"] = a =>
        {
            if (a.Length < 1) return null;
            if (a[0] is Dictionary<string, object?> sd && sd.TryGetValue("tag", out var tag))
                return tag;
            return null;
        };

        r["union_extract"] = a =>
        {
            if (a.Length < 2) return null;
            var key = TypeCoercionHelper.ToBogDbString(a[1]) ?? "";
            if (a[0] is Dictionary<string, object?> sd)
            {
                if (sd.TryGetValue(key, out var v)) return v;
                if (sd.TryGetValue("tag", out var tag) && Equals(tag, key) && sd.TryGetValue("value", out var val))
                    return val;
            }
            return null;
        };
    }
}
