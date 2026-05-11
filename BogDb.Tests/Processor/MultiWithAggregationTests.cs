using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Tests.Processor;

/// <summary>
/// Regression tests for multi-stage WITH pipelines that pass aggregate values
/// across clause boundaries (Bug 3 investigation).
///
/// Pattern: MATCH … WITH key, agg(x) AS a   WITH key, a [, computed] RETURN …
/// These tests confirm (or expose failures in) the value-passing through
/// LogicalAggregate → LogicalProjection → subsequent WITH chains.
/// </summary>
public class MultiWithAggregationTests
{
    private static BogConnection Setup()
    {
        var db   = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);
        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Item", new Dictionary<string, LogicalTypeID>
        {
            ["cat"]   = LogicalTypeID.STRING,
            ["score"] = LogicalTypeID.DOUBLE,
        });
        // Two categories: A (scores 80, 90 → avg 85), B (score 70 → avg 70)
        conn.UpsertNodeById("Item", "i1", new() { ["cat"] = "A", ["score"] = 80.0 });
        conn.UpsertNodeById("Item", "i2", new() { ["cat"] = "A", ["score"] = 90.0 });
        conn.UpsertNodeById("Item", "i3", new() { ["cat"] = "B", ["score"] = 70.0 });
        conn.Commit();
        return conn;
    }

    private static List<BogDb.Core.Main.QueryResult.BogRow> Drain(
        BogDb.Core.Main.QueryResult.QueryResult result)
    {
        var rows = new List<BogDb.Core.Main.QueryResult.BogRow>();
        while (result.HasNext()) rows.Add(result.GetNext());
        return rows;
    }

    // ── Test 1: aggregate carried through a second WITH unchanged ───────────

    [Fact]
    public void MultiWith_Aggregate_IsNonZeroAfterPassThrough()
    {
        // MATCH … WITH cat, avg(score) AS a
        // WITH cat, a                             ← carry unchanged
        // RETURN cat, a ORDER BY cat
        var conn = Setup();
        var r = conn.Query(
            "MATCH (n:Item) " +
            "WITH n.cat AS cat, avg(n.score) AS a " +
            "WITH cat, a " +
            "RETURN cat, a " +
            "ORDER BY cat");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var rows = Drain(r);
        Assert.Equal(2, rows.Count);

        var byName = rows.ToDictionary(row => row.GetString(0)!, row => row.GetDouble(1));
        Assert.Equal(85.0, byName["A"]);   // avg(80,90) = 85
        Assert.Equal(70.0, byName["B"]);   // avg(70) = 70
    }

    // ── Test 2: aggregate used in a computed expression in second WITH ───────

    [Fact]
    public void MultiWith_Aggregate_UsedInComputedExpression()
    {
        // MATCH … WITH cat, avg(score) AS a
        // WITH cat, a, a * 2.0 AS doubled
        // RETURN cat, a, doubled ORDER BY cat
        var conn = Setup();
        var r = conn.Query(
            "MATCH (n:Item) " +
            "WITH n.cat AS cat, avg(n.score) AS a " +
            "WITH cat, a, a * 2.0 AS doubled " +
            "RETURN cat, a, doubled " +
            "ORDER BY cat");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var rows = Drain(r);
        Assert.Equal(2, rows.Count);

        // Category A: a=85, doubled=170
        var rowA = rows.First(r => r.GetString(0) == "A");
        Assert.Equal(85.0,  rowA.GetDouble(1), precision: 5);
        Assert.Equal(170.0, rowA.GetDouble(2), precision: 5);

        // Category B: a=70, doubled=140
        var rowB = rows.First(r => r.GetString(0) == "B");
        Assert.Equal(70.0,  rowB.GetDouble(1), precision: 5);
        Assert.Equal(140.0, rowB.GetDouble(2), precision: 5);
    }

    // ── Test 3: triple WITH chain ─────────────────────────────────────────────

    [Fact]
    public void MultiWith_TripleChain_AggregateReachesReturn()
    {
        // MATCH … WITH cat, avg(score) AS a
        // WITH cat, a, a * 2.0 AS doubled
        // WITH cat, doubled
        // RETURN cat, doubled ORDER BY cat
        var conn = Setup();
        var r = conn.Query(
            "MATCH (n:Item) " +
            "WITH n.cat AS cat, avg(n.score) AS a " +
            "WITH cat, a, a * 2.0 AS doubled " +
            "WITH cat, doubled " +
            "RETURN cat, doubled " +
            "ORDER BY cat");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var rows = Drain(r);
        Assert.Equal(2, rows.Count);

        var byName = rows.ToDictionary(row => row.GetString(0)!, row => row.GetDouble(1));
        Assert.Equal(170.0, byName["A"], precision: 5);
        Assert.Equal(140.0, byName["B"], precision: 5);
    }

    // ── Test 4: ORDER BY uses the carried aggregate (not inline expression) ───

    [Fact]
    public void MultiWith_OrderByCarriedAggregate_Descending()
    {
        // MATCH … WITH cat, avg(score) AS a
        // WITH cat, a
        // RETURN cat, a ORDER BY a DESC   ← a is a carried alias, not a RETURN-level alias
        var conn = Setup();
        var r = conn.Query(
            "MATCH (n:Item) " +
            "WITH n.cat AS cat, avg(n.score) AS a " +
            "WITH cat, a " +
            "RETURN cat, a " +
            "ORDER BY a DESC");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var rows = Drain(r);
        Assert.Equal(2, rows.Count);

        // A (85) > B (70) when DESC
        Assert.Equal("A", rows[0].GetString(0));
        Assert.Equal("B", rows[1].GetString(0));
    }
}
