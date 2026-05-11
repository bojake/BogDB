using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

/// <summary>
/// E2E tests for CALL { subquery } inline subquery execution.
/// Tests both non-correlated and correlated subquery forms.
/// </summary>
public sealed class CallSubqueryExecutionTests
{
    private static BogConnection SetupTestGraph()
    {
        var db = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, name STRING, age INT64)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'a', name:'Alice', age:30})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'b', name:'Bob', age:25})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Person {id:'c', name:'Cara', age:35})").IsSuccess);
        Assert.True(conn.Query("MATCH (a:Person {id:'a'}), (b:Person {id:'b'}) CREATE (a)-[:KNOWS]->(b)").IsSuccess);
        Assert.True(conn.Query("MATCH (a:Person {id:'a'}), (c:Person {id:'c'}) CREATE (a)-[:KNOWS]->(c)").IsSuccess);
        Assert.True(conn.Query("MATCH (b:Person {id:'b'}), (c:Person {id:'c'}) CREATE (b)-[:KNOWS]->(c)").IsSuccess);

        return conn;
    }

    [Fact]
    public void NonCorrelatedCallSubquery_CrossProduct()
    {
        using var conn = SetupTestGraph();

        // Non-correlated: CALL { MATCH ... RETURN count(*) AS total }
        // Each outer row should see the total count of persons
        var result = conn.Query(
            "MATCH (a:Person) " +
            "CALL { MATCH (b:Person) RETURN count(*) AS total } " +
            "RETURN a.name, total ORDER BY a.name");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var row1 = result.GetNext();
        Assert.Equal("Alice", row1.GetString(0));
        Assert.Equal(3L, row1.GetInt64(1));

        var row2 = result.GetNext();
        Assert.Equal("Bob", row2.GetString(0));
        Assert.Equal(3L, row2.GetInt64(1));

        var row3 = result.GetNext();
        Assert.Equal("Cara", row3.GetString(0));
        Assert.Equal(3L, row3.GetInt64(1));

        Assert.False(result.HasNext());
    }

    [Fact]
    public void NonCorrelatedCallSubquery_IndependentMatch()
    {
        using var conn = SetupTestGraph();

        // Non-correlated CALL that returns multiple columns
        var result = conn.Query(
            "MATCH (a:Person {id:'a'}) " +
            "CALL { MATCH (x:Person) WHERE x.age > 28 RETURN x.name AS mature_name ORDER BY mature_name } " +
            "RETURN a.name, mature_name ORDER BY mature_name");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        // Alice (30) and Cara (35) are > 28, so we get Alice cross-producted with those two
        var row1 = result.GetNext();
        Assert.Equal("Alice", row1.GetString(0));
        Assert.Equal("Alice", row1.GetString(1));

        var row2 = result.GetNext();
        Assert.Equal("Alice", row2.GetString(0));
        Assert.Equal("Cara", row2.GetString(1));

        Assert.False(result.HasNext());
    }

    [Fact]
    public void CorrelatedCallSubquery_WithVariable()
    {
        using var conn = SetupTestGraph();

        // Correlated: CALL { WITH a MATCH (a)-[:KNOWS]->(b) RETURN count(b) AS cnt }
        var result = conn.Query(
            "MATCH (a:Person) " +
            "CALL { WITH a MATCH (a)-[:KNOWS]->(b:Person) RETURN count(*) AS cnt } " +
            "RETURN a.name, cnt ORDER BY a.name");

        Assert.True(result.IsSuccess, result.ErrorMessage);

        var row1 = result.GetNext();
        Assert.Equal("Alice", row1.GetString(0));
        Assert.Equal(2L, row1.GetInt64(1));  // Alice knows Bob and Cara

        var row2 = result.GetNext();
        Assert.Equal("Bob", row2.GetString(0));
        Assert.Equal(1L, row2.GetInt64(1));  // Bob knows Cara

        var row3 = result.GetNext();
        Assert.Equal("Cara", row3.GetString(0));
        Assert.Equal(0L, row3.GetInt64(1));  // Cara knows nobody

        Assert.False(result.HasNext());
    }

    [Fact]
    public void CallSubquery_ParsesCorrectly()
    {
        using var conn = SetupTestGraph();

        // Simple parse test - make sure CALL { } doesn't cause parse errors
        var result = conn.Query(
            "CALL { MATCH (p:Person) RETURN count(*) AS cnt } RETURN cnt");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(3L, result.GetNext().GetInt64(0));
    }

    [Fact]
    public void CallSubqueryPreprocessor_DetectsCallBracePattern()
    {
        var preproc = new BogDb.Core.Parser.Antlr4.CallSubqueryPreprocessor();

        // Simple CALL { ... }
        var result = preproc.Preprocess(
            "MATCH (a:Person) CALL { MATCH (b:Person) RETURN count(*) AS total } RETURN a.name, total");

        Assert.Single(preproc.ExtractedBodies);
        Assert.Contains("MATCH (b:Person) RETURN count(*) AS total", preproc.ExtractedBodies[0]);
        Assert.Contains("MATCH (__call_subquery_0:__CallSubquery)", result);
    }

    [Fact]
    public void CallSubqueryPreprocessor_HandlesMultipleBlocks()
    {
        var preproc = new BogDb.Core.Parser.Antlr4.CallSubqueryPreprocessor();

        var result = preproc.Preprocess(
            "CALL { RETURN 1 AS x } CALL { RETURN 2 AS y } RETURN x, y");

        Assert.Equal(2, preproc.ExtractedBodies.Count);
        Assert.Contains("MATCH (__call_subquery_0:__CallSubquery)", result);
        Assert.Contains("MATCH (__call_subquery_1:__CallSubquery)", result);
    }

    [Fact]
    public void CallSubqueryPreprocessor_NoCallSubquery_PassesThrough()
    {
        var preproc = new BogDb.Core.Parser.Antlr4.CallSubqueryPreprocessor();

        var original = "MATCH (a:Person) RETURN a.name";
        var result = preproc.Preprocess(original);

        Assert.Empty(preproc.ExtractedBodies);
        Assert.Equal(original, result);
    }

    [Fact]
    public void CallSubqueryPreprocessor_IgnoresCallFunction()
    {
        var preproc = new BogDb.Core.Parser.Antlr4.CallSubqueryPreprocessor();

        // CALL with parentheses (function call) should NOT be matched
        var original = "CALL db.tables() YIELD *";
        var result = preproc.Preprocess(original);

        Assert.Empty(preproc.ExtractedBodies);
        Assert.Equal(original, result);
    }
}
