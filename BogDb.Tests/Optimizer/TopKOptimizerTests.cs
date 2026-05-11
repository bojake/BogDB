using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Tests.Optimizer;

/// <summary>
/// Tests for the TopK optimizer rule: ORDER BY ... LIMIT N queries should use
/// PhysicalTopK (max-heap, O(n·log K)) instead of full sort + truncate.
/// Correctness is verified by end-to-end Cypher query results.
/// </summary>
public class TopKOptimizerTests
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

    private static LogicalTypeID InferType(object? v) => v switch
    {
        long or int   => LogicalTypeID.INT64,
        double        => LogicalTypeID.DOUBLE,
        _             => LogicalTypeID.STRING
    };

    private static List<BogDb.Core.Main.QueryResult.BogRow> Drain(
        BogDb.Core.Main.QueryResult.QueryResult r)
    {
        var rows = new List<BogDb.Core.Main.QueryResult.BogRow>();
        for (ulong i = 0; i < r.GetNumTuples(); i++)
            rows.Add(r.GetNext());
        return rows;
    }

    // ── Basic top-K correctness ───────────────────────────────────────────────

    [Fact]
    public void TopK_OrderByAsc_Limit2_ReturnsSmallest2()
    {
        var conn = Setup("Score",
            ("s1", new() { ["val"] = 30L }),
            ("s2", new() { ["val"] = 10L }),
            ("s3", new() { ["val"] = 50L }),
            ("s4", new() { ["val"] = 20L }),
            ("s5", new() { ["val"] = 40L }));

        var rows = Drain(conn.Query(
            "MATCH (n:Score) RETURN n.val ORDER BY n.val ASC LIMIT 2"));

        Assert.Equal(2, rows.Count);
        Assert.Equal(10L, rows[0].GetInt64(0));
        Assert.Equal(20L, rows[1].GetInt64(0));
    }

    [Fact]
    public void TopK_OrderByDesc_Limit3_ReturnsLargest3()
    {
        var conn = Setup("Item",
            ("i1", new() { ["score"] = 5L }),
            ("i2", new() { ["score"] = 2L }),
            ("i3", new() { ["score"] = 8L }),
            ("i4", new() { ["score"] = 1L }),
            ("i5", new() { ["score"] = 6L }),
            ("i6", new() { ["score"] = 3L }));

        var rows = Drain(conn.Query(
            "MATCH (n:Item) RETURN n.score ORDER BY n.score DESC LIMIT 3"));

        Assert.Equal(3, rows.Count);
        Assert.Equal(8L, rows[0].GetInt64(0));
        Assert.Equal(6L, rows[1].GetInt64(0));
        Assert.Equal(5L, rows[2].GetInt64(0));
    }

    [Fact]
    public void TopK_LimitLargerThanInput_ReturnsAll()
    {
        var conn = Setup("Tiny",
            ("t1", new() { ["v"] = 3L }),
            ("t2", new() { ["v"] = 1L }));

        var rows = Drain(conn.Query(
            "MATCH (n:Tiny) RETURN n.v ORDER BY n.v ASC LIMIT 100"));

        // K > n — all rows returned in sorted order
        Assert.Equal(2, rows.Count);
        Assert.Equal(1L, rows[0].GetInt64(0));
        Assert.Equal(3L, rows[1].GetInt64(0));
    }

    [Fact]
    public void TopK_StringOrder_TopNameAlphabetically()
    {
        var conn = Setup("Person",
            ("p1", new() { ["name"] = "Charlie" }),
            ("p2", new() { ["name"] = "Alice"   }),
            ("p3", new() { ["name"] = "Bob"     }),
            ("p4", new() { ["name"] = "Dave"    }));

        var rows = Drain(conn.Query(
            "MATCH (n:Person) RETURN n.name ORDER BY n.name ASC LIMIT 2"));

        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice",   rows[0].GetString(0));
        Assert.Equal("Bob",     rows[1].GetString(0));
    }

    [Fact]
    public void TopK_Limit0_ReturnsEmpty()
    {
        var conn = Setup("Z",
            ("z1", new() { ["v"] = 1L }),
            ("z2", new() { ["v"] = 2L }));

        var rows = Drain(conn.Query(
            "MATCH (n:Z) RETURN n.v ORDER BY n.v ASC LIMIT 0"));

        Assert.Empty(rows);
    }
}
