using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Struct;

/// <summary>
/// Struct scalar functions.
/// C++ parity: src/function/struct/*.cpp
/// </summary>
internal static class StructFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        r["struct_pack"] = a =>
        {
            if (a.Length == 1 && a[0] is Dictionary<string, object?> sd)
                return (object?)new Dictionary<string, object?>(sd, StringComparer.OrdinalIgnoreCase);

            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (a.Length % 2 == 0)
            {
                for (int i = 0; i < a.Length; i += 2)
                {
                    var key = TypeCoercionHelper.ToBogDbString(a[i]) ?? $"f{i / 2}";
                    dict[key] = a[i + 1];
                }
            }
            else
            {
                for (int i = 0; i < a.Length; i++)
                {
                    dict[$"f{i}"] = a[i];
                }
            }
            return (object?)dict;
        };

        r["struct_extract"] = a =>
        {
            if (a.Length < 2) return null;
            var key = TypeCoercionHelper.ToBogDbString(a[1]);
            if (key == null) return null;
            if (a[0] is Dictionary<string, object?> sd)
                return sd.TryGetValue(key, out var v) ? v : null;
            if (a[0] is Dictionary<string, object> sod)
                return sod.TryGetValue(key, out var v) ? v : null;
            return null;
        };

        r["keys"] = a =>
        {
            if (a.Length < 1) return null;
            if (a[0] is Dictionary<string, object?> sd)
                return (object?)sd.Keys.Cast<object?>().ToList();
            if (a[0] is Dictionary<string, object> sod)
                return (object?)sod.Keys.Cast<object?>().ToList();
            return null;
        };
    }
}
