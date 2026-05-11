using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BogDb.Core.Main;
using BogDb.Core.Main.QueryResult;

namespace BogDb.Core.Processor.Operator.Window;

/// <summary>
/// Orchestrates window function query execution:
///   1. Detect OVER () clause in query string
///   2. Parse out all window function specs
///   3. Rewrite query to a base form (no window expressions)
///   4. Execute base query to get all rows
///   5. Apply each window function using WindowEvaluator
///   6. Project to output columns and return augmented QueryResult
///
/// Design: pre-ANTLR4 interception — no grammar changes needed.
/// C++ parity: window_aggregate_function_executor.cpp (conceptual parity).
/// </summary>
public static class WindowFunctionService
{
    // ── Entry point ───────────────────────────────────────────────────────────

    public static bool IsWindowQuery(string query)
        => WindowQueryParser.ContainsWindowFunctions(query);

    /// <summary>Execute a query containing window functions, returning complete QueryResult.</summary>
    public static QueryResult Execute(BogConnection conn, string query)
    {
        try
        {
            return ExecuteInternal(conn, query);
        }
        catch (Exception ex)
        {
            return QueryResult.FromError($"Window function error: {ex.Message}");
        }
    }

    // ── Internal pipeline ─────────────────────────────────────────────────────

    private static QueryResult ExecuteInternal(BogConnection conn, string query)
    {
        // 1. Parse window specs
        var specs = WindowQueryParser.Parse(query);
        if (specs.Count == 0)
            return QueryResult.FromError("Window function detected but could not be parsed.");

        // 2. Build base query — strip OVER clauses, keep regular RETURN items
        var (baseQuery, regularAliases, needsExtraColumns) = BuildBaseQuery(query, specs);

        // 3. Execute base query — this call goes through the normal (non-window) path
        var baseResult = conn.Query(baseQuery);
        if (!baseResult.IsSuccess)
            return QueryResult.FromError($"Window base query failed: {baseResult.ErrorMessage}");

        // 4. Collect all rows as dicts keyed by column expression / alias
        var allRows = CollectRows(baseResult, regularAliases, needsExtraColumns);

        // 5. Apply each window function (mutates rows in-place adding output alias keys)
        foreach (var spec in specs)
            WindowEvaluator.Apply(allRows, spec);

        // 6. Build final output: regular cols + window function cols, drop extras
        var outputAliases = new List<string>(regularAliases);
        foreach (var spec in specs) outputAliases.Add(spec.OutputAlias);

        var finalRows = allRows.Select(row => ProjectRow(row, outputAliases)).ToList();
        return QueryResult.FromOrderedRows(finalRows, outputAliases);
    }

    // ── Base query construction ────────────────────────────────────────────────

    private static (string baseQuery, List<string> regularAliases, List<string> extraExprs)
        BuildBaseQuery(string query, List<WindowSpec> specs)
    {
        // Find RETURN clause
        var returnIdx = IndexOfReturn(query);
        if (returnIdx < 0)
            throw new InvalidOperationException("No RETURN clause found in window query.");

        var preReturn  = query[..(returnIdx + "RETURN".Length)];
        var afterReturn = query[(returnIdx + "RETURN".Length)..].Trim();

        // Separate ORDER BY / LIMIT / SKIP / WHERE tail that follows RETURN
        var (returnBody, tail) = SplitReturnTail(afterReturn);

        // Split the RETURN body into items (comma-separated, paren-aware)
        var items = WindowQueryParser.SplitTopLevelCommas(returnBody);

        // Classify items: window vs regular
        var regularItems  = new List<string>();
        var regularAliases = new List<string>();
        var windowRawTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in specs)
            windowRawTokens.Add(spec.RawToken);

        foreach (var item in items)
        {
            var trimmed = item.Trim();
            if (IsWindowItem(trimmed))
                continue; // skip window items from base RETURN

            regularItems.Add(trimmed);
            regularAliases.Add(ExtractAlias(trimmed));
        }

        // Collect expressions needed for partition/order that are NOT already in regular items
        var extraExprs = new List<string>();
        var regularSet = new HashSet<string>(regularAliases, StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            foreach (var pExpr in spec.PartitionByExprs)
                if (!regularSet.Contains(pExpr) && !regularSet.Contains(ShortName(pExpr)) && !extraExprs.Contains(pExpr))
                    extraExprs.Add(pExpr);
            foreach (var oItem in spec.OrderByItems)
                if (!regularSet.Contains(oItem.Expression) && !regularSet.Contains(ShortName(oItem.Expression)) && !extraExprs.Contains(oItem.Expression))
                    extraExprs.Add(oItem.Expression);
        }

        // Build base RETURN items list
        var baseReturnItems = new List<string>(regularItems);
        foreach (var extra in extraExprs)
            baseReturnItems.Add($"{extra} AS {MakeExtraAlias(extra)}");

        if (baseReturnItems.Count == 0)
            throw new InvalidOperationException("Window query has no regular RETURN columns.");

        var baseReturn  = string.Join(", ", baseReturnItems);
        // Build base query WITHOUT ORDER BY / LIMIT (window needs all rows)
        // We preserve ORDER BY only if it's outside the RETURN (final result sort, not data sort)
        var baseQuery   = $"{preReturn} {baseReturn}";

        return (baseQuery, regularAliases, extraExprs);
    }

    private static bool IsWindowItem(string item)
    {
        // Heuristic: item contains ") OVER (" (with whitespace variants)
        int depth = 0;
        for (int i = 0; i < item.Length; i++)
        {
            if (item[i] == '(') { depth++; continue; }
            if (item[i] == ')') { depth--; continue; }
            if (depth == 0 &&
                i + 4 < item.Length &&
                string.Compare(item, i, "OVER", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                int j = i - 1;
                while (j >= 0 && item[j] == ' ') j--;
                if (j >= 0 && item[j] == ')') return true;
            }
        }
        return false;
    }

    // ── Row collection ────────────────────────────────────────────────────────

    private static List<Dictionary<string, object?>> CollectRows(
        QueryResult baseResult,
        List<string> regularAliases,
        List<string> extraExprs)
    {
        var rows = new List<Dictionary<string, object?>>();

        // Build combined column name list (regular + extra)
        var allAliases = new List<string>(regularAliases);
        foreach (var e in extraExprs) allAliases.Add(MakeExtraAlias(e));

        int colCount = allAliases.Count;

        while (baseResult.HasNext())
        {
            var bogdbRow = baseResult.GetNext();

            // Try to build dict from typed access
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < colCount; i++)
            {
                var alias  = allAliases[i];
                object? v;
                try { v = bogdbRow.GetValue(i); }
                catch { v = null; }
                dict[alias] = v;

                // Also register the dot-notation style (short name) for PARTITION BY resolution
                var shortName = ShortName(alias);
                if (shortName != alias) dict[shortName] = v;

                // Register extra exprs under their original expression string too
                if (i >= regularAliases.Count)
                {
                    var origExpr = extraExprs[i - regularAliases.Count];
                    dict[origExpr] = v;
                }
            }
            rows.Add(dict);
        }
        return rows;
    }

    // ── Output projection ──────────────────────────────────────────────────────

    private static Dictionary<string, object?> ProjectRow(
        Dictionary<string, object?> row,
        List<string> columns)
    {
        var out_ = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
            out_[col] = row.TryGetValue(col, out var v) ? v : null;
        return out_;
    }

    // ── Alias resolution helpers ──────────────────────────────────────────────

    private static string ExtractAlias(string item)
    {
        // "expr AS alias" → alias; "expr" → short name
        var asIdx = item.LastIndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
        if (asIdx >= 0) return item[(asIdx + 4)..].Trim();
        return ShortName(item);
    }

    private static string ShortName(string expr)
    {
        // "p.salary" → "salary"; "alias" → "alias"
        var dot = expr.LastIndexOf('.');
        return dot >= 0 ? expr[(dot + 1)..] : expr;
    }

    private static string MakeExtraAlias(string expr)
    {
        // Use a simple hash-based short identifier — avoids double-underscore
        // prefixes and special chars that may be invalid in some Cypher grammars.
        var safe = expr.Replace(".", "D").Replace("(", "").Replace(")", "")
                       .Replace(" ", "").Replace("*", "Ast").Replace("+", "")
                       .Replace("-", "").Replace("/", "").Replace(":", "C");
        // Ensure it starts with a letter
        if (safe.Length == 0 || !char.IsLetter(safe[0])) safe = "x" + safe;
        // Truncate to a reasonable length
        if (safe.Length > 20) safe = safe[..20];
        return "wfext" + safe.ToLowerInvariant();
    }

    private static int IndexOfReturn(string query)
    {
        int idx = 0;
        while (idx < query.Length)
        {
            var ri = query.IndexOf("RETURN", idx, StringComparison.OrdinalIgnoreCase);
            if (ri < 0) return -1;
            bool startOk = ri == 0 || !char.IsLetterOrDigit(query[ri - 1]);
            bool endOk   = ri + 6 >= query.Length || !char.IsLetterOrDigit(query[ri + 6]);
            if (startOk && endOk) return ri;
            idx = ri + 1;
        }
        return -1;
    }

    private static (string body, string tail) SplitReturnTail(string afterReturn)
    {
        // Stop at ORDER BY, LIMIT, SKIP that are at the top level (not inside parens)
        int depth = 0;
        var keywords = new[] { "ORDER BY", "LIMIT", "SKIP" };
        for (int i = 0; i < afterReturn.Length; i++)
        {
            if (afterReturn[i] == '(') { depth++; continue; }
            if (afterReturn[i] == ')') { depth--; continue; }
            if (depth != 0) continue;
            foreach (var kw in keywords)
            {
                if (i + kw.Length <= afterReturn.Length &&
                    string.Compare(afterReturn, i, kw, 0, kw.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    bool startOk = i == 0 || !char.IsLetterOrDigit(afterReturn[i - 1]);
                    if (startOk)
                        return (afterReturn[..i].Trim(), afterReturn[i..].Trim());
                }
            }
        }
        return (afterReturn.Trim(), string.Empty);
    }
}
