using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Tests.Processor;

/// <summary>
/// Tests for GROUP BY (implicit Cypher grouping) via the keyed PhysicalAggregate path.
/// Uses positional GetNext/GetInt64/GetDouble/GetString row access.
/// </summary>
public class GroupByAggregateTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LogicalTypeID InferType(object? val) => val switch
    {
        long or int   => LogicalTypeID.INT64,
        double or float => LogicalTypeID.DOUBLE,
        bool            => LogicalTypeID.BOOL,
        _               => LogicalTypeID.STRING
    };

    /// <summary>
    /// Creates an in-memory db, registers the table schema from the first node's props,
    /// inserts all nodes, and returns a committed connection ready for MATCH queries.
    /// </summary>
    private static BogConnection Setup(string tableName,
        params (string id, Dictionary<string, object> props)[] nodes)
    {
        var db   = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);
        conn.BeginWriteTransaction();

        // Register table in catalog so the binder can resolve it in MATCH clauses
        if (nodes.Length > 0)
        {
            var schema = nodes[0].props
                .ToDictionary(kv => kv.Key, kv => InferType(kv.Value));
            conn.EnsureNodeTable(tableName, schema);
        }

        foreach (var (id, props) in nodes)
            conn.UpsertNodeById(tableName, id, props);

        conn.Commit();
        return conn;
    }

    /// Drain all rows from a QueryResult into a list.
    private static List<BogDb.Core.Main.QueryResult.BogRow> Drain(
        BogDb.Core.Main.QueryResult.QueryResult result)
    {
        var rows = new List<BogDb.Core.Main.QueryResult.BogRow>();
        ulong n = result.GetNumTuples();
        for (ulong i = 0; i < n; i++)
            rows.Add(result.GetNext());
        return rows;
    }

    // ── Single-key GROUP BY ───────────────────────────────────────────────────

    [Fact]
    public void GroupBy_SingleKey_Count_ReturnsOneRowPerGroup()
    {
        var conn = Setup("Employee",
            ("e1", new() { ["dept"] = "Eng",   ["name"] = "Alice" }),
            ("e2", new() { ["dept"] = "Eng",   ["name"] = "Bob"   }),
            ("e3", new() { ["dept"] = "HR",    ["name"] = "Carol" }),
            ("e4", new() { ["dept"] = "HR",    ["name"] = "Dave"  }),
            ("e5", new() { ["dept"] = "Legal", ["name"] = "Eve"   }));

        var result = conn.Query("MATCH (n:Employee) RETURN n.dept, count(n)");
        var rows = Drain(result);

        Assert.Equal(3, rows.Count); // Eng, HR, Legal
        var byDept = rows.ToDictionary(r => r.GetString(0)!, r => r.GetInt64(1));
        Assert.Equal(2L, byDept["Eng"]);
        Assert.Equal(2L, byDept["HR"]);
        Assert.Equal(1L, byDept["Legal"]);
    }

    [Fact]
    public void GroupBy_SingleKey_Sum()
    {
        var conn = Setup("Staff",
            ("s1", new() { ["dept"] = "Eng", ["salary"] = 100.0 }),
            ("s2", new() { ["dept"] = "Eng", ["salary"] = 200.0 }),
            ("s3", new() { ["dept"] = "HR",  ["salary"] = 150.0 }));

        var result = conn.Query("MATCH (n:Staff) RETURN n.dept, sum(n.salary)");
        var rows = Drain(result);

        Assert.Equal(2, rows.Count);
        var byDept = rows.ToDictionary(r => r.GetString(0)!, r => r.GetDouble(1));
        Assert.Equal(300.0, byDept["Eng"]);
        Assert.Equal(150.0, byDept["HR"]);
    }

    [Fact]
    public void GroupBy_SingleKey_Avg()
    {
        var conn = Setup("Worker",
            ("w1", new() { ["dept"] = "A", ["score"] = 80.0 }),
            ("w2", new() { ["dept"] = "A", ["score"] = 100.0 }),
            ("w3", new() { ["dept"] = "B", ["score"] = 60.0 }));

        var result = conn.Query("MATCH (n:Worker) RETURN n.dept, avg(n.score)");
        var rows = Drain(result);
        Assert.Equal(2, rows.Count);

        var byDept = rows.ToDictionary(r => r.GetString(0)!, r => r.GetDouble(1));
        Assert.Equal(90.0, byDept["A"]);
        Assert.Equal(60.0, byDept["B"]);
    }

    [Fact]
    public void GroupBy_SingleKey_Min_And_Max()
    {
        var conn = Setup("Item",
            ("i1", new() { ["cat"] = "X", ["price"] = 10.0 }),
            ("i2", new() { ["cat"] = "X", ["price"] = 50.0 }),
            ("i3", new() { ["cat"] = "Y", ["price"] = 30.0 }));

        // col 0=cat, 1=min, 2=max
        var result = conn.Query("MATCH (n:Item) RETURN n.cat, min(n.price), max(n.price)");
        var rows = Drain(result);
        Assert.Equal(2, rows.Count);

        var xRow = rows.First(r => r.GetString(0) == "X");
        Assert.Equal(10.0, xRow.GetDouble(1));
        Assert.Equal(50.0, xRow.GetDouble(2));
    }

    // ── Multi-key GROUP BY ────────────────────────────────────────────────────

    [Fact]
    public void GroupBy_MultiKey_Count()
    {
        var conn = Setup("Person",
            ("p1", new() { ["dept"] = "Eng", ["level"] = "Senior" }),
            ("p2", new() { ["dept"] = "Eng", ["level"] = "Junior" }),
            ("p3", new() { ["dept"] = "Eng", ["level"] = "Senior" }),
            ("p4", new() { ["dept"] = "HR",  ["level"] = "Senior" }));

        // col 0=dept, 1=level, 2=count
        var result = conn.Query("MATCH (n:Person) RETURN n.dept, n.level, count(n)");
        var rows = Drain(result);

        // Eng/Senior=2, Eng/Junior=1, HR/Senior=1 → 3 groups
        Assert.Equal(3, rows.Count);

        var engSenior = rows.FirstOrDefault(r =>
            r.GetString(0) == "Eng" && r.GetString(1) == "Senior");
        Assert.NotNull(engSenior);
        Assert.Equal(2L, engSenior!.GetInt64(2));
    }

    // ── Mixed aggregates ──────────────────────────────────────────────────────

    [Fact]
    public void GroupBy_MixedAggregates_CountAndSum()
    {
        var conn = Setup("Sale",
            ("s1", new() { ["region"] = "West", ["amount"] = 100.0 }),
            ("s2", new() { ["region"] = "West", ["amount"] = 200.0 }),
            ("s3", new() { ["region"] = "East", ["amount"] = 150.0 }));

        // col 0=region, 1=count, 2=sum
        var result = conn.Query("MATCH (n:Sale) RETURN n.region, count(n), sum(n.amount)");
        var rows = Drain(result);
        Assert.Equal(2, rows.Count);

        var west = rows.First(r => r.GetString(0) == "West");
        Assert.Equal(2L,    west.GetInt64(1));
        Assert.Equal(300.0, west.GetDouble(2));
    }

    // ── Global (no group key) still works ────────────────────────────────────

    [Fact]
    public void GlobalAggregate_Count_StillWorks_AfterRefactor()
    {
        var conn = Setup("GNode",
            ("n1", new() { ["v"] = 1L }),
            ("n2", new() { ["v"] = 2L }),
            ("n3", new() { ["v"] = 3L }));

        var result = conn.Query("MATCH (n:GNode) RETURN count(n)");
        Assert.Equal(1UL, result.GetNumTuples());
        Assert.Equal(3L, result.GetNext().GetInt64(0));
    }

    // ── GROUP BY + ORDER BY ───────────────────────────────────────────────────

    [Fact]
    public void GroupBy_With_OrderBy_SortsByCount()
    {
        var conn = Setup("Task",
            ("t1", new() { ["status"] = "open"   }),
            ("t2", new() { ["status"] = "open"   }),
            ("t3", new() { ["status"] = "open"   }),
            ("t4", new() { ["status"] = "closed" }),
            ("t5", new() { ["status"] = "closed" }));

        // col 0=status, 1=count; ORDER BY count ASC → closed(2) before open(3)
        var result = conn.Query(
            "MATCH (n:Task) RETURN n.status, count(n) ORDER BY count(n) ASC");
        var rows = Drain(result);

        Assert.Equal(2, rows.Count);
        Assert.Equal("closed", rows[0].GetString(0));
        Assert.Equal(2L,       rows[0].GetInt64(1));
        Assert.Equal("open",   rows[1].GetString(0));
        Assert.Equal(3L,       rows[1].GetInt64(1));
    }
}
