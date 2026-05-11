using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Cast;

/// <summary>
/// Type casting scalar functions with null-safe semantics.
/// C++ parity: src/function/cast/
/// </summary>
internal static class CastFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── Integer casts ──────────────────────────────────────────────────────
        r["tointeger"]    = r["toint"]    = r["int"]    = r["to_int"]   =
        r["to_int64"]     = r["int64"]    = r["bigint"] =
            a => Safe(() => (object?)TypeCoercionHelper.ToInt64(a.Length >= 1 ? a[0] : null));

        r["to_int32"]  = r["int32"]  = r["integer"] =
            a => Safe(() => (object?)(int)TypeCoercionHelper.ToInt64(a.Length >= 1 ? a[0] : null));

        r["to_int16"]  = r["int16"]  = r["smallint"] =
            a => Safe(() => (object?)(short)TypeCoercionHelper.ToInt64(a.Length >= 1 ? a[0] : null));

        r["to_int8"]   = r["int8"]   = r["tinyint"] =
            a => Safe(() => (object?)(sbyte)TypeCoercionHelper.ToInt64(a.Length >= 1 ? a[0] : null));

        r["to_uint64"] = r["uint64"] = r["ubigint"] =
            a => Safe(() => (object?)(ulong)TypeCoercionHelper.ToInt64(a.Length >= 1 ? a[0] : null));

        r["to_uint32"] = r["uint32"] = r["uinteger"] =
            a => Safe(() => (object?)(uint)TypeCoercionHelper.ToInt64(a.Length >= 1 ? a[0] : null));

        r["to_uint16"] = r["uint16"] = r["usmallint"] =
            a => Safe(() => (object?)(ushort)TypeCoercionHelper.ToInt64(a.Length >= 1 ? a[0] : null));

        r["to_uint8"]  = r["uint8"]  = r["utinyint"] =
            a => Safe(() => (object?)(byte)TypeCoercionHelper.ToInt64(a.Length >= 1 ? a[0] : null));

        // ── Float casts ────────────────────────────────────────────────────────
        r["tofloat"]   = r["to_float"]   = r["float"]   = r["real"]    =
        r["tododuble"] = r["todouble"]   = r["to_double"]  = r["double"]  = r["decimal"] =
            a => Safe(() => (object?)TypeCoercionHelper.ToDouble(a.Length >= 1 ? a[0] : null));
        r["to_float32"] = r["float32"] = r["real32"] =
            a => Safe(() => (object?)(float)TypeCoercionHelper.ToDouble(a.Length >= 1 ? a[0] : null));

        // ── Bool casts ─────────────────────────────────────────────────────────
        r["tobool"]    = r["to_bool"]    = r["boolean"] = r["bool"] =
            a => Safe(() => (object?)TypeCoercionHelper.ToBool(a.Length >= 1 ? a[0] : null));

        // ── String casts ───────────────────────────────────────────────────────
        r["tostring"]  = r["to_string"]  = r["str"]     = r["string"] = r["varchar"] =
            a => Safe(() => (object?)TypeCoercionHelper.ToBogDbString(a.Length >= 1 ? a[0] : null));

        // ── Date / timestamp casts ────────────────────────────────────────────
        r["to_date"]      = a =>
        {
            var s = TypeCoercionHelper.ToBogDbString(a.Length >= 1 ? a[0] : null);
            if (s == null) return null;
            return DateTime.TryParse(s, out var dt) ? (object?)dt.ToString("yyyy-MM-dd") : null;
        };
        r["to_timestamp"] = a =>
        {
            var s = TypeCoercionHelper.ToBogDbString(a.Length >= 1 ? a[0] : null);
            if (s == null) return null;
            return DateTime.TryParse(s, out var dt) ? (object?)dt.ToString("o") : null;
        };
        r["to_interval"] = a =>
            Safe(() => TypeCoercionHelper.TryParseInterval(a.Length >= 1 ? a[0] : null, out var interval)
                ? (object?)interval
                : null);

        // ── Generic cast(val, type) ────────────────────────────────────────────
        r["cast"] = a =>
        {
            if (a.Length < 2) return null;
            var typeName = (TypeCoercionHelper.ToBogDbString(a[1]) ?? "").ToLowerInvariant().Trim();
            return typeName switch
            {
                "int" or "int64" or "bigint"
                    => Safe(() => (object?)TypeCoercionHelper.ToInt64(a[0])),
                "integer" or "int32"
                    => Safe(() => (object?)(int)TypeCoercionHelper.ToInt64(a[0])),
                "smallint" or "int16"
                    => Safe(() => (object?)(short)TypeCoercionHelper.ToInt64(a[0])),
                "float" or "double" or "real" or "decimal"
                    => Safe(() => (object?)TypeCoercionHelper.ToDouble(a[0])),
                "float32" or "real32"
                    => Safe(() => (object?)(float)TypeCoercionHelper.ToDouble(a[0])),
                "bool" or "boolean"
                    => Safe(() => (object?)TypeCoercionHelper.ToBool(a[0])),
                "string" or "varchar" or "text"
                    => Safe(() => (object?)TypeCoercionHelper.ToBogDbString(a[0])),
                "date"
                    => Safe(() =>
                    {
                        var s = TypeCoercionHelper.ToBogDbString(a[0]);
                        return DateTime.TryParse(s, out var dt) ? (object?)dt.ToString("yyyy-MM-dd") : null;
                    }),
                "timestamp" or "datetime"
                    => Safe(() =>
                    {
                        var s = TypeCoercionHelper.ToBogDbString(a[0]);
                        return DateTime.TryParse(s, out var dt) ? (object?)dt.ToString("o") : null;
                    }),
                "interval"
                    => Safe(() => TypeCoercionHelper.TryParseInterval(a[0], out var interval)
                        ? (object?)interval
                        : null),
                "int128"
                    => Safe(() => (object?)(decimal)TypeCoercionHelper.ToDouble(a[0])),
                "uint128"
                    => Safe(() =>
                    {
                        var d = TypeCoercionHelper.ToDouble(a[0]);
                        return d < 0 ? null : (object?)(decimal)d;
                    }),
                "serial"
                    => Safe(() => (object?)TypeCoercionHelper.ToInt64(a[0])),
                "uuid"
                    => Safe(() =>
                    {
                        var s = TypeCoercionHelper.ToBogDbString(a[0]);
                        return Guid.TryParse(s, out var guid) ? (object?)guid.ToString("D") : null;
                    }),
                "blob"
                    => Safe(() =>
                    {
                        var s = TypeCoercionHelper.ToBogDbString(a[0]);
                        return s == null ? null : (object?)System.Text.Encoding.UTF8.GetBytes(s);
                    }),
                _ => null
            };
        };

        // ── try_cast (alias of safe cast) ────────────────────────────────────
        r["try_cast"] = r["cast"];

        // ── Exotic type casts (C++ parity) ──────────────────────────────────

        // INT128 / UINT128 — C++ has native 128-bit integer types.
        // C# maps these to decimal for max precision within managed types.
        r["to_int128"] = a => Safe(() =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            var d = TypeCoercionHelper.ToDouble(a[0]);
            return (object?)(decimal)d;
        });
        r["to_uint128"] = a => Safe(() =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            var d = TypeCoercionHelper.ToDouble(a[0]);
            if (d < 0) return null;
            return (object?)(decimal)d;
        });

        // SERIAL — auto-incrementing integer type; cast returns INT64.
        r["to_serial"] = a => Safe(() => (object?)TypeCoercionHelper.ToInt64(a.Length >= 1 ? a[0] : null));

        // UUID — stored as string in C#; cast ensures valid UUID format.
        r["to_uuid"] = r["uuid"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            var s = TypeCoercionHelper.ToBogDbString(a[0]);
            if (s == null) return null;
            return Guid.TryParse(s, out var guid) ? (object?)guid.ToString("D") : null;
        };

        // BLOB — Binary Large Object; cast from string/hex to byte[] representation.
        r["blob"] = r["to_blob"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            var s = TypeCoercionHelper.ToBogDbString(a[0]);
            if (s == null) return null;
            // Try hex decode (\\x prefix or pure hex), fallback to UTF-8 bytes
            if (s.StartsWith("\\x", StringComparison.OrdinalIgnoreCase))
                s = s.Replace("\\x", "");
            try
            {
                if (s.Length % 2 == 0 && s.All(c => "0123456789abcdefABCDEF".Contains(c)))
                {
                    var bytes = new byte[s.Length / 2];
                    for (var i = 0; i < bytes.Length; i++)
                        bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
                    return (object?)bytes;
                }
            }
            catch { /* fallthrough */ }
            return (object?)System.Text.Encoding.UTF8.GetBytes(s);
        };

        // ── Type check ────────────────────────────────────────────────────────
        r["typeof"]    = a => a.Length >= 1 ? (object?)TypeName(a[0]) : null;
        r["pg_typeof"] = r["typeof"];
    }

    private static object? Safe(Func<object?> fn)
    {
        try { return fn(); } catch { return null; }
    }

    private static string TypeName(object? v) => v switch
    {
        null         => "NULL",
        long         => "INT64",
        int          => "INT32",
        short        => "INT16",
        sbyte        => "INT8",
        ulong        => "UINT64",
        uint         => "UINT32",
        ushort       => "UINT16",
        byte         => "UINT8",
        double       => "DOUBLE",
        float        => "FLOAT",
        decimal      => "INT128",
        bool         => "BOOL",
        BogDbInterval => "INTERVAL",
        byte[]       => "BLOB",
        Guid         => "UUID",
        string       => "STRING",
        System.Collections.Generic.List<object?> => "LIST",
        System.Collections.Generic.Dictionary<string, object?> => "MAP",
        _ => v.GetType().Name.ToUpperInvariant()
    };
}
