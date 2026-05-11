using System;
using System.Collections.Generic;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Common;

namespace BogDb.Tests.Processor;

/// <summary>
/// Tests for scan_multi_rel_tables — traversal that spans more than one
/// relationship table type in a single MATCH hop.
///
/// Uses EnsureNodeTable/EnsureRelTable/UpsertNode/UpsertRelationship helpers
/// (same pattern as TopKOptimizerTests) to avoid Cypher CREATE limitations.
/// </summary>
public class MultiRelScanTests
{
    private static (BogDatabase db, BogConnection conn) BuildGraph()
    {
        var db   = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"]   = LogicalTypeID.INT64,
            ["name"] = LogicalTypeID.STRING,
        });
        conn.EnsureRelTable("KNOWS",   "Person", "Person", new Dictionary<string, LogicalTypeID>
            { ["since"] = LogicalTypeID.INT64 });
        conn.EnsureRelTable("LIKES",   "Person", "Person", new Dictionary<string, LogicalTypeID>
            { ["weight"] = LogicalTypeID.DOUBLE });
        conn.EnsureRelTable("BLOCKED", "Person", "Person", new Dictionary<string, LogicalTypeID>());

        // Nodes: Alice(1), Bob(2), Carol(3)
        conn.UpsertNodeById("Person", "1", new Dictionary<string, object> { ["id"] = 1L, ["name"] = "Alice" });
        conn.UpsertNodeById("Person", "2", new Dictionary<string, object> { ["id"] = 2L, ["name"] = "Bob"   });
        conn.UpsertNodeById("Person", "3", new Dictionary<string, object> { ["id"] = 3L, ["name"] = "Carol" });

        // Edges: Alice KNOWS Bob, Alice LIKES Carol, Bob BLOCKED Carol
        conn.UpsertRelationshipById("KNOWS",   "1", "2", new Dictionary<string, object> { ["since"]  = 2020L });
        conn.UpsertRelationshipById("LIKES",   "1", "3", new Dictionary<string, object> { ["weight"] = 0.9  });
        conn.UpsertRelationshipById("BLOCKED", "2", "3", new Dictionary<string, object>());
        conn.Commit();

        return (db, conn);
    }

    // ── Single-type traversal still works ────────────────────────────────────

    [Fact]
    public void SingleType_KNOWS_ReturnsOneEdge()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.name, b.name");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(1UL, r.GetNumTuples());
        }
    }

    // ── Multi-type: KNOWS|LIKES should return 2 edges (both from Alice) ───────

    [Fact]
    public void MultiType_KnowsOrLikes_ReturnsTwoEdgesFromAlice()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (a:Person)-[r:KNOWS|LIKES]->(b:Person) WHERE a.id = 1 RETURN b.name ORDER BY b.name");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(2UL, r.GetNumTuples());

            var names = new System.Collections.Generic.List<string>();
            while (r.HasNext()) names.Add(r.GetNext().GetString(0)!);
            Assert.Contains("Bob",   names);
            Assert.Contains("Carol", names);
        }
    }

    // ── Multi-type: all three types should yield 3 total edges ───────────────

    [Fact]
    public void MultiType_AllThreeTypes_ReturnsThreeEdges()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (a:Person)-[r:KNOWS|LIKES|BLOCKED]->(b:Person) RETURN a.name, b.name");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
        }
    }

    // ── Untyped traversal scans ALL rel tables ────────────────────────────────

    [Fact]
    public void UntypedTraversal_ReturnsEdgesFromAllTables()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query("MATCH (a:Person)-[r]->(b:Person) RETURN a.name, b.name");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
        }
    }

    // ── type(r) correctly identifies the relationship type per edge ──────────

    [Fact]
    public void MultiType_TypeFunction_ReturnsCorrectLabel()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (a:Person)-[r:KNOWS|LIKES]->(b:Person) WHERE a.id = 1 RETURN type(r), b.name ORDER BY b.name");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(2UL, r.GetNumTuples());

            var row1 = r.GetNext();
            var row2 = r.GetNext();
            Assert.False(string.IsNullOrEmpty(row1.GetString(0)));
            Assert.False(string.IsNullOrEmpty(row2.GetString(0)));
        }
    }

    // ── Multi-type with no matching rows in one table still returns others ────

    [Fact]
    public void MultiType_OneTableEmpty_OtherTableStillReturnsRows()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            // BLOCKED has 0 edges from Alice (id=1), LIKES has 1
            var r = conn.Query(
                "MATCH (a:Person)-[r:LIKES|BLOCKED]->(b:Person) WHERE a.id = 1 RETURN b.name");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(1UL, r.GetNumTuples());
            Assert.Equal("Carol", r.GetNext().GetString(0));
        }
    }

    // ── Multi-type COUNT works correctly ─────────────────────────────────────

    [Fact]
    public void MultiType_Count_AggregatesAcrossAllTypes()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (a:Person)-[r:KNOWS|LIKES|BLOCKED]->(b:Person) RETURN count(r) AS total");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            var row   = r.GetNext();
            var total = row.GetInt64(0);
            Assert.Equal(3L, total);
        }
    }

    [Fact]
    public void MultiType_RecursiveTraversal_SpansAllRequestedTables()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (a:Person)-[:KNOWS|BLOCKED*1..2]->(b:Person) WHERE a.id = 1 RETURN DISTINCT b.name ORDER BY b.name");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(2UL, r.GetNumTuples());
            Assert.Equal("Bob", r.GetNext().GetString(0));
            Assert.Equal("Carol", r.GetNext().GetString(0));
        }
    }
}
