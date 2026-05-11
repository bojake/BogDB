using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BogDb.Core.Processor.Operator.Window;

/// <summary>
/// Parses window function expressions from a Cypher RETURN clause.
///
/// Recognised syntax (case-insensitive):
///   func ( args ) OVER ( [PARTITION BY exprs] [ORDER BY exprs [ASC|DESC]] ) [AS alias]
///
/// Supported function names: ROW_NUMBER, RANK, DENSE_RANK, NTILE, PERCENT_RANK,
///   CUME_DIST, LAG, LEAD, FIRST_VALUE, LAST_VALUE, NTH_VALUE,
///   SUM, COUNT, AVG, MIN, MAX.
/// </summary>
public static class WindowQueryParser
{
    // Detects whether a query contains at least one OVER ( ... ) window clause
    private static readonly Regex QuickCheck = new(
        @"\)\s+OVER\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ContainsWindowFunctions(string query)
        => QuickCheck.IsMatch(query);

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Extracts all window function specs from the original query's RETURN clause.
    /// Returns empty list if none found.
    /// </summary>
    public static List<WindowSpec> Parse(string query)
    {
        var returnClause = ExtractReturnClause(query);
        if (returnClause == null) return new List<WindowSpec>();
        return ParseReturnClause(returnClause);
    }

    // ── Return clause extraction ──────────────────────────────────────────────

    private static string? ExtractReturnClause(string query)
    {
        var idx = IndexOfReturnKeyword(query);
        if (idx < 0) return null;
        var afterReturn = query[(idx + "RETURN".Length)..].Trim();
        // Use depth-aware split so we don't cut at ORDER BY inside OVER(...)
        var (body, _) = SplitTailDepthAware(afterReturn);
        return body;
    }

    /// <summary>
    /// Splits a string at the first top-level (depth==0) occurrence of ORDER BY, LIMIT, SKIP,
    /// WHERE, or UNION. Ignores occurrences inside parentheses.
    /// </summary>
    private static (string body, string tail) SplitTailDepthAware(string s)
    {
        var keywords = new[] { "ORDER BY", "LIMIT", "SKIP", "WHERE", "UNION" };
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') { depth++; continue; }
            if (s[i] == ')') { depth--; continue; }
            if (depth != 0) continue;
            foreach (var kw in keywords)
            {
                if (i + kw.Length > s.Length) continue;
                if (!string.Equals(s.AsSpan(i, kw.Length).ToString(), kw, StringComparison.OrdinalIgnoreCase)) continue;
                bool startOk = i == 0 || !char.IsLetterOrDigit(s[i - 1]);
                bool endOk   = i + kw.Length >= s.Length || !char.IsLetterOrDigit(s[i + kw.Length]);
                if (startOk && endOk)
                    return (s[..i].Trim(), s[i..].Trim());
            }
        }
        return (s.Trim(), string.Empty);
    }

    private static int IndexOfReturnKeyword(string query)
    {
        // Find RETURN that is not inside a string literal or a MATCH keyword
        var idx = 0;
        while (idx < query.Length)
        {
            var ri = query.IndexOf("RETURN", idx, StringComparison.OrdinalIgnoreCase);
            if (ri < 0) return -1;
            // Make sure it's a word boundary on both sides
            bool startOk = ri == 0 || !char.IsLetterOrDigit(query[ri - 1]);
            bool endOk   = ri + 6 >= query.Length || !char.IsLetterOrDigit(query[ri + 6]);
            if (startOk && endOk) return ri;
            idx = ri + 1;
        }
        return -1;
    }

    // ── Item-level parsing ────────────────────────────────────────────────────

    private static List<WindowSpec> ParseReturnClause(string returnClause)
    {
        var specs = new List<WindowSpec>();
        var items = SplitTopLevelCommas(returnClause);
        int autoIdx = 0;

        foreach (var item in items)
        {
            var spec = TryParseWindowItem(item.Trim(), autoIdx);
            if (spec != null)
            {
                specs.Add(spec);
                autoIdx++;
            }
        }
        return specs;
    }

    private static WindowSpec? TryParseWindowItem(string item, int autoIdx)
    {
        // Pattern: FunctionName(args) OVER (overClause) [AS alias]
        // We scan for ")  OVER  (" — that distinguishes a window call
        var overPos = FindOverKeyword(item);
        if (overPos < 0) return null;

        // Extract function name + args from text BEFORE " OVER "
        var funcPart = item[..overPos].Trim();
        var (funcName, funcArgs) = ParseFunctionCall(funcPart);
        if (funcName == null) return null;

        // Extract the text AFTER "OVER "
        var afterOver = SkipWhitespace(item, overPos + 4); // "OVER".Length = 4
        if (afterOver >= item.Length || item[afterOver] != '(') return null;

        // Find matching closing paren of OVER clause
        var overClauseEnd = FindMatchingParen(item, afterOver);
        if (overClauseEnd < 0) return null;

        var overClause = item[(afterOver + 1)..overClauseEnd].Trim();
        var (partitionBy, orderBy, frame) = ParseOverClause(overClause);

        // Optional AS alias after OVER(...)
        var afterSpec = item[(overClauseEnd + 1)..].Trim();
        string alias;
        if (afterSpec.StartsWith("AS ", StringComparison.OrdinalIgnoreCase))
            alias = afterSpec[3..].Trim();
        else if (afterSpec.StartsWith("as ", StringComparison.OrdinalIgnoreCase))
            alias = afterSpec[3..].Trim();
        else
            alias = $"__wf_{autoIdx}__";

        return new WindowSpec
        {
            FunctionName     = funcName.ToUpperInvariant(),
            FunctionArgs     = funcArgs,
            PartitionByExprs = partitionBy,
            OrderByItems     = orderBy,
            Frame            = frame,
            OutputAlias      = alias,
            RawToken         = item,
        };
    }

    // ── Over clause parser ────────────────────────────────────────────────────

    private static (List<string> partition, List<OrderByItem> orderBy, FrameSpec? frame)
        ParseOverClause(string overClause)
    {
        var partition = new List<string>();
        var orderBy   = new List<OrderByItem>();
        FrameSpec? frame = null;

        // Split at PARTITION BY and ORDER BY keywords
        var partIdx  = overClause.IndexOf("PARTITION BY", StringComparison.OrdinalIgnoreCase);
        var orderIdx = overClause.IndexOf("ORDER BY",     StringComparison.OrdinalIgnoreCase);

        string? partText  = null;
        string? orderText = null;

        if (partIdx >= 0)
        {
            var start = partIdx + "PARTITION BY".Length;
            var end   = orderIdx > partIdx ? orderIdx : overClause.Length;
            partText  = overClause[start..end].Trim();
        }
        if (orderIdx >= 0)
        {
            var start = orderIdx + "ORDER BY".Length;
            // Stop at the frame clause keyword
            var frameIdx = IndexOfFrameStart(overClause, start);
            var end = frameIdx >= 0 ? frameIdx : overClause.Length;
            orderText = overClause[start..end].Trim();

            // Parse frame clause if present
            if (frameIdx >= 0)
                frame = TryParseFrameSpec(overClause[frameIdx..].Trim());
        }

        if (partText != null)
        {
            foreach (var p in SplitTopLevelCommas(partText))
                if (!string.IsNullOrWhiteSpace(p)) partition.Add(p.Trim());
        }
        if (orderText != null)
        {
            foreach (var o in SplitTopLevelCommas(orderText))
            {
                var ot = o.Trim();
                if (string.IsNullOrWhiteSpace(ot)) continue;
                bool asc = true;
                if (ot.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase))
                {
                    asc = false;
                    ot  = ot[..^5].Trim();
                }
                else if (ot.EndsWith(" ASC", StringComparison.OrdinalIgnoreCase))
                {
                    ot = ot[..^4].Trim();
                }
                orderBy.Add(new OrderByItem { Expression = ot, Ascending = asc });
            }
        }
        return (partition, orderBy, frame);
    }

    /// <summary>Finds the first occurrence of a frame clause keyword (ROWS/RANGE/GROUPS BETWEEN/UNBOUNDED).</summary>
    private static int IndexOfFrameStart(string s, int from)
    {
        var keys = new[] { "ROWS BETWEEN", "RANGE BETWEEN", "GROUPS BETWEEN",
                           "ROWS UNBOUNDED", "RANGE UNBOUNDED" };
        return IndexOfAny(s, from, keys);
    }

    /// <summary>
    /// Parses a frame clause of the form:
    ///   (ROWS|RANGE|GROUPS) BETWEEN {bound} AND {bound}
    ///   (ROWS|RANGE|GROUPS) UNBOUNDED PRECEDING  (shorthand)
    /// </summary>
    private static FrameSpec? TryParseFrameSpec(string text)
    {
        var t = text.Trim();

        FrameUnit unit;
        if (t.StartsWith("ROWS",   StringComparison.OrdinalIgnoreCase)) unit = FrameUnit.Rows;
        else if (t.StartsWith("RANGE",  StringComparison.OrdinalIgnoreCase)) unit = FrameUnit.Range;
        else if (t.StartsWith("GROUPS", StringComparison.OrdinalIgnoreCase)) unit = FrameUnit.Groups;
        else return null;

        // Advance past unit keyword
        int idx = t.IndexOf(' ');
        if (idx < 0) return null;
        t = t[idx..].TrimStart();

        // Handle "BETWEEN start AND end"
        if (t.StartsWith("BETWEEN", StringComparison.OrdinalIgnoreCase))
        {
            t = t["BETWEEN".Length..].TrimStart();
            // Split at " AND "
            var andIdx = t.IndexOf(" AND ", StringComparison.OrdinalIgnoreCase);
            if (andIdx < 0) return null;

            var startText = t[..andIdx].Trim();
            var endText   = t[(andIdx + 5)..].Trim();

            var startBound = ParseFrameBound(startText);
            var endBound   = ParseFrameBound(endText);
            if (startBound == null || endBound == null) return null;

            return new FrameSpec { Unit = unit, Start = startBound, End = endBound };
        }

        // Handle shorthand "UNBOUNDED PRECEDING" / "CURRENT ROW"
        var bound = ParseFrameBound(t);
        if (bound != null)
        {
            return new FrameSpec
            {
                Unit  = unit,
                Start = bound,
                End   = FrameBound.CurrentRow,
            };
        }
        return null;
    }

    private static FrameBound? ParseFrameBound(string text)
    {
        var t = text.Trim();
        if (t.Equals("UNBOUNDED PRECEDING", StringComparison.OrdinalIgnoreCase))
            return FrameBound.UnboundedPreceding;
        if (t.Equals("UNBOUNDED FOLLOWING", StringComparison.OrdinalIgnoreCase))
            return FrameBound.UnboundedFollowing;
        if (t.Equals("CURRENT ROW", StringComparison.OrdinalIgnoreCase))
            return FrameBound.CurrentRow;

        // N PRECEDING
        if (t.EndsWith(" PRECEDING", StringComparison.OrdinalIgnoreCase))
        {
            var numStr = t[..^" PRECEDING".Length].Trim();
            if (int.TryParse(numStr, out int n))
                return new FrameBound { BoundType = FrameBoundType.Preceding, Offset = n };
        }
        // N FOLLOWING
        if (t.EndsWith(" FOLLOWING", StringComparison.OrdinalIgnoreCase))
        {
            var numStr = t[..^" FOLLOWING".Length].Trim();
            if (int.TryParse(numStr, out int n))
                return new FrameBound { BoundType = FrameBoundType.Following, Offset = n };
        }
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int FindOverKeyword(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') { depth++; continue; }
            if (text[i] == ')') { depth--; continue; }
            if (depth == 0 &&
                i + 4 < text.Length &&
                string.Compare(text, i, "OVER", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                // Must be preceded by ')' (possibly with whitespace)
                int j = i - 1;
                while (j >= 0 && text[j] == ' ') j--;
                if (j >= 0 && text[j] == ')')
                    return i;
            }
        }
        return -1;
    }

    private static (string? name, List<string> args) ParseFunctionCall(string funcPart)
    {
        var parenPos = funcPart.IndexOf('(');
        if (parenPos < 0) return (null, new List<string>());
        var name = funcPart[..parenPos].Trim();
        if (string.IsNullOrEmpty(name)) return (null, new List<string>());

        var argsText = funcPart[(parenPos + 1)..].TrimEnd(')', ' ');
        var args = new List<string>();
        foreach (var a in SplitTopLevelCommas(argsText))
            if (!string.IsNullOrWhiteSpace(a)) args.Add(a.Trim());

        return (name, args);
    }

    /// <summary>Split s by top-level commas (not inside parentheses).</summary>
    public static List<string> SplitTopLevelCommas(string s)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (s[i] == ',' && depth == 0)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }

    private static int FindMatchingParen(string s, int openPos)
    {
        int depth = 0;
        for (int i = openPos; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static int SkipWhitespace(string s, int from)
    {
        while (from < s.Length && s[from] == ' ') from++;
        return from;
    }

    private static int IndexOfAny(string s, int start, params string[] keywords)
    {
        int result = s.Length;
        foreach (var kw in keywords)
        {
            var idx = s.IndexOf(kw, start, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < result) result = idx;
        }
        return result == s.Length ? -1 : result;
    }
}
