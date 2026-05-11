using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BogDb.Core.Common;

namespace BogDb.Core.Function.String;

/// <summary>
/// String scalar functions (migrated + extended).
/// C++ parity: src/function/string/*.cpp
/// </summary>
internal static class StringFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── Predicates ──────────────────────────────────────────────────────────
        r["starts_with"]  = a => StrPred(a, (s, p) => s.StartsWith(p, StringComparison.Ordinal));
        r["startswith"]   = r["starts_with"];
        r["ends_with"]    = a => StrPred(a, (s, p) => s.EndsWith(p, StringComparison.Ordinal));
        r["endswith"]     = r["ends_with"];
        r["contains"]     = a => StrPred(a, (s, p) => s.Contains(p, StringComparison.Ordinal));
        r["regexp_matches"]  = a => StrPred(a, (s, p) => Regex.IsMatch(s, p));
        r["regexp_like"]     = r["regexp_matches"];
        r["regexp_full_match"] = a =>
        {
            if (a.Length < 2) return null;
            var s = Str(a[0]); var p = Str(a[1]);
            if (s == null || p == null) return null;
            return (object?)Regex.IsMatch(s, $"^(?:{p})$");
        };

        // ── Transform ──────────────────────────────────────────────────────────
        r["tolower"]  = a => S(a, s => s.ToLowerInvariant()); r["lower"] = r["tolower"];
        r["toupper"]  = a => S(a, s => s.ToUpperInvariant()); r["upper"] = r["toupper"];
        r["lcase"]    = r["tolower"];
        r["ucase"]    = r["toupper"];
        r["trim"]     = a => S(a, s => s.Trim());
        r["ltrim"]    = a => S(a, s => s.TrimStart());
        r["rtrim"]    = a => S(a, s => s.TrimEnd());
        r["reverse"]  = a => S(a, s => new string(s.Reverse().ToArray()));
        r["repeat"]   = a => a.Length >= 2
            ? (object?)RepeatStr(Str(a[0]), (int)TypeCoercionHelper.ToInt64(a[1]))
            : null;
        r["concat"]   = a => a.Length >= 2
            ? (object?)(Str(a[0]) + Str(a[1]))
            : null;
        r["||"]       = r["concat"];
        r["concat_ws"] = a =>
        {
            if (a.Length < 2) return null;
            var sep = Str(a[0]) ?? "";
            return (object?)string.Join(sep, a.Skip(1).Select(x => Str(x) ?? ""));
        };

        // ── Pad ────────────────────────────────────────────────────────────────
        r["lpad"] = a =>
        {
            if (a.Length < 2) return null;
            var s = Str(a[0]) ?? ""; var n = (int)TypeCoercionHelper.ToInt64(a[1]);
            var fill = a.Length >= 3 ? (Str(a[2]) ?? " ") : " ";
            return (object?)Pad(s, n, fill, left: true);
        };
        r["rpad"] = a =>
        {
            if (a.Length < 2) return null;
            var s = Str(a[0]) ?? ""; var n = (int)TypeCoercionHelper.ToInt64(a[1]);
            var fill = a.Length >= 3 ? (Str(a[2]) ?? " ") : " ";
            return (object?)Pad(s, n, fill, left: false);
        };

        // ── Slice / sub ────────────────────────────────────────────────────────
        r["substring"] = a => (object?)Substring(a); r["substr"] = r["substring"];
        r["left"]  = a =>
        {
            if (a.Length < 2) return null;
            var s = Str(a[0]); if (s == null) return null;
            var n = (int)TypeCoercionHelper.ToInt64(a[1]);
            return (object?)s.Substring(0, System.Math.Min(n, s.Length));
        };
        r["right"] = a =>
        {
            if (a.Length < 2) return null;
            var s = Str(a[0]); if (s == null) return null;
            var n = (int)TypeCoercionHelper.ToInt64(a[1]);
            return (object?)(n >= s.Length ? s : s.Substring(s.Length - n));
        };

        // ── Replace / regex ────────────────────────────────────────────────────
        r["replace"] = a => a.Length >= 3
            ? (object?)Str(a[0])?.Replace(Str(a[1]) ?? "", Str(a[2]) ?? "")
            : null;
        r["regexp_replace"] = a => a.Length >= 3
            ? (object?)Regex.Replace(Str(a[0]) ?? "", Str(a[1]) ?? "", Str(a[2]) ?? "")
            : null;
        r["regexp_extract"] = a =>
        {
            if (a.Length < 2) return null;
            var m = Regex.Match(Str(a[0]) ?? "", Str(a[1]) ?? "");
            if (!m.Success) return null;
            var grp = a.Length >= 3 ? (int)TypeCoercionHelper.ToInt64(a[2]) : 0;
            return (object?)(grp < m.Groups.Count ? m.Groups[grp].Value : null);
        };
        r["regexp_extract_all"] = a =>
        {
            if (a.Length < 2) return null;
            var s = Str(a[0]); var p = Str(a[1]);
            if (s == null || p == null) return null;
            var grp = a.Length >= 3 ? (int)TypeCoercionHelper.ToInt64(a[2]) : 0;
            var matches = Regex.Matches(s, p);
            var list = new System.Collections.Generic.List<object?>(matches.Count);
            foreach (Match m in matches)
            {
                if (!m.Success) continue;
                list.Add(grp >= 0 && grp < m.Groups.Count ? m.Groups[grp].Value : null);
            }
            return (object?)list;
        };
        r["regexp_split_to_array"] = a =>
        {
            if (a.Length < 2) return null;
            var s = Str(a[0]); var p = Str(a[1]);
            if (s == null || p == null) return null;
            var parts = Regex.Split(s, p);
            return (object?)new System.Collections.Generic.List<object?>(parts.Select(x => (object?)x));
        };

        // ── Split ──────────────────────────────────────────────────────────────
        r["split"]        = a => Split(a);
        r["string_split"] = r["split"];
        r["split_part"] = a =>
        {
            if (a.Length < 3) return null;
            var s = Str(a[0]); var d = Str(a[1]);
            if (s == null || d == null) return null;
            var idx = (int)TypeCoercionHelper.ToInt64(a[2]);
            if (idx <= 0) return null;
            var parts = s.Split(new[] { d }, StringSplitOptions.None);
            return (object?)(idx <= parts.Length ? parts[idx - 1] : "");
        };

        // ── Size / length ──────────────────────────────────────────────────────
        r["size"]             = a => Len(a);
        r["length"]           = r["size"];
        r["strlen"]           = r["size"];
        r["char_length"]      = r["size"];
        r["character_length"] = r["size"];
        r["position"] = a =>
        {
            if (a.Length < 2) return null;
            var s = Str(a[0]); var p = Str(a[1]);
            if (s == null || p == null) return null;
            var idx = s.IndexOf(p, StringComparison.Ordinal);
            return (object?)(long)(idx >= 0 ? idx + 1 : 0);
        };
        r["locate"] = r["position"];
        r["instr"]  = r["position"];

        // ── Char codes ─────────────────────────────────────────────────────────
        r["ascii"]   = a => a.Length >= 1 && Str(a[0])?.Length > 0
            ? (object?)(long)Str(a[0])![0] : null;
        r["unicode"] = r["ascii"];
        r["chr"]     = a => a.Length >= 1
            ? (object?)((char)TypeCoercionHelper.ToInt64(a[0])).ToString() : null;

        // ── Distance ───────────────────────────────────────────────────────────
        r["levenshtein"]      = a => a.Length >= 2
            ? (object?)(long)Levenshtein(Str(a[0]) ?? "", Str(a[1]) ?? "")
            : null;
        r["editdistance"]     = r["levenshtein"];

        // ── Misc ───────────────────────────────────────────────────────────────
        r["string_agg"] = a => a.Length >= 2
            ? (object?)string.Join(Str(a[1]) ?? "", ((System.Collections.Generic.IEnumerable<object?>)a).Select(Str))
            : null;

        // printf/format: real %s/%d/%i/%f/%g/%e/%o/%x positional substitution
        r["printf"] = a => a.Length >= 1 ? (object?)SprintF(Str(a[0]) ?? "", a.Skip(1).ToArray()) : null;
        r["format"] = r["printf"];
        r["format_string"] = r["printf"];

        r["initcap"] = a => S(a, s => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower()));

        // ── Case aliases ───────────────────────────────────────────────────────
        r["strip"] = r["trim"];
        r["prefix"] = r["starts_with"];   // C++ parity alias
        r["suffix"] = r["ends_with"];     // C++ parity alias
        r["str_split"] = r["split"];      // C++ parity alias
        r["string_to_array"] = r["split"]; // C++ parity alias

        // ── Encoding ───────────────────────────────────────────────────────────
        r["base64_encode"] = a => a.Length >= 1
            ? (object?)Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Str(a[0]) ?? ""))
            : null;
        r["base64_decode"] = a =>
        {
            if (a.Length < 1) return null;
            try { return (object?)System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Str(a[0]) ?? "")); }
            catch { return null; }
        };
        r["url_encode"] = a => a.Length >= 1
            ? (object?)Uri.EscapeDataString(Str(a[0]) ?? "") : null;
        r["url_decode"] = a => a.Length >= 1
            ? (object?)Uri.UnescapeDataString(Str(a[0]) ?? "") : null;
        r["to_hex"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            var n = TypeCoercionHelper.ToInt64(a[0]);
            return (object?)n.ToString("x");
        };
        r["from_hex"] = a =>
        {
            if (a.Length < 1) return null;
            var s = Str(a[0])?.TrimStart('0', 'x', 'X') ?? "";
            try { return (object?)Convert.ToInt64(s, 16); } catch { return null; }
        };
        r["bit_length"]   = a => a.Length >= 1
            ? (object?)(long)(System.Text.Encoding.UTF8.GetByteCount(Str(a[0]) ?? "") * 8) : null;
        r["octet_length"] = a => a.Length >= 1
            ? (object?)(long)System.Text.Encoding.UTF8.GetByteCount(Str(a[0]) ?? "") : null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static object? S(object?[] a, Func<string, string> f)
        => a.Length >= 1 && Str(a[0]) is string s ? (object?)f(s) : null;

    private static object? StrPred(object?[] a, Func<string, string, bool> f)
    {
        if (a.Length < 2) return null;
        var s = Str(a[0]); var p = Str(a[1]);
        return s != null && p != null ? (object?)f(s, p) : null;
    }

    private static object? Len(object?[] a)
        => a.Length >= 1 ? (object?)(long)(Str(a[0])?.Length ?? 0) : null;

    private static object? Split(object?[] a)
    {
        if (a.Length < 2) return null;
        var s = Str(a[0]); var d = Str(a[1]);
        if (s == null || d == null) return null;
        return (object?)new System.Collections.Generic.List<object?>(
            s.Split(new[] { d }, StringSplitOptions.None).Select(x => (object?)x));
    }

    private static string? Substring(object?[] a)
    {
        var s = Str(a[0]); if (s == null) return null;
        var start = (int)TypeCoercionHelper.ToInt64(a[1]);
        if (start < 0) start = System.Math.Max(0, s.Length + start);
        if (start > s.Length) return "";
        if (a.Length >= 3)
        {
            var len = (int)TypeCoercionHelper.ToInt64(a[2]);
            return s.Substring(start, System.Math.Min(len, s.Length - start));
        }
        return s.Substring(start);
    }

    private static string? Pad(string s, int n, string fill, bool left)
    {
        if (n <= 0) return "";
        if (s.Length >= n) return s[..n];
        if (string.IsNullOrEmpty(fill)) fill = " ";
        int needed = n - s.Length;
        var sb = new StringBuilder();
        while (sb.Length < needed) sb.Append(fill);
        var pad = sb.ToString()[..needed]; // exactly needed chars
        return left ? pad + s : s + pad;
    }

    private static string? RepeatStr(string? s, int n)
    {
        if (s == null || n <= 0) return s ?? "";
        var sb = new StringBuilder(s.Length * n);
        for (int i = 0; i < n; i++) sb.Append(s);
        return sb.ToString();
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = a[i - 1] == b[j - 1] ? d[i - 1, j - 1]
                    : 1 + System.Math.Min(d[i - 1, j - 1], System.Math.Min(d[i - 1, j], d[i, j - 1]));
        return d[a.Length, b.Length];
    }

    private static string? Str(object? v) => TypeCoercionHelper.ToBogDbString(v);

    /// <summary>
    /// Minimal BogDb-compatible sprintf: handles %s %d %i %f %g %e %o %x %%
    /// Positional — each % spec consumes the next argument in order.
    /// </summary>
    private static string SprintF(string fmt, object?[] args)
    {
        var sb = new StringBuilder();
        int argIdx = 0;
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] != '%' || i + 1 >= fmt.Length) { sb.Append(fmt[i]); continue; }
            if (fmt[i + 1] == '%') { sb.Append('%'); i++; continue; }

            var j = i + 1;
            while (j < fmt.Length && "-+ #0".IndexOf(fmt[j]) >= 0) j++;
            while (j < fmt.Length && char.IsDigit(fmt[j])) j++;

            int? precision = null;
            if (j < fmt.Length && fmt[j] == '.')
            {
                j++;
                int precisionStart = j;
                while (j < fmt.Length && char.IsDigit(fmt[j])) j++;
                precision = j > precisionStart
                    ? int.Parse(fmt[precisionStart..j], System.Globalization.CultureInfo.InvariantCulture)
                    : 0;
            }

            if (j >= fmt.Length)
            {
                sb.Append('%');
                break;
            }

            char spec = fmt[j];

            object? arg = argIdx < args.Length ? args[argIdx++] : null;
            switch (spec)
            {
                case 's': sb.Append(TypeCoercionHelper.ToBogDbString(arg) ?? ""); break;
                case 'd': case 'i': sb.Append(TypeCoercionHelper.ToInt64(arg)); break;
                case 'f':
                    sb.Append(TypeCoercionHelper.ToDouble(arg).ToString(
                        $"F{precision ?? 6}", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case 'g':
                    sb.Append(TypeCoercionHelper.ToDouble(arg).ToString(
                        $"G{(precision.HasValue ? System.Math.Max(precision.Value, 15) : 15)}",
                        System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case 'e':
                    sb.Append(TypeCoercionHelper.ToDouble(arg).ToString(
                        $"E{precision ?? 6}", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant());
                    break;
                case 'o': sb.Append(Convert.ToString(TypeCoercionHelper.ToInt64(arg), 8)); break;
                case 'x': sb.Append(TypeCoercionHelper.ToInt64(arg).ToString("x")); break;
                case 'X': sb.Append(TypeCoercionHelper.ToInt64(arg).ToString("X")); break;
                default:
                    sb.Append(fmt[i..(j + 1)]);
                    argIdx--;
                    break;
            }
            i = j; // skip the full format token
        }
        return sb.ToString();
    }
}
