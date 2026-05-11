using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Tests.Processor;

/// <summary>
/// Tests for the implicit GROUP BY in WITH clauses (aggregation mid-pipeline).
/// Pattern: MATCH ... WITH key, agg(n) [WHERE agg > threshold] RETURN key
/// </summary>
public class WithAggregationTests
{
    private static BogConnection Setup(string tableName,
        params (string id, Dictionary<string, object> props)[] nodes)
    {
        var db   = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);
        conn.BeginWriteTransaction();
        if (nodes.Length > 0)
        {
            var schema = nodes[0].props.ToDictionary(kv => kv.Key, kv => InferType(kv.Value));
            conn.EnsureNodeTable(tableName, schema);
        }
        foreach (var (id, props) in nodes)
            conn.UpsertNodeById(tableName, id, props);
        conn.Commit();
        return conn;
    }

    private static LogicalTypeID InferType(object? val) => val switch
    {
        long or int   => LogicalTypeID.INT64,
        double or float => LogicalTypeID.DOUBLE,
        bool            => LogicalTypeID.BOOL,
        _               => LogicalTypeID.STRING
    };

    private static List<BogDb.Core.Main.QueryResult.BogRow> Drain(
        BogDb.Core.Main.QueryResult.QueryResult result)
    {
        var rows = new List<BogDb.Core.Main.QueryResult.BogRow>();
        ulong n = result.GetNumTuples();
        for (ulong i = 0; i < n; i++)
            rows.Add(result.GetNext());
        return rows;
    }

    // ── Basic WITH aggregation ────────────────────────────────────────────────

    [Fact]
    public void With_GlobalCount_ThenReturn()
    {
        // MATCH (n:W) WITH count(n) AS cnt RETURN cnt
        var conn = Setup("W",
            ("w1", new() { ["v"] = 1L }),
            ("w2", new() { ["v"] = 2L }),
            ("w3", new() { ["v"] = 3L }));

        var result = conn.Query("MATCH (n:W) WITH count(n) AS cnt RETURN cnt");
        Assert.Equal(1UL, result.GetNumTuples());
        Assert.Equal(3L, result.GetNext().GetInt64(0));
    }

    [Fact]
    public void With_GroupedCount_ThenReturn()
    {
        // MATCH (n:Emp) WITH n.dept AS dept, count(n) AS cnt RETURN dept, cnt
        var conn = Setup("Emp",
            ("e1", new() { ["dept"] = "Eng", ["name"] = "Alice" }),
            ("e2", new() { ["dept"] = "Eng", ["name"] = "Bob"   }),
            ("e3", new() { ["dept"] = "HR",  ["name"] = "Carol" }));

        var result = conn.Query(
            "MATCH (n:Emp) WITH n.dept AS dept, count(n) AS cnt RETURN dept, cnt");
        var rows = Drain(result);

        Assert.Equal(2, rows.Count); // Eng, HR
        var byDept = rows.ToDictionary(r => r.GetString(0)!, r => r.GetInt64(1));
        Assert.Equal(2L, byDept["Eng"]);
        Assert.Equal(1L, byDept["HR"]);
    }

    // ── WITH aggregation + WHERE (HAVING semantics) ───────────────────────────

    [Fact]
    public void With_GroupedCount_WhereFilter_HavingSemantics()
    {
        // MATCH (n:Task) WITH n.status AS s, count(n) AS cnt
        // WHERE cnt > 1
        // RETURN s, cnt
        var conn = Setup("Task",
            ("t1", new() { ["status"] = "open"   }),
            ("t2", new() { ["status"] = "open"   }),
            ("t3", new() { ["status"] = "open"   }),
            ("t4", new() { ["status"] = "closed" }));

        var result = conn.Query(
            "MATCH (n:Task) WITH n.status AS s, count(n) AS cnt WHERE cnt > 1 RETURN s, cnt");
        var rows = Drain(result);

        // Only 'open' (3) passes cnt > 1; 'closed' (1) does not
        Assert.Equal(1, rows.Count);
        Assert.Equal("open", rows[0].GetString(0));
        Assert.Equal(3L, rows[0].GetInt64(1));
    }

    // ── WITH aggregation chained to RETURN ────────────────────────────────────

    [Fact]
    public void With_GroupedSum_ThenOrderedReturn()
    {
        // MATCH (n:Sale) WITH n.region AS r, sum(n.amount) AS total
        // RETURN r, total ORDER BY total DESC
        var conn = Setup("Sale",
            ("s1", new() { ["region"] = "West", ["amount"] = 100.0 }),
            ("s2", new() { ["region"] = "West", ["amount"] = 200.0 }),
            ("s3", new() { ["region"] = "East", ["amount"] = 50.0  }));

        var result = conn.Query(
            "MATCH (n:Sale) WITH n.region AS r, sum(n.amount) AS total " +
            "RETURN r, total ORDER BY total DESC");
        var rows = Drain(result);

        Assert.Equal(2, rows.Count);
        // West (300) > East (50) with DESC ordering
        Assert.Equal("West", rows[0].GetString(0));
        Assert.Equal(300.0, rows[0].GetDouble(1));
        Assert.Equal("East", rows[1].GetString(0));
        Assert.Equal(50.0, rows[1].GetDouble(1));
    }
}
