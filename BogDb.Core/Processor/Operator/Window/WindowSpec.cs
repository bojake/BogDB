using System.Collections.Generic;

namespace BogDb.Core.Processor.Operator.Window;

// ── Frame data model ──────────────────────────────────────────────────────────

/// <summary>Frame mode: ROWS (physical row offset) | RANGE (value distance) | GROUPS (peer group count).</summary>
public enum FrameUnit { Rows, Range, Groups }

/// <summary>How a frame boundary is defined.</summary>
public enum FrameBoundType
{
    UnboundedPreceding,   // UNBOUNDED PRECEDING
    Preceding,            // N PRECEDING  (Offset = N)
    CurrentRow,           // CURRENT ROW
    Following,            // N FOLLOWING  (Offset = N)
    UnboundedFollowing,   // UNBOUNDED FOLLOWING
}

/// <summary>One boundary (start or end) of a window frame.</summary>
public sealed class FrameBound
{
    public FrameBoundType BoundType { get; init; } = FrameBoundType.CurrentRow;
    /// <summary>Row offset for N PRECEDING / N FOLLOWING. 0 otherwise.</summary>
    public int Offset { get; init; }

    public static readonly FrameBound UnboundedPreceding =
        new() { BoundType = FrameBoundType.UnboundedPreceding };
    public static readonly FrameBound UnboundedFollowing =
        new() { BoundType = FrameBoundType.UnboundedFollowing };
    public static readonly FrameBound CurrentRow =
        new() { BoundType = FrameBoundType.CurrentRow };
}

/// <summary>
/// Describes the window frame: unit + start boundary + end boundary.
/// Mirrors the SQL ROWS/RANGE/GROUPS BETWEEN … AND … clause.
/// </summary>
public sealed class FrameSpec
{
    public FrameUnit Unit  { get; init; } = FrameUnit.Rows;
    public FrameBound Start { get; init; } = FrameBound.UnboundedPreceding;
    public FrameBound End   { get; init; } = FrameBound.CurrentRow;

    // ── SQL defaults ─────────────────────────────────────────────────────────

    /// <summary>Default when ORDER BY is present: RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW.</summary>
    public static readonly FrameSpec DefaultWithOrderBy = new()
    {
        Unit  = FrameUnit.Range,
        Start = FrameBound.UnboundedPreceding,
        End   = FrameBound.CurrentRow,
    };

    /// <summary>Default when no ORDER BY: ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING (whole partition).</summary>
    public static readonly FrameSpec DefaultNoOrderBy = new()
    {
        Unit  = FrameUnit.Rows,
        Start = FrameBound.UnboundedPreceding,
        End   = FrameBound.UnboundedFollowing,
    };

    /// <summary>Whole-partition ROWS frame (identical to DefaultNoOrderBy).</summary>
    public static readonly FrameSpec WholePartition = DefaultNoOrderBy;
}

// ── Window spec ───────────────────────────────────────────────────────────────

/// <summary>
/// Describes a single window function call: func(args) OVER (PARTITION BY … ORDER BY … frame).
/// </summary>
public sealed class WindowSpec
{
    /// <summary>Function name (ROW_NUMBER, RANK, SUM, LAG, etc.) — uppercased.</summary>
    public string FunctionName { get; init; } = "";

    /// <summary>Positional arguments inside func(). e.g. ["p.salary", "1", "0"] for LAG.</summary>
    public IReadOnlyList<string> FunctionArgs { get; init; } = new List<string>();

    /// <summary>Expressions in PARTITION BY clause.</summary>
    public IReadOnlyList<string> PartitionByExprs { get; init; } = new List<string>();

    /// <summary>Expressions in ORDER BY clause with direction.</summary>
    public IReadOnlyList<OrderByItem> OrderByItems { get; init; } = new List<OrderByItem>();

    /// <summary>
    /// Explicit frame clause, or null if the query had no frame clause (evaluator applies SQL default).
    /// </summary>
    public FrameSpec? Frame { get; init; }

    /// <summary>Output alias (AS name).</summary>
    public string OutputAlias { get; init; } = "";

    /// <summary>Full raw token for this window call.</summary>
    public string RawToken { get; init; } = "";

    /// <summary>Start index of the raw token in the original RETURN clause.</summary>
    public int StartIndex { get; init; }

    /// <summary>End index (exclusive) of the raw token in the original RETURN clause.</summary>
    public int EndIndex { get; init; }
}

/// <summary>Expression + sort direction used in ORDER BY inside a window spec.</summary>
public sealed class OrderByItem
{
    public string Expression { get; init; } = "";
    public bool Ascending    { get; init; } = true;
}
