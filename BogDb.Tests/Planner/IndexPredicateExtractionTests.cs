using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Planner;

public class IndexPredicateExtractionTests
{
    private BogDatabase CreateDatabaseWithIndex()
    {
        var db = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id", LogicalTypeID.STRING },
            { "name", LogicalTypeID.STRING },
            { "age", LogicalTypeID.INT64 }
        });
        conn.UpsertNode("Person", "alice", new Dictionary<string, object>
            { { "id", "alice" }, { "name", "Alice" }, { "age", 30L } });
        conn.UpsertNode("Person", "bob", new Dictionary<string, object>
            { { "id", "bob" }, { "name", "Bob" }, { "age", 25L } });
        conn.UpsertNode("Person", "charlie", new Dictionary<string, object>
            { { "id", "charlie" }, { "name", "Charlie" }, { "age", 35L } });
        conn.Commit();

        conn.CreateIndex("Person", "name");
        conn.CreateIndex("Person", "age");

        return db;
    }

    /// <summary>
    /// Gets the physical plan text for a query via EXPLAIN.
    /// </summary>
    private static string GetPlan(BogConnection conn, string query)
    {
        var result = conn.Query($"EXPLAIN {query}");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        return result.GetNext().GetString(0);
    }

    [Fact]
    public void OrSameProperty_PlanContainsIndexScan()
    {
        using var db = CreateDatabaseWithIndex();
        using var conn = new BogConnection(db);

        // REAL: Verify the plan actually uses INDEX_SCAN, not SCAN + FILTER
        var plan = GetPlan(conn, "MATCH (p:Person) WHERE p.name = 'Alice' OR p.name = 'Bob' RETURN p.id");
        Assert.Contains("INDEX_SCAN", plan);
        Assert.DoesNotContain("FILTER", plan); // No post-filter needed — index handled it

        // Also verify correctness
        var result = conn.Query("MATCH (p:Person) WHERE p.name = 'Alice' OR p.name = 'Bob' RETURN p.id ORDER BY p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2UL, result.GetNumTuples());
        Assert.Equal("alice", result.GetNext().GetString(0));
        Assert.Equal("bob", result.GetNext().GetString(0));
    }

    [Fact]
    public void OrThreeValues_PlanContainsMultipleIndexScans()
    {
        using var db = CreateDatabaseWithIndex();
        using var conn = new BogConnection(db);

        // REAL: Verify three-way OR produces UNION_ALL of INDEX_SCANs
        var plan = GetPlan(conn, "MATCH (p:Person) WHERE p.name = 'Alice' OR p.name = 'Bob' OR p.name = 'Charlie' RETURN p.id");
        Assert.Contains("INDEX_SCAN", plan);
        Assert.Contains("UNION_ALL", plan); // Multiple scans being unioned

        var result = conn.Query("MATCH (p:Person) WHERE p.name = 'Alice' OR p.name = 'Bob' OR p.name = 'Charlie' RETURN p.id ORDER BY p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(3UL, result.GetNumTuples());
    }

    [Fact]
    public void OrDifferentProperties_PlanFallsBackToFilter()
    {
        using var db = CreateDatabaseWithIndex();
        using var conn = new BogConnection(db);

        // REAL: Different properties can't use OR-index; plan uses full scan, NOT index
        var plan = GetPlan(conn, "MATCH (p:Person) WHERE p.name = 'Alice' OR p.age = 25 RETURN p.id");
        Assert.Contains("SCAN_NODE_PROPERTY", plan); // Falls back to full table scan
        Assert.DoesNotContain("INDEX_SCAN", plan); // No index optimization

        // Correctness still works
        var result = conn.Query("MATCH (p:Person) WHERE p.name = 'Alice' OR p.age = 25 RETURN p.id ORDER BY p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2UL, result.GetNumTuples());
        Assert.Equal("alice", result.GetNext().GetString(0));
        Assert.Equal("bob", result.GetNext().GetString(0));
    }

    [Fact]
    public void InList_PlanContainsIndexScan()
    {
        using var db = CreateDatabaseWithIndex();
        using var conn = new BogConnection(db);

        // REAL: IN list should produce INDEX_SCAN
        var plan = GetPlan(conn, "MATCH (p:Person) WHERE p.name IN ['Alice', 'Bob'] RETURN p.id");
        Assert.Contains("INDEX_SCAN", plan);

        var result = conn.Query("MATCH (p:Person) WHERE p.name IN ['Alice', 'Bob'] RETURN p.id ORDER BY p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2UL, result.GetNumTuples());
        Assert.Equal("alice", result.GetNext().GetString(0));
        Assert.Equal("bob", result.GetNext().GetString(0));
    }

    [Fact]
    public void OrWithNonMatchingValue_IndexScanReturnsOnlyMatching()
    {
        using var db = CreateDatabaseWithIndex();
        using var conn = new BogConnection(db);

        // REAL: nonexistent key in OR still gets index-scanned (just yields no rows)
        var plan = GetPlan(conn, "MATCH (p:Person) WHERE p.name = 'Alice' OR p.name = 'Nonexistent' RETURN p.id");
        Assert.Contains("INDEX_SCAN", plan);

        var result = conn.Query("MATCH (p:Person) WHERE p.name = 'Alice' OR p.name = 'Nonexistent' RETURN p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        Assert.Equal("alice", result.GetNext().GetString(0));
    }

    [Fact]
    public void OrAndCombined_PlanUsesIndexForOrGroup()
    {
        using var db = CreateDatabaseWithIndex();
        using var conn = new BogConnection(db);

        // REAL: (OR group) AND equality — plan uses HASH_JOIN to intersect index results
        var plan = GetPlan(conn, "MATCH (p:Person) WHERE (p.name = 'Alice' OR p.name = 'Bob') AND p.age = 30 RETURN p.id");
        Assert.Contains("HASH_JOIN", plan); // Cross-property intersection via hash join
        Assert.DoesNotContain("SCAN_NODE_PROPERTY", plan); // Not falling back to full scan

        var result = conn.Query("MATCH (p:Person) WHERE (p.name = 'Alice' OR p.name = 'Bob') AND p.age = 30 RETURN p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        Assert.Equal("alice", result.GetNext().GetString(0));
    }

    [Fact]
    public void SingleEquality_PlanContainsIndexScan_Baseline()
    {
        using var db = CreateDatabaseWithIndex();
        using var conn = new BogConnection(db);

        // Baseline: single equality uses index scan — proves EXPLAIN works
        var plan = GetPlan(conn, "MATCH (p:Person) WHERE p.name = 'Alice' RETURN p.id");
        Assert.Contains("INDEX_SCAN", plan);
        Assert.DoesNotContain("FILTER", plan);
    }

    [Fact]
    public void NoIndex_PlanUsesScanNotIndexScan_Baseline()
    {
        using var db = CreateDatabaseWithIndex();
        using var conn = new BogConnection(db);

        // Baseline: non-indexed property uses SCAN_NODE_PROPERTY, not INDEX_SCAN
        var plan = GetPlan(conn, "MATCH (p:Person) WHERE p.id = 'alice' RETURN p.name");
        Assert.Contains("SCAN_NODE_PROPERTY", plan);
        Assert.DoesNotContain("INDEX_SCAN", plan); // Proves EXPLAIN distinguishes paths
    }
}
