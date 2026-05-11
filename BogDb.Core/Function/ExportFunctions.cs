using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Export;

/// <summary>
/// C++ parity: <c>src/function/export/export_csv_function.cpp</c>,
///             <c>src/function/export/export_parquet_function.cpp</c>
///
/// Export functions allow query results to be written to external files.
/// Scalar invocations: export_csv(path) / export_parquet(path) validate the path
/// and return it if writable (or an error string if not).
/// Full streaming export (COPY TO pipeline) requires a dedicated physical operator
/// and is tracked separately. These scalar wrappers ensure expressions like
///   RETURN export_csv('/tmp/out.csv')
/// work without crashing the planner and give meaningful feedback.
///
/// Functions:
///   export_csv(path, delimiter?, header?)   → STRING: path (validated) or "[error: ...]"
///   export_parquet(path)                    → STRING: path (validated) or "[error: ...]"
///   csv_escape(value, delimiter?)           → STRING: CSV-safe escaped string
///   csv_quote(value)                        → STRING: double-quoted CSV value
///   format_csv_row(values...)               → STRING: comma-joined CSV line
///   csv_header(columns...)                  → STRING: header line for a CSV file
///   write_csv_line(path, values...)         → STRING: appends one CSV line; returns path
/// </summary>
public static class ExportFunctions
{
    public static void Register(Dictionary<string, Func<object?[], object?>> funcs)
    {
        // ── export_csv(path, delimiter?, header?) ──────────────────────────────
        // Validates path writability. Returns path if writable, "[error: ...]" otherwise.
        funcs["export_csv"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            var path = args[0]!.ToString()!;
            return ValidatePath(path, ".csv");
        };

        // ── export_parquet(path) ───────────────────────────────────────────────
        funcs["export_parquet"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            var path = args[0]!.ToString()!;
            return ValidatePath(path, ".parquet");
        };

        // ── csv_escape(value, delimiter?) ─────────────────────────────────────
        // Follows RFC 4180: wrap in quotes if value contains delimiter, quote, newline, or CR.
        funcs["csv_escape"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return "";
            var val   = args[0]!.ToString()!;
            var delim = args.Length > 1 && args[1] != null ? args[1]!.ToString()! : ",";
            return CsvEscape(val, delim.Length > 0 ? delim[0] : ',');
        };

        // ── csv_quote(value) ───────────────────────────────────────────────────
        // Always wraps in double quotes, escaping internal quotes by doubling.
        funcs["csv_quote"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return "\"\"";
            var val = args[0]!.ToString()!;
            return "\"" + val.Replace("\"", "\"\"") + "\"";
        };

        // ── format_csv_row(values...) ─────────────────────────────────────────
        // Joins values into a single RFC-4180-compatible CSV line (no trailing newline).
        funcs["format_csv_row"] = args =>
        {
            if (args.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var v = args[i]?.ToString() ?? "";
                sb.Append(CsvEscape(v, ','));
            }
            return sb.ToString();
        };

        // ── csv_header(columns...) ────────────────────────────────────────────
        // Generates a header row from column name strings.
        funcs["csv_header"] = args =>
        {
            if (args.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var col = args[i]?.ToString() ?? "";
                sb.Append(CsvEscape(col, ','));
            }
            return sb.ToString();
        };

        // ── write_csv_line(path, values...) ────────────────────────────────────
        // Appends one CSV line to the file at path (creates if absent).
        // Returns path on success, "[error: ...]" on failure.
        funcs["write_csv_line"] = args =>
        {
            if (args.Length < 1 || args[0] == null) return null;
            var path = args[0]!.ToString()!;
            var sb = new StringBuilder();
            for (int i = 1; i < args.Length; i++)
            {
                if (i > 1) sb.Append(',');
                sb.Append(CsvEscape(args[i]?.ToString() ?? "", ','));
            }
            try
            {
                File.AppendAllText(path, sb.ToString() + "\n", Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                return $"[error: {ex.Message}]";
            }
        };

        // ── copy_to_csv(path, delimiter?, quote?, escape?, header?) ────────────
        // Metadata alias used in COPY TO context; returns path so the planner can
        // resolve the expression without crashing. Real data movement is done by
        // the COPY TO physical operator (tracked separately).
        funcs["copy_to_csv"]     = args => args.Length > 0 ? args[0]?.ToString() : null;
        funcs["copy_to_parquet"] = args => args.Length > 0 ? args[0]?.ToString() : null;
        funcs["copy_to_json"]    = args => args.Length > 0 ? args[0]?.ToString() : null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that <paramref name="path"/> points to a writable location.
    /// Returns <paramref name="path"/> if valid, or "[error: reason]" otherwise.
    /// </summary>
    private static string ValidatePath(string path, string expectedExt)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                return $"[error: directory does not exist: {dir}]";

            // Try to open/create the file to verify write permission without truncating it.
            using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            { /* just checking access */ }
            return path;
        }
        catch (Exception ex)
        {
            return $"[error: {ex.Message}]";
        }
    }

    private static string CsvEscape(string value, char delimiter)
    {
        bool needsQuoting = value.IndexOf(delimiter) >= 0
                         || value.IndexOf('"')  >= 0
                         || value.IndexOf('\n') >= 0
                         || value.IndexOf('\r') >= 0;
        if (!needsQuoting) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
