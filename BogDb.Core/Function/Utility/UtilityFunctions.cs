using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Utility;

/// <summary>
/// Utility / conditional / identity scalar functions.
/// C++ parity: src/function/utility_functions.cpp
/// </summary>
internal static class UtilityFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── Null-conditional ───────────────────────────────────────────────────
        r["coalesce"]  = a => a.FirstOrDefault(x => x != null);
        r["ifnull"]    = a => a.Length >= 2 ? (a[0] ?? a[1]) : null;
        r["nvl"]       = r["ifnull"];

        // nvl2(val, a, b) → a if val is not null, else b
        r["nvl2"] = a => a.Length >= 3
            ? (a[0] != null ? a[1] : a[2])
            : null;

        r["nullif"] = a => a.Length >= 2 && Equals(
                               TypeCoercionHelper.Normalize(a[0]),
                               TypeCoercionHelper.Normalize(a[1])) ? null : (a.Length >= 1 ? a[0] : null);

        r["if"]  = a => a.Length >= 3
            ? (TypeCoercionHelper.ToBool(a[0]) ? a[1] : a[2])
            : null;
        r["iff"] = r["if"];

        // equal_null(a, b) — null-safe equality: null = null → true
        // C++ parity: EQUALS with null semantics for MATCH equality predicates
        r["equal_null"] = a => a.Length >= 2
            ? (object?)(TypeCoercionHelper.Normalize(a[0]) == null
                ? TypeCoercionHelper.Normalize(a[1]) == null
                : Equals(TypeCoercionHelper.Normalize(a[0]),
                         TypeCoercionHelper.Normalize(a[1])))
            : null;

        // ── UUID / random ──────────────────────────────────────────────────────
        r["gen_random_uuid"]   = _ => (object?)Guid.NewGuid().ToString();
        r["uuid"]              = r["gen_random_uuid"];
        r["random"]            = _ => (object?)Random.Shared.NextDouble();
        r["rand"]              = r["random"];
        r["random_int"]        = a => a.Length >= 1
                                   ? (object?)(long)Random.Shared.Next(0, (int)TypeCoercionHelper.ToInt64(a[0]))
                                   : null;

        // ── Hash ──────────────────────────────────────────────────────────────
        r["hash"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            // Consistent hash: 64-bit from 32-bit GetHashCode, sign-extended
            return (object?)(long)a[0]!.GetHashCode();
        };
        r["md5"] = a =>
        {
            if (a.Length < 1) return null;
            var s = TypeCoercionHelper.ToBogDbString(a[0]) ?? "";
            using var md5 = System.Security.Cryptography.MD5.Create();
            var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
            return (object?)Convert.ToHexString(bytes).ToLowerInvariant();
        };
        r["sha256"] = a =>
        {
            if (a.Length < 1) return null;
            var s = TypeCoercionHelper.ToBogDbString(a[0]) ?? "";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
            return (object?)Convert.ToHexString(bytes).ToLowerInvariant();
        };
        r["sha1"] = a =>
        {
            if (a.Length < 1) return null;
            var s = TypeCoercionHelper.ToBogDbString(a[0]) ?? "";
            using var sha = System.Security.Cryptography.SHA1.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
            return (object?)Convert.ToHexString(bytes).ToLowerInvariant();
        };
        r["crc32"] = a =>
        {
            if (a.Length < 1) return null;
            var s = TypeCoercionHelper.ToBogDbString(a[0]) ?? "";
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            return (object?)(long)Crc32(bytes);
        };

        // ── Type / identity ───────────────────────────────────────────────────
        r["type"]  = a => a.Length >= 1 ? (object?)a[0]?.GetType().Name : null;
        r["label"] = r["type"];
        r["id"]    = a => a.Length >= 1 ? a[0] : null;

        // row_number / rank / dense_rank: handled by WindowFunctionService.
        // These stubs exist so expressions that reference them in non-window context
        // don't throw "function not found". PhysicalAggregate/Window supply real values.
        r["row_number"] = _ => null;
        r["rank"]       = _ => null;
        r["dense_rank"] = _ => null;
        r["percent_rank"] = _ => null;
        r["cume_dist"]    = _ => null;
        r["ntile"]        = _ => null;

        // count: aggregate stub — PhysicalAggregate injects real accumulated value
        r["count"]      = _ => 1L;
        r["count_star"] = _ => 1L;

        // version() — matches TableFunctions.BogDbNgVersion
        r["version"] = _ => (object?)BogDb.Core.Function.Table.TableFunctions.BogDbNgVersion;

        // error(msg) — raises an exception; mirrors BogDb C++ behavior
        r["error"] = a =>
        {
            var msg = a.Length >= 1 ? TypeCoercionHelper.ToBogDbString(a[0]) ?? "error()" : "error()";
            throw new InvalidOperationException(msg);
        };

        // sleep(ms) — pauses execution (useful for integration tests / rate limiting)
        r["sleep"] = a =>
        {
            if (a.Length >= 1 && a[0] != null)
                System.Threading.Thread.Sleep((int)TypeCoercionHelper.ToInt64(a[0]));
            return null;
        };

        // ── Boolean utility ───────────────────────────────────────────────────
        r["bool_and"] = a => a.Length >= 2
            ? (object?)(TypeCoercionHelper.ToBool(a[0]) && TypeCoercionHelper.ToBool(a[1]))
            : null;
        r["bool_or"] = a => a.Length >= 2
            ? (object?)(TypeCoercionHelper.ToBool(a[0]) || TypeCoercionHelper.ToBool(a[1]))
            : null;
        r["bool_xor"] = a => a.Length >= 2
            ? (object?)(TypeCoercionHelper.ToBool(a[0]) ^ TypeCoercionHelper.ToBool(a[1]))
            : null;

        // ── Greatest / Least ─────────────────────────────────────────────────
        // C++ parity: src/function/utility/
        r["greatest"] = a =>
        {
            object? best = null;
            foreach (var v in a)
            {
                if (v == null) continue;
                if (best == null) { best = v; continue; }
                var cmp = List.BogDbComparer.Instance.Compare(
                    TypeCoercionHelper.Normalize(v),
                    TypeCoercionHelper.Normalize(best));
                if (cmp > 0) best = v;
            }
            return best;
        };
        r["least"] = a =>
        {
            object? best = null;
            foreach (var v in a)
            {
                if (v == null) continue;
                if (best == null) { best = v; continue; }
                var cmp = List.BogDbComparer.Instance.Compare(
                    TypeCoercionHelper.Normalize(v),
                    TypeCoercionHelper.Normalize(best));
                if (cmp < 0) best = v;
            }
            return best;
        };

        // constant_or_null(val, isNull) — returns val if isNull is false/null, else null
        r["constant_or_null"] = a =>
        {
            if (a.Length < 2) return null;
            return a[1] == null ? a[0] : null;
        };

        // current_setting(name) — returns the value of a database option
        // In the C# engine, this returns null (options are not yet query-accessible)
        r["current_setting"] = a => null;

        // db_version() / catalog_version() — version strings
        r["db_version"]      = _ => (object?)BogDb.Core.Function.Table.TableFunctions.BogDbNgVersion;
        r["catalog_version"] = _ => (object?)1L;  // static catalog version

        // typeof(val) — returns the BogDb type name of the value
        r["typeof"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return (object?)"NULL";
            return a[0] switch
            {
                long    => (object?)"INT64",
                double  => (object?)"DOUBLE",
                bool    => (object?)"BOOL",
                string  => (object?)"STRING",
                System.Collections.Generic.List<object?> => (object?)"LIST",
                System.Collections.Generic.Dictionary<string, object> => (object?)"STRUCT",
                DateOnly  => (object?)"DATE",
                DateTime  => (object?)"TIMESTAMP",
                BogDbInterval => (object?)"INTERVAL",
                _ => (object?)a[0]!.GetType().Name.ToUpperInvariant()
            };
        };
    }

    /// <summary>Pure CRC-32 (Castagnoli table) for the crc32() function.</summary>
    private static uint Crc32(byte[] data)
    {
        const uint Poly = 0xEDB88320u;
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Poly : crc >> 1;
        }
        return ~crc;
    }
}
