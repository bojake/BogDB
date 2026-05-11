using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Optimizer;

/// <summary>
/// Tests for DP-based join enumeration.
/// Verifies that multi-way join queries produce correct results
/// after the optimizer explores join orderings.
/// </summary>
public sealed class DPJoinEnumeratorTests
{
    private static BogConnection SetupMultiJoinGraph()
    {
        var db = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        // Create a schema with 4 node types and 3 rel types to force multi-way joins
        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE NODE TABLE City(id STRING, name STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE NODE TABLE Company(id STRING, name STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE NODE TABLE School(id STRING, name STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE LIVES_IN(FROM Person TO City)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE WORKS_AT(FROM Person TO Company)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE STUDIED_AT(FROM Person TO School)").IsSuccess);

        // Populate
        Assert.True(conn.Query("CREATE (:Person {id:'p1', name:'Alice'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'p2', name:'Bob'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:City {id:'c1', name:'Seattle'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:City {id:'c2', name:'Portland'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Company {id:'co1', name:'Acme'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:School {id:'s1', name:'UW'})").IsSuccess);

        Assert.True(conn.Query("MATCH (p:Person {id:'p1'}), (c:City {id:'c1'}) CREATE (p)-[:LIVES_IN]->(c)").IsSuccess);
        Assert.True(conn.Query("MATCH (p:Person {id:'p1'}), (co:Company {id:'co1'}) CREATE (p)-[:WORKS_AT]->(co)").IsSuccess);
        Assert.True(conn.Query("MATCH (p:Person {id:'p1'}), (s:School {id:'s1'}) CREATE (p)-[:STUDIED_AT]->(s)").IsSuccess);
        Assert.True(conn.Query("MATCH (p:Person {id:'p2'}), (c:City {id:'c2'}) CREATE (p)-[:LIVES_IN]->(c)").IsSuccess);
        Assert.True(conn.Query("MATCH (p:Person {id:'p2'}), (co:Company {id:'co1'}) CREATE (p)-[:WORKS_AT]->(co)").IsSuccess);

        return conn;
    }

    [Fact]
    public void ThreeWayJoin_ProducesCorrectResults()
    {
        using var conn = SetupMultiJoinGraph();

        // 3-way join: Person → City + Person → Company
        var result = conn.Query(
            "MATCH (p:Person)-[:LIVES_IN]->(c:City), " +
            "(p)-[:WORKS_AT]->(co:Company) " +
            "RETURN p.name, c.name, co.name ORDER BY p.name");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var row1 = result.GetNext();
        Assert.Equal("Alice", row1.GetString(0));
        Assert.Equal("Seattle", row1.GetString(1));
        Assert.Equal("Acme", row1.GetString(2));

        var row2 = result.GetNext();
        Assert.Equal("Bob", row2.GetString(0));
        Assert.Equal("Portland", row2.GetString(1));
        Assert.Equal("Acme", row2.GetString(2));

        Assert.False(result.HasNext());
    }

    [Fact]
    public void FourWayJoin_ProducesCorrectResults()
    {
        using var conn = SetupMultiJoinGraph();

        // 4-way join: Person → City + Person → Company + Person → School
        var result = conn.Query(
            "MATCH (p:Person)-[:LIVES_IN]->(c:City), " +
            "(p)-[:WORKS_AT]->(co:Company), " +
            "(p)-[:STUDIED_AT]->(s:School) " +
            "RETURN p.name, c.name, co.name, s.name ORDER BY p.name");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        // Only Alice studied at UW
        var row1 = result.GetNext();
        Assert.Equal("Alice", row1.GetString(0));
        Assert.Equal("Seattle", row1.GetString(1));
        Assert.Equal("Acme", row1.GetString(2));
        Assert.Equal("UW", row1.GetString(3));

        Assert.False(result.HasNext());
    }

    [Fact]
    public void IndependentPatterns_CrossProduct()
    {
        using var conn = SetupMultiJoinGraph();

        // Independent patterns (no shared variables) → cross product
        var result = conn.Query(
            "MATCH (p:Person {id:'p1'}), (c:City {id:'c1'}), (co:Company {id:'co1'}) " +
            "RETURN p.name, c.name, co.name");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var row = result.GetNext();
        Assert.Equal("Alice", row.GetString(0));
        Assert.Equal("Seattle", row.GetString(1));
        Assert.Equal("Acme", row.GetString(2));

        Assert.False(result.HasNext());
    }

    [Fact]
    public void TwoWayJoin_NotAffectedByDP()
    {
        using var conn = SetupMultiJoinGraph();

        // Simple 2-way join — DP should NOT activate (below threshold)
        var result = conn.Query(
            "MATCH (p:Person)-[:LIVES_IN]->(c:City) " +
            "RETURN p.name, c.name ORDER BY p.name");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var row1 = result.GetNext();
        Assert.Equal("Alice", row1.GetString(0));
        Assert.Equal("Seattle", row1.GetString(1));

        var row2 = result.GetNext();
        Assert.Equal("Bob", row2.GetString(0));
        Assert.Equal("Portland", row2.GetString(1));

        Assert.False(result.HasNext());
    }
}
