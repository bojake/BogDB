using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Processor;

/// <summary>
/// Tests for aggregate functions in RETURN clauses: COUNT, SUM, AVG, MIN, MAX.
/// All tests create a small in-memory Person graph and run aggregate queries.
/// </summary>
public sealed class AggregateTests
{
    // ── Setup helper ──────────────────────────────────────────────────────────

    private static BogConnection CreateDb(out BogDatabase db)
    {
        db = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  }
        });
        conn.Commit();

        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", 1L, new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });
        conn.UpsertNode("Person", 2L, new Dictionary<string, object> { ["name"] = "Bob",   ["age"] = 25L });
        conn.UpsertNode("Person", 3L, new Dictionary<string, object> { ["name"] = "Carol",  ["age"] = 35L });
        conn.Commit();

        return conn;
    }

    // ── COUNT(*) ─────────────────────────────────────────────────────────────

    [Fact]
    public void Count_Star_ReturnsNodeCount()
    {
        using var conn = CreateDb(out var db);
        var result = conn.Query("MATCH (n:Person) RETURN count(*)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(3L, result.GetNext().GetInt64(0));
    }

    [Fact]
    public void Count_Property_ReturnsNodeCount()
    {
        using var conn = CreateDb(out var db);
        var result = conn.Query("MATCH (n:Person) RETURN count(n.age)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(3L, result.GetNext().GetInt64(0));
    }

    [Fact]
    public void Count_WithFilter_ReturnsFilteredCount()
    {
        using var conn = CreateDb(out var db);
        var result = conn.Query("MATCH (n:Person) WHERE n.age > 25 RETURN count(*)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        // Alice (30) and Carol (35) pass the filter — Bob (25) does not
        Assert.Equal(2L, result.GetNext().GetInt64(0));
    }

    // ── SUM ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Sum_Age_ReturnsTotalAge()
    {
        using var conn = CreateDb(out var db);
        var result = conn.Query("MATCH (n:Person) RETURN sum(n.age)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        // 30 + 25 + 35 = 90
        var val = result.GetNext().GetDouble(0);
        Assert.Equal(90.0, val, precision: 1);
    }

    // ── AVG ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Avg_Age_ReturnsAverageAge()
    {
        using var conn = CreateDb(out var db);
        var result = conn.Query("MATCH (n:Person) RETURN avg(n.age)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        // (30 + 25 + 35) / 3 = 30.0
        var val = result.GetNext().GetDouble(0);
        Assert.Equal(30.0, val, precision: 2);
    }

    // ── MIN / MAX ─────────────────────────────────────────────────────────────

    [Fact]
    public void Min_Age_ReturnsMinimumAge()
    {
        using var conn = CreateDb(out var db);
        var result = conn.Query("MATCH (n:Person) RETURN min(n.age)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var val = result.GetNext().GetDouble(0);
        Assert.Equal(25.0, val, precision: 1);
    }

    [Fact]
    public void Max_Age_ReturnsMaximumAge()
    {
        using var conn = CreateDb(out var db);
        var result = conn.Query("MATCH (n:Person) RETURN max(n.age)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var val = result.GetNext().GetDouble(0);
        Assert.Equal(35.0, val, precision: 1);
    }

    // ── Multi-aggregate ───────────────────────────────────────────────────────

    [Fact]
    public void MultiAggregate_CountAndSum_ReturnsBothValues()
    {
        using var conn = CreateDb(out var db);
        var result = conn.Query("MATCH (n:Person) RETURN count(*), sum(n.age)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal(3L, row.GetInt64(0));           // count(*) = 3
        Assert.Equal(90.0, row.GetDouble(1), precision: 1); // sum = 90
    }

    [Fact]
    public void Sum_CaseWhen_ReturnsConditionalCount()
    {
        using var conn = CreateDb(out var db);
        var result = conn.Query(
            "MATCH (n:Person) RETURN sum(CASE WHEN n.age > 25 THEN 1 ELSE 0 END)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(2.0, result.GetNext().GetDouble(0), precision: 1);
    }

    [Fact]
    public void CountDistinct_MixedScalarTypes_RemainsTypeSensitive()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "UNWIND [1, '1', 1.0, 2] AS v RETURN count(DISTINCT v)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(3L, result.GetNext().GetInt64(0));
    }

    [Fact]
    public void SumDistinct_NumericValues_NormalizesEquivalentNumbers()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "UNWIND [1, 1.0, 2] AS v RETURN sum(DISTINCT v)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(3.0, result.GetNext().GetDouble(0), precision: 5);
    }

    [Fact]
    public void CountDistinct_StructValues_UsesStructuralEquality()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "UNWIND [{a:1, b:2}, {b:2, a:1}, {a:2, b:3}] AS s RETURN count(DISTINCT s)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(2L, result.GetNext().GetInt64(0));
    }
}
