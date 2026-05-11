using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Parser;

/// <summary>
/// Tests for UNWIND clause: list literals → per-row element bindings.
/// Covers bare UNWIND, UNWIND with arithmetic, and empty list.
/// </summary>
public sealed class UnwindTests
{
    // ── UNWIND [int list] AS x RETURN x ─────────────────────────────────────

    [Fact]
    public void Unwind_IntList_ReturnsAllElements()
    {
        var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("UNWIND [1, 2, 3] AS x RETURN x");
        Assert.True(result.IsSuccess, result.ErrorMessage);

        var values = new List<long>();
        while (result.HasNext())
            values.Add(result.GetNext().GetInt64(0));

        Assert.Equal(3, values.Count);
        Assert.Equal(1L, values[0]);
        Assert.Equal(2L, values[1]);
        Assert.Equal(3L, values[2]);
    }

    [Fact]
    public void Unwind_StringList_ReturnsAllElements()
    {
        var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("UNWIND ['alice', 'bob', 'carol'] AS s RETURN s");
        Assert.True(result.IsSuccess, result.ErrorMessage);

        var values = new List<string>();
        while (result.HasNext())
            values.Add(result.GetNext().GetString(0));

        Assert.Equal(3, values.Count);
        Assert.Equal("alice", values[0]);
        Assert.Equal("bob", values[1]);
        Assert.Equal("carol", values[2]);
    }

    [Fact]
    public void Unwind_EmptyList_ReturnsNoRows()
    {
        var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("UNWIND [] AS x RETURN x");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(result.HasNext());
    }

    [Fact]
    public void Unwind_WithArithmetic_ReturnsComputedValues()
    {
        var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        // UNWIND [10, 20] AS x RETURN x * 2
        var result = conn.Query("UNWIND [10, 20] AS x RETURN x * 2");
        Assert.True(result.IsSuccess, result.ErrorMessage);

        var values = new List<long>();
        while (result.HasNext())
            values.Add(result.GetNext().GetInt64(0));

        Assert.Equal(2, values.Count);
        Assert.Equal(20L, values[0]);
        Assert.Equal(40L, values[1]);
    }

    [Fact]
    public void Unwind_SingleElement_ReturnsOneRow()
    {
        var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("UNWIND [42] AS x RETURN x");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(42L, result.GetNext().GetInt64(0));
        Assert.False(result.HasNext());
    }
}
