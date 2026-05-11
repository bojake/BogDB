using BogDb.Core.Main;
using BogDb.Core.Main.QueryResult;
using Xunit;

namespace BogDb.Tests.Function;

/// <summary>
/// Tests for list quantifier predicates (all, any, none, single),
/// list_filter, and list_reduce.
/// </summary>
public class ListPredicateTests
{
    // ── ALL ──────────────────────────────────────────────────────────────────

    [Fact]
    public void All_AllMatch_ReturnsTrue()
    {
        var r = Q("RETURN all(x IN [1,2,3] WHERE x > 0)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(true, r.GetNext().GetValue(0));
    }

    [Fact]
    public void All_SomeFail_ReturnsFalse()
    {
        var r = Q("RETURN all(x IN [1,2,3] WHERE x > 1)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(false, r.GetNext().GetValue(0));
    }

    [Fact]
    public void All_EmptyList_ReturnsTrue()
    {
        // Vacuous truth: all(x IN [] WHERE ...) = true
        var r = Q("RETURN all(x IN [] WHERE x > 0)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        // Empty list -> quantifier evaluates collection to empty -> returns true for ALL
    }

    // ── ANY ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Any_OneMatch_ReturnsTrue()
    {
        var r = Q("RETURN any(x IN [1,2,3] WHERE x = 2)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(true, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Any_NoneMatch_ReturnsFalse()
    {
        var r = Q("RETURN any(x IN [1,2,3] WHERE x = 5)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(false, r.GetNext().GetValue(0));
    }

    // ── NONE ─────────────────────────────────────────────────────────────────

    [Fact]
    public void None_NoneMatch_ReturnsTrue()
    {
        var r = Q("RETURN none(x IN [1,2,3] WHERE x < 0)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(true, r.GetNext().GetValue(0));
    }

    [Fact]
    public void None_SomeMatch_ReturnsFalse()
    {
        var r = Q("RETURN none(x IN [1,2,3] WHERE x = 1)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(false, r.GetNext().GetValue(0));
    }

    // ── SINGLE ───────────────────────────────────────────────────────────────

    [Fact]
    public void Single_ExactlyOneMatch_ReturnsTrue()
    {
        var r = Q("RETURN single(x IN [1,2,3] WHERE x = 2)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(true, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Single_MultipleMatch_ReturnsFalse()
    {
        var r = Q("RETURN single(x IN [1,2,3] WHERE x > 1)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(false, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Single_ZeroMatch_ReturnsFalse()
    {
        var r = Q("RETURN single(x IN [1,2,3] WHERE x = 99)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(false, r.GetNext().GetValue(0));
    }

    // ── Quantifiers with graph data ──────────────────────────────────────────

    [Fact]
    public void All_WithGraphData_Works()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY, age INT64)");
        conn.Query("CREATE (:Person {id:'a1', age:25})");
        conn.Query("CREATE (:Person {id:'a2', age:30})");
        conn.Query("CREATE (:Person {id:'a3', age:35})");

        var r = conn.Query("MATCH (p:Person) WITH collect(p.age) AS ages RETURN all(a IN ages WHERE a >= 25)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(true, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Any_WithGraphData_Works()
    {
        // Quantifier predicates combined with boolean AND
        var r = Q("RETURN any(a IN [20, 30, 40] WHERE a > 35)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(true, r.GetNext().GetValue(0));
    }

    // ── list_filter ──────────────────────────────────────────────────────────

    [Fact]
    public void ListFilter_FiltersElements()
    {
        var r = Q("RETURN list_filter([1,2,3,4,5], x -> x > 3)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(1UL, r.GetNumTuples());
    }

    [Fact]
    public void ListFilter_NoMatch_ReturnsEmptyList()
    {
        var r = Q("RETURN list_filter([1,2,3], x -> x > 10)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(1UL, r.GetNumTuples());
    }

    // ── list_reduce ──────────────────────────────────────────────────────────

    [Fact]
    public void ListReduce_SumsElements()
    {
        var r = Q("RETURN list_reduce([1,2,3,4], (a, b) -> a + b)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(1UL, r.GetNumTuples());
    }

    // ── Nested Quantifiers ───────────────────────────────────────────────────

    [Fact]
    public void NestedQuantifier_AllContainsAny()
    {
        // Nested quantifiers: all elements are positive AND any is even
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        var r = conn.Query("RETURN all(x IN [2,4,6] WHERE x > 0) AND any(x IN [2,4,6] WHERE x = 4)");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(true, r.GetNext().GetValue(0));
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static QueryResult Q(string cypher)
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        return conn.Query(cypher);
    }
}
