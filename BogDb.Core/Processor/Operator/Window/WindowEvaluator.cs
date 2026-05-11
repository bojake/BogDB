using System;
using System.Collections.Generic;
using System.Linq;

namespace BogDb.Core.Processor.Operator.Window;

/// <summary>
/// Computes window function values over a collection of row dictionaries.
///
/// Supported functions:
///   Ranking:    ROW_NUMBER, RANK, DENSE_RANK, NTILE, PERCENT_RANK, CUME_DIST
///   Navigation: LAG, LEAD, FIRST_VALUE, LAST_VALUE, NTH_VALUE
///   Aggregate:  SUM, COUNT, AVG, MIN, MAX  (with OVER clause, full frame support)
///
/// Frame clause support (ROWS/RANGE/GROUPS BETWEEN ... AND ...):
///   - UNBOUNDED PRECEDING | N PRECEDING | CURRENT ROW | N FOLLOWING | UNBOUNDED FOLLOWING
///   - ROWS mode: physical row offsets (fully implemented)
///   - RANGE mode: same-order-key peer grouping (CURRENT ROW = include all peers)
///   - GROUPS mode: mapped to ROWS (approximation)
/// </summary>
public static class WindowEvaluator
{
    /// <summary>
    /// Applies a single window spec to all rows, augmenting each with the computed
    /// window column keyed by <see cref="WindowSpec.OutputAlias"/>.
    /// </summary>
    public static void Apply(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        if (rows.Count == 0) return;

        // Group rows by partition key
        var partitions = Partition(rows, spec.PartitionByExprs);

        foreach (var partition in partitions)
        {
            // Sort partition by ORDER BY items (multi-key)
            var sorted = SortPartition(partition, spec.OrderByItems);

            // Compute function values and assign back to rows (in-place via reference equality)
            ComputeFunction(sorted, spec);
        }
    }

    // ── Partition ─────────────────────────────────────────────────────────────

    private static IEnumerable<List<Dictionary<string, object?>>> Partition(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<string> partitionExprs)
    {
        if (partitionExprs.Count == 0)
        {
            yield return rows;
            yield break;
        }

        var groups = new Dictionary<string, List<Dictionary<string, object?>>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var key = string.Join("|", partitionExprs.Select(e => Stringify(Resolve(row, e))));
            if (!groups.TryGetValue(key, out var group))
            {
                group = new List<Dictionary<string, object?>>();
                groups[key] = group;
            }
            group.Add(row);
        }

        foreach (var g in groups.Values) yield return g;
    }

    // ── Sort (multi-key) ───────────────────────────────────────────────────────

    private static List<Dictionary<string, object?>> SortPartition(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<OrderByItem> orderItems)
    {
        if (orderItems.Count == 0) return rows;

        IOrderedEnumerable<Dictionary<string, object?>> query =
            rows.OrderBy(r => Resolve(r, orderItems[0].Expression),
                         GetComparer(orderItems[0].Ascending));

        for (int i = 1; i < orderItems.Count; i++)
        {
            var item = orderItems[i];
            query = query.ThenBy(r => Resolve(r, item.Expression),
                                 GetComparer(item.Ascending));
        }
        return query.ToList();
    }

    private static IComparer<object?> GetComparer(bool ascending)
        => Comparer<object?>.Create((a, b) =>
        {
            int cmp = CompareValues(a, b);
            return ascending ? cmp : -cmp;
        });

    // ── Frame bounds ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the (inclusive) [startIdx, endIdx] row range for the frame window
    /// at position <paramref name="rowIndex"/> within <paramref name="partition"/>.
    /// </summary>
    private static (int start, int end) ComputeFrameBounds(
        List<Dictionary<string, object?>> partition,
        int rowIndex,
        FrameSpec frame,
        IReadOnlyList<OrderByItem> orderItems)
    {
        int n = partition.Count;

        switch (frame.Unit)
        {
            case FrameUnit.Rows:
            case FrameUnit.Groups:  // Groups mapped to Rows (approximation)
                return (BoundToIndex(frame.Start, rowIndex, n, partition, orderItems, isStart: true),
                        BoundToIndex(frame.End,   rowIndex, n, partition, orderItems, isStart: false));

            case FrameUnit.Range:
                // For RANGE mode, CURRENT ROW means "all peers with same ORDER BY value"
                int rangeStart = BoundToIndex(frame.Start, rowIndex, n, partition, orderItems, isStart: true);
                int rangeEnd;
                if (frame.End.BoundType == FrameBoundType.CurrentRow)
                {
                    // Extend end to include all peers (same order key)
                    rangeEnd = rowIndex;
                    while (rangeEnd + 1 < n && IsTie(partition[rowIndex], partition[rangeEnd + 1], orderItems))
                        rangeEnd++;
                }
                else
                {
                    rangeEnd = BoundToIndex(frame.End, rowIndex, n, partition, orderItems, isStart: false);
                }
                return (rangeStart, rangeEnd);

            default:
                return (0, n - 1);
        }
    }

    private static int BoundToIndex(
        FrameBound bound, int rowIndex, int n,
        List<Dictionary<string, object?>> partition,
        IReadOnlyList<OrderByItem> orderItems,
        bool isStart)
    {
        return bound.BoundType switch
        {
            FrameBoundType.UnboundedPreceding => 0,
            FrameBoundType.Preceding          => Math.Max(0, rowIndex - bound.Offset),
            FrameBoundType.CurrentRow         => rowIndex,
            FrameBoundType.Following          => Math.Min(n - 1, rowIndex + bound.Offset),
            FrameBoundType.UnboundedFollowing => n - 1,
            _                                 => isStart ? 0 : n - 1,
        };
    }

    /// <summary>Returns the effective frame for a spec, applying SQL defaults.</summary>
    private static FrameSpec EffectiveFrame(WindowSpec spec)
    {
        if (spec.Frame != null) return spec.Frame;
        // SQL standard: if ORDER BY present → RANGE UNBOUNDED PRECEDING TO CURRENT ROW
        //               if no ORDER BY      → ROWS  UNBOUNDED PRECEDING TO UNBOUNDED FOLLOWING
        return spec.OrderByItems.Count > 0
            ? FrameSpec.DefaultWithOrderBy
            : FrameSpec.DefaultNoOrderBy;
    }

    // ── Function dispatch ──────────────────────────────────────────────────────

    private static void ComputeFunction(List<Dictionary<string, object?>> sorted, WindowSpec spec)
    {
        switch (spec.FunctionName)
        {
            case "ROW_NUMBER":   ApplyRowNumber(sorted, spec);          break;
            case "RANK":         ApplyRank(sorted, spec);               break;
            case "DENSE_RANK":   ApplyDenseRank(sorted, spec);          break;
            case "NTILE":        ApplyNtile(sorted, spec);              break;
            case "PERCENT_RANK": ApplyPercentRank(sorted, spec);        break;
            case "CUME_DIST":    ApplyCumeDist(sorted, spec);           break;
            case "LAG":          ApplyLagLead(sorted, spec, false);     break;
            case "LEAD":         ApplyLagLead(sorted, spec, true);      break;
            case "FIRST_VALUE":  ApplyFirstValue(sorted, spec);         break;
            case "LAST_VALUE":   ApplyLastValue(sorted, spec);          break;
            case "NTH_VALUE":    ApplyNthValue(sorted, spec);           break;
            case "SUM":          ApplyAggWindow(sorted, spec, AggMode.Sum);   break;
            case "COUNT":        ApplyAggWindow(sorted, spec, AggMode.Count); break;
            case "AVG":          ApplyAggWindow(sorted, spec, AggMode.Avg);   break;
            case "MIN":          ApplyAggWindow(sorted, spec, AggMode.Min);   break;
            case "MAX":          ApplyAggWindow(sorted, spec, AggMode.Max);   break;
            default:
                foreach (var row in sorted) row[spec.OutputAlias] = null;
                break;
        }
    }

    // ── Ranking functions ─────────────────────────────────────────────────────

    private static void ApplyRowNumber(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        for (int i = 0; i < rows.Count; i++)
            rows[i][spec.OutputAlias] = (long)(i + 1);
    }

    private static void ApplyRank(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        var orderExprs = spec.OrderByItems;
        long rank = 1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (i == 0)
            {
                rows[i][spec.OutputAlias] = 1L;
            }
            else
            {
                bool tie = IsTie(rows[i - 1], rows[i], orderExprs);
                if (!tie) rank = i + 1;
                rows[i][spec.OutputAlias] = rank;
            }
        }
    }

    private static void ApplyDenseRank(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        var orderExprs = spec.OrderByItems;
        long rank = 1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0 && !IsTie(rows[i - 1], rows[i], orderExprs))
                rank++;
            rows[i][spec.OutputAlias] = rank;
        }
    }

    private static void ApplyNtile(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        long n = spec.FunctionArgs.Count > 0 && long.TryParse(spec.FunctionArgs[0], out var nv) ? nv : 1;
        n = Math.Max(1, n);
        int total = rows.Count;
        for (int i = 0; i < total; i++)
            rows[i][spec.OutputAlias] = (long)(i * n / total + 1);
    }

    private static void ApplyPercentRank(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        int n = rows.Count;
        if (n <= 1) { foreach (var r in rows) r[spec.OutputAlias] = 0.0; return; }
        var orderExprs = spec.OrderByItems;
        long rank = 1;
        for (int i = 0; i < n; i++)
        {
            if (i > 0 && !IsTie(rows[i - 1], rows[i], orderExprs)) rank = i + 1;
            rows[i][spec.OutputAlias] = (double)(rank - 1) / (n - 1);
        }
    }

    private static void ApplyCumeDist(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        int n = rows.Count;
        var orderExprs = spec.OrderByItems;
        int i = 0;
        while (i < n)
        {
            int j = i;
            while (j < n - 1 && IsTie(rows[j], rows[j + 1], orderExprs)) j++;
            double cd = (double)(j + 1) / n;
            for (int k = i; k <= j; k++)
                rows[k][spec.OutputAlias] = cd;
            i = j + 1;
        }
    }

    // ── Navigation functions ───────────────────────────────────────────────────

    private static void ApplyLagLead(List<Dictionary<string, object?>> rows, WindowSpec spec, bool forward)
    {
        if (spec.FunctionArgs.Count == 0) return;
        string expr  = spec.FunctionArgs[0];
        int offset   = spec.FunctionArgs.Count > 1 && int.TryParse(spec.FunctionArgs[1], out var ov) ? ov : 1;
        object? def  = spec.FunctionArgs.Count > 2 ? (object?)spec.FunctionArgs[2] : null;

        for (int i = 0; i < rows.Count; i++)
        {
            int target = forward ? i + offset : i - offset;
            if (target >= 0 && target < rows.Count)
                rows[i][spec.OutputAlias] = Resolve(rows[target], expr);
            else
                rows[i][spec.OutputAlias] = def;
        }
    }

    /// <summary>FIRST_VALUE: returns the first value in the current frame.</summary>
    private static void ApplyFirstValue(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        if (spec.FunctionArgs.Count == 0) return;
        string expr = spec.FunctionArgs[0];
        var frame   = EffectiveFrame(spec);
        for (int i = 0; i < rows.Count; i++)
        {
            var (start, _) = ComputeFrameBounds(rows, i, frame, spec.OrderByItems);
            rows[i][spec.OutputAlias] = Resolve(rows[start], expr);
        }
    }

    /// <summary>LAST_VALUE: returns the last value in the current frame.</summary>
    private static void ApplyLastValue(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        if (spec.FunctionArgs.Count == 0) return;
        string expr = spec.FunctionArgs[0];
        var frame   = EffectiveFrame(spec);
        for (int i = 0; i < rows.Count; i++)
        {
            var (_, end) = ComputeFrameBounds(rows, i, frame, spec.OrderByItems);
            rows[i][spec.OutputAlias] = Resolve(rows[end], expr);
        }
    }

    private static void ApplyNthValue(List<Dictionary<string, object?>> rows, WindowSpec spec)
    {
        if (spec.FunctionArgs.Count < 2) return;
        string expr = spec.FunctionArgs[0];
        int n       = int.TryParse(spec.FunctionArgs[1], out var nv) ? nv - 1 : 0; // 1-indexed → 0-indexed
        var frame   = EffectiveFrame(spec);
        for (int i = 0; i < rows.Count; i++)
        {
            var (start, end) = ComputeFrameBounds(rows, i, frame, spec.OrderByItems);
            int target = start + n;
            rows[i][spec.OutputAlias] = (target >= start && target <= end)
                ? Resolve(rows[target], expr)
                : null;
        }
    }

    // ── Aggregate window functions (frame-aware) ───────────────────────────────

    private enum AggMode { Sum, Count, Avg, Min, Max }

    private static void ApplyAggWindow(List<Dictionary<string, object?>> rows, WindowSpec spec, AggMode mode)
    {
        string? expr = spec.FunctionArgs.Count > 0 ? spec.FunctionArgs[0] : null;
        var frame    = EffectiveFrame(spec);

        for (int i = 0; i < rows.Count; i++)
        {
            var (start, end) = ComputeFrameBounds(rows, i, frame, spec.OrderByItems);

            // Collect values in the frame window
            IEnumerable<object?> frameValues()
            {
                for (int j = start; j <= end; j++)
                    yield return expr == null ? null : Resolve(rows[j], expr);
            }

            object? result = mode switch
            {
                AggMode.Count => (long)(end - start + 1),
                AggMode.Sum   => expr == null ? null : SumValues(frameValues()),
                AggMode.Avg   => expr == null ? null : AvgValues(frameValues()),
                AggMode.Min   => expr == null ? null : MinMax(frameValues(), min: true),
                AggMode.Max   => expr == null ? null : MinMax(frameValues(), min: false),
                _             => null,
            };
            rows[i][spec.OutputAlias] = result;
        }
    }

    // ── Value helpers ─────────────────────────────────────────────────────────

    /// <summary>Resolves a column expression against a row dict. Tries exact key, then dot-shortname, then case-insensitive.</summary>
    internal static object? Resolve(Dictionary<string, object?> row, string expr)
    {
        if (row.TryGetValue(expr, out var v)) return v;
        var dot = expr.LastIndexOf('.');
        if (dot >= 0)
        {
            var shortKey = expr[(dot + 1)..];
            if (row.TryGetValue(shortKey, out var sv)) return sv;
        }
        foreach (var kv in row)
            if (string.Equals(kv.Key, expr, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    private static string Stringify(object? v) => v?.ToString() ?? "\0null\0";

    private static bool IsTie(Dictionary<string, object?> a, Dictionary<string, object?> b,
        IReadOnlyList<OrderByItem> orderItems)
    {
        foreach (var o in orderItems)
        {
            if (CompareValues(Resolve(a, o.Expression), Resolve(b, o.Expression)) != 0)
                return false;
        }
        return true;
    }

    private static int CompareValues(object? a, object? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;
        if (a is IComparable ac && b.GetType() == a.GetType())
            return ac.CompareTo(b);
        if (TryDouble(a, out double da) && TryDouble(b, out double db))
            return da.CompareTo(db);
        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDouble(object? v, out double d)
    {
        switch (v) {
            case long l:    d = l;  return true;
            case int i:     d = i;  return true;
            case double dv: d = dv; return true;
            case float f:   d = f;  return true;
            default:        d = 0;  return false;
        }
    }

    private static object? SumValues(IEnumerable<object?> values)
    {
        double s = 0; bool any = false;
        foreach (var v in values) { if (TryDouble(v, out double d)) { s += d; any = true; } }
        return any ? (object?)s : null;
    }

    private static object? AvgValues(IEnumerable<object?> values)
    {
        double s = 0; int n = 0;
        foreach (var v in values) { if (TryDouble(v, out double d)) { s += d; n++; } }
        return n > 0 ? (object?)(s / n) : null;
    }

    private static object? MinMax(IEnumerable<object?> values, bool min)
    {
        object? best = null;
        foreach (var v in values)
        {
            if (v == null) continue;
            if (best == null || (min ? CompareValues(v, best) < 0 : CompareValues(v, best) > 0))
                best = v;
        }
        return best;
    }
}
