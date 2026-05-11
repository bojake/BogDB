using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class SubqueryExecutionTests
{
    [Fact]
    public void ExistsSubqueryInWhere_FiltersRows()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'a', name:'Alice'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'b', name:'Bob'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'c', name:'Cara'})").IsSuccess);
        Assert.True(conn.Query("MATCH (a:Person {id:'a'}), (b:Person {id:'b'}) CREATE (a)-[:KNOWS]->(b)").IsSuccess);

        var result = conn.Query(
            "MATCH (a:Person) WHERE EXISTS { MATCH (a)-[:KNOWS]->(b:Person) } RETURN a.name ORDER BY a.name");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("Alice", result.GetNext().GetString(0));
        Assert.False(result.HasNext());
    }

    [Fact]
    public void CountSubqueryInReturn_ComputesDegree()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'a'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'b'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'c'})").IsSuccess);
        Assert.True(conn.Query("MATCH (a:Person {id:'a'}), (b:Person {id:'b'}) CREATE (a)-[:KNOWS]->(b)").IsSuccess);
        Assert.True(conn.Query("MATCH (a:Person {id:'a'}), (c:Person {id:'c'}) CREATE (a)-[:KNOWS]->(c)").IsSuccess);

        var result = conn.Query(
            "MATCH (a:Person) RETURN a.id, COUNT { MATCH (a)-[:KNOWS]->(b:Person) } AS degree ORDER BY a.id");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var row1 = result.GetNext();
        var row2 = result.GetNext();
        var row3 = result.GetNext();

        Assert.Equal("a", row1.GetString(0));
        Assert.Equal(2L, row1.GetInt64(1));
        Assert.Equal("b", row2.GetString(0));
        Assert.Equal(0L, row2.GetInt64(1));
        Assert.Equal("c", row3.GetString(0));
        Assert.Equal(0L, row3.GetInt64(1));
    }

    [Fact]
    public void NotExistsSubquery_WorksForAntiMatch()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'a'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'b'})").IsSuccess);
        Assert.True(conn.Query("MATCH (a:Person {id:'a'}), (b:Person {id:'b'}) CREATE (a)-[:KNOWS]->(b)").IsSuccess);

        var result = conn.Query(
            "MATCH (a:Person) WHERE NOT EXISTS { MATCH (a)-[:KNOWS]->(b:Person) } RETURN a.id");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("b", result.GetNext().GetString(0));
        Assert.False(result.HasNext());
    }

    [Fact]
    public void ExistsSubqueryWithInnerFilter_UsesCorrelatedBindings()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, fName STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'a', fName:'Alice'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'b', fName:'Bob'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'c', fName:'Cara'})").IsSuccess);
        Assert.True(conn.Query("MATCH (a:Person {id:'a'}), (b:Person {id:'b'}) CREATE (a)-[:KNOWS]->(b)").IsSuccess);
        Assert.True(conn.Query("MATCH (c:Person {id:'c'}), (b:Person {id:'b'}) CREATE (c)-[:KNOWS]->(b)").IsSuccess);

        var result = conn.Query(
            "MATCH (a:Person) WHERE EXISTS { MATCH (a)-[:KNOWS]->(b:Person) WHERE b.fName = 'Bob' } RETURN a.fName ORDER BY a.fName");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("Alice", result.GetNext().GetString(0));
        Assert.True(result.HasNext());
        Assert.Equal("Cara", result.GetNext().GetString(0));
        Assert.False(result.HasNext());
    }

    [Fact]
    public void RawPatternAtomInWhere_LowersToExistsSemantics()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'a'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'b'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'c'})").IsSuccess);
        Assert.True(conn.Query("MATCH (a:Person {id:'a'}), (b:Person {id:'b'}) CREATE (a)-[:KNOWS]->(b)").IsSuccess);

        var result = conn.Query(
            "MATCH (a:Person), (b:Person) WHERE (a)-[:KNOWS]->(b) RETURN a.id, b.id ORDER BY a.id, b.id");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("a", row.GetString(0));
        Assert.Equal("b", row.GetString(1));
        Assert.False(result.HasNext());
    }

    [Fact]
    public void RawNegatedPatternAtomWithRelPropertyMap_UsesCorrelatedBindings()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE HealthPlan(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE NODE TABLE Manager(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE NODE TABLE Supplier(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE CONTRACTS_WITH(FROM HealthPlan TO Manager, date_range STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE RESPONSIBLE_FOR(FROM Manager TO Supplier, contract_dates STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE (:HealthPlan {id:'hp1'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Manager {id:'mg1'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Supplier {id:'sc1'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Supplier {id:'sc2'})").IsSuccess);
        Assert.True(conn.Query("MATCH (hp:HealthPlan {id:'hp1'}), (mg:Manager {id:'mg1'}) CREATE (hp)-[:CONTRACTS_WITH {date_range:'2024'}]->(mg)").IsSuccess);
        Assert.True(conn.Query("MATCH (mg:Manager {id:'mg1'}), (sc:Supplier {id:'sc1'}) CREATE (mg)-[:RESPONSIBLE_FOR {contract_dates:'2024'}]->(sc)").IsSuccess);

        var result = conn.Query(
            "MATCH (hp:HealthPlan)-[c:CONTRACTS_WITH]->(mg:Manager) " +
            "MATCH (sc:Supplier) " +
            "WHERE NOT (mg)-[:RESPONSIBLE_FOR {contract_dates: c.date_range}]->(sc) " +
            "RETURN hp.id, mg.id, sc.id ORDER BY hp.id, mg.id, sc.id");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("hp1", row.GetString(0));
        Assert.Equal("mg1", row.GetString(1));
        Assert.Equal("sc2", row.GetString(2));
        Assert.False(result.HasNext());
    }
}
