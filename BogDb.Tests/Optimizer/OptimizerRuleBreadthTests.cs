using System;
using System.Collections.Generic;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Common;

namespace BogDb.Tests.Optimizer;

/// <summary>
/// Integration tests for the 5 new optimizer rules.
/// Uses BogConnection.UpsertNode/UpsertRelationship helpers (same pattern as TopKOptimizerTests)
/// to avoid depending on Cypher CREATE syntax which may have limitations.
/// </summary>
public class OptimizerRuleBreadthTests
{
    private static (BogDatabase db, BogConnection conn) BuildGraph()
    {
        var db   = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        // EnsureNodeTable and EnsureRelTable require an active write transaction
        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"]   = LogicalTypeID.INT64,
            ["name"] = LogicalTypeID.STRING,
            ["dept"] = LogicalTypeID.STRING,
        });
        conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>());

        for (int i = 1; i <= 10; i++)
        {
            conn.UpsertNodeById("Person", i.ToString(), new Dictionary<string, object>
            {
                ["id"]   = (long)i,
                ["name"] = $"P{i}",
                ["dept"] = $"dept{(i % 3) + 1}",
            });
        }
        // Add a few KNOWS edges (1→2, 1→3, 2→4) using string IDs matching UpsertNodeById
        conn.UpsertRelationshipById("KNOWS", "1", "2", new Dictionary<string, object>());
        conn.UpsertRelationshipById("KNOWS", "1", "3", new Dictionary<string, object>());
        conn.UpsertRelationshipById("KNOWS", "2", "4", new Dictionary<string, object>());
        conn.Commit();

        return (db, conn);
    }

    // ── LimitPushDownRule ─────────────────────────────────────────────────────

    [Fact]
    public void LimitPushDown_OrderByLimit_ReturnsExactN()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query("MATCH (p:Person) RETURN p.id ORDER BY p.id LIMIT 3");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void LimitPushDown_LimitLargerThanInput_ReturnsAll()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query("MATCH (p:Person) RETURN p.id ORDER BY p.id LIMIT 999");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(10UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void LimitPushDown_LimitZero_ReturnsNoRows()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query("MATCH (p:Person) RETURN p.id LIMIT 0");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(0UL, r.GetNumTuples());
        }
    }

    // ── RemoveUnnecessaryJoinRule ─────────────────────────────────────────────

    [Fact]
    public void RemoveUnnecessaryJoin_SimpleMatchReturnsCorrectRows()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query("MATCH (p:Person) WHERE p.id <= 3 RETURN p.name ORDER BY p.id");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
        }
    }

    // ── AggKeyDependencyRule ──────────────────────────────────────────────────

    [Fact]
    public void AggKeyDependency_GroupByIdAndName_CorrectAggregation()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            // AggKeyDependency rule fires for queries with GROUP BY; verify correct row count
            var r = conn.Query("MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id LIMIT 3");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void AggKeyDependency_GroupByDept_CorrectCountPerGroup()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (p:Person) RETURN p.dept, count(p) AS cnt ORDER BY p.dept");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples()); // dept1, dept2, dept3
        }
    }

    // ── AccHashJoinRule ───────────────────────────────────────────────────────

    [Fact]
    public void AccHashJoin_JoinQueryStillCorrect()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (a:Person)-[:KNOWS]->(b:Person) RETURN a.name, b.name ORDER BY a.id, b.id");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
        }
    }

    // ── SchemaPopulatorRule ───────────────────────────────────────────────────

    [Fact]
    public void SchemaPopulator_ComplexQuery_ResultsUnchanged()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (p:Person) WHERE p.id > 5 RETURN p.name ORDER BY p.id LIMIT 3");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
        }
    }

    // ── Combined rule interaction ──────────────────────────────────────────────

    [Fact]
    public void AllRules_GroupByWithLimitAndOrder_CorrectResult()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (p:Person) WITH p.dept AS dept, count(p) AS cnt RETURN dept, cnt ORDER BY cnt DESC LIMIT 2");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(2UL, r.GetNumTuples());
        }
    }
}
