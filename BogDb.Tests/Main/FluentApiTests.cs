using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.Main;
using BogDb.Core.Main.QueryResult;

namespace BogDb.Tests.Main;

/// <summary>
/// Tests for the fluent API — CypherFluentQuery, GraphMutationBuilder,
/// and QueryResult IEnumerable/convenience methods.
/// </summary>
public class FluentApiTests : IDisposable
{
    private readonly BogDatabase _db;
    private readonly BogConnection _conn;

    public FluentApiTests()
    {
        _db = BogDatabase.CreateInMemory();
        _conn = new BogConnection(_db);

        // Seed test data using the fluent mutation builder
        _conn.Graph()
            .EnsureNodeTable("Person", new()
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING,
                ["age"] = LogicalTypeID.INT64,
                ["city"] = LogicalTypeID.STRING,
            })
            .EnsureNodeTable("Company", new()
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING,
            })
            .EnsureRelTable("WORKS_AT", "Person", "Company", new()
            {
                ["since"] = LogicalTypeID.INT64,
            })
            .AddNode("Person", "p1", new { id = "p1", name = "Alice", age = 30L, city = "NYC" })
            .AddNode("Person", "p2", new { id = "p2", name = "Bob", age = 25L, city = "SF" })
            .AddNode("Person", "p3", new { id = "p3", name = "Carol", age = 35L, city = "NYC" })
            .AddNode("Person", "p4", new { id = "p4", name = "Dave", age = 28L, city = "LA" })
            .AddNode("Company", "c1", new { id = "c1", name = "Acme Corp" })
            .AddNode("Company", "c2", new { id = "c2", name = "Globex" })
            .AddEdge("WORKS_AT", "p1", "c1", new { since = 2020L })
            .AddEdge("WORKS_AT", "p2", "c1", new { since = 2021L })
            .AddEdge("WORKS_AT", "p3", "c2", new { since = 2019L })
            .Commit();
    }

    // ── CypherFluentQuery tests ──────────────────────────────────────────────

    [Fact]
    public void Cypher_Select_ProjectsRowsToStrings()
    {
        var names = _conn.Cypher("MATCH (p:Person) RETURN p.name ORDER BY p.name")
            .Select(row => row.GetString("p.name")!);

        Assert.Equal(4, names.Count);
        Assert.Equal("Alice", names[0]);
        Assert.Equal("Bob", names[1]);
        Assert.Equal("Carol", names[2]);
        Assert.Equal("Dave", names[3]);
    }

    [Fact]
    public void Cypher_Param_BindsSingleParameter()
    {
        var names = _conn.Cypher("MATCH (p:Person) WHERE p.city = $city RETURN p.name ORDER BY p.name")
            .Param("city", "NYC")
            .Select(row => row.GetString("p.name")!);

        Assert.Equal(2, names.Count);
        Assert.Equal("Alice", names[0]);
        Assert.Equal("Carol", names[1]);
    }

    [Fact]
    public void Cypher_Params_AnonymousObject_BindsMultipleParameters()
    {
        var names = _conn.Cypher(
                "MATCH (p:Person) WHERE p.city = $city AND p.age > $minAge RETURN p.name")
            .Params(new { city = "NYC", minAge = 31L })
            .Select(row => row.GetString("p.name")!);

        Assert.Single(names);
        Assert.Equal("Carol", names[0]);
    }

    [Fact]
    public void Cypher_Scalar_ReturnsCount()
    {
        var count = _conn.Cypher("MATCH (p:Person) RETURN COUNT(*) AS cnt")
            .Scalar<long>();

        Assert.Equal(4L, count);
    }

    [Fact]
    public void Cypher_FirstOrDefault_ReturnsSingleRow()
    {
        var row = _conn.Cypher("MATCH (p:Person {id: 'p1'}) RETURN p.name, p.age")
            .FirstOrDefault();

        Assert.NotNull(row);
        Assert.Equal("Alice", row!.GetString("p.name"));
        Assert.Equal(30L, row.GetInt64("p.age"));
    }

    [Fact]
    public void Cypher_FirstOrDefault_ReturnsNullWhenEmpty()
    {
        var row = _conn.Cypher("MATCH (p:Person {id: 'nonexistent'}) RETURN p.name")
            .FirstOrDefault();

        Assert.Null(row);
    }

    [Fact]
    public void Cypher_Count_ReturnsRowCount()
    {
        var count = _conn.Cypher("MATCH (p:Person) RETURN p.name").Count();
        Assert.Equal(4, count);
    }

    [Fact]
    public void Cypher_ForEach_IteratesAllRows()
    {
        var collected = new List<string>();

        _conn.Cypher("MATCH (p:Person) RETURN p.name ORDER BY p.name")
            .ForEach(row => collected.Add(row.GetString("p.name")!));

        Assert.Equal(4, collected.Count);
        Assert.Equal("Alice", collected[0]);
    }

    [Fact]
    public void Cypher_ToList_MaterializesAllRows()
    {
        var rows = _conn.Cypher("MATCH (p:Person) RETURN p.name ORDER BY p.name")
            .ToList();

        Assert.Equal(4, rows.Count);
        Assert.Equal("Dave", rows[3].GetString("p.name"));
    }

    [Fact]
    public void Cypher_AsEnumerable_SupportsLinqToObjects()
    {
        var over30 = _conn.Cypher("MATCH (p:Person) RETURN p.name, p.age")
            .AsEnumerable()
            .Where(row => row.GetInt64("p.age") > 28)
            .Select(row => row.GetString("p.name"))
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(2, over30.Count);
        Assert.Equal("Alice", over30[0]);
        Assert.Equal("Carol", over30[1]);
    }

    [Fact]
    public void Cypher_Traversal_JoinsNaturally()
    {
        var results = _conn.Cypher(
                "MATCH (p:Person)-[:WORKS_AT]->(c:Company) RETURN p.name, c.name AS company ORDER BY p.name")
            .Select(row => (row.GetString("p.name")!, row.GetString("company")!));

        Assert.Equal(3, results.Count);
        Assert.Equal(("Alice", "Acme Corp"), results[0]);
        Assert.Equal(("Bob", "Acme Corp"), results[1]);
        Assert.Equal(("Carol", "Globex"), results[2]);
    }

    // ── GraphMutationBuilder tests ───────────────────────────────────────────

    [Fact]
    public void Graph_AddNodeAndEdge_CommitsAtomically()
    {
        _conn.Graph()
            .AddNode("Person", "p5", new { id = "p5", name = "Eve", age = 22L, city = "Chicago" })
            .AddEdge("WORKS_AT", "p5", "c1", new { since = 2024L })
            .Commit();

        var row = _conn.Cypher("MATCH (p:Person {id: 'p5'})-[:WORKS_AT]->(c:Company) RETURN p.name, c.name AS co")
            .FirstOrDefault();

        Assert.NotNull(row);
        Assert.Equal("Eve", row!.GetString("p.name"));
        Assert.Equal("Acme Corp", row.GetString("co"));
    }

    [Fact]
    public void Graph_PendingCount_TracksOperations()
    {
        var builder = _conn.Graph()
            .AddNode("Person", "p6", new { id = "p6", name = "Frank", age = 40L, city = "Boston" })
            .AddNode("Person", "p7", new { id = "p7", name = "Grace", age = 33L, city = "Austin" })
            .AddEdge("WORKS_AT", "p6", "c2");

        Assert.Equal(3, builder.PendingCount);

        builder.Commit();

        // Verify both nodes exist
        var count = _conn.Cypher("MATCH (p:Person) WHERE p.id IN ['p6','p7'] RETURN COUNT(*) AS cnt")
            .Scalar<long>();
        Assert.Equal(2L, count);
    }

    [Fact]
    public void Graph_Cypher_ExecutesMutationInTransaction()
    {
        _conn.Graph()
            .AddNode("Person", "p8", new { id = "p8", name = "Hank", age = 50L, city = "Denver" })
            .Cypher("MATCH (p:Person {id: 'p8'}) SET p.age = 51")
            .Commit();

        var age = _conn.Cypher("MATCH (p:Person {id: 'p8'}) RETURN p.age AS age")
            .Scalar<long>();
        Assert.Equal(51L, age);
    }

    [Fact]
    public void Graph_DoubleCommit_Throws()
    {
        var builder = _conn.Graph()
            .AddNode("Person", "p9", new { id = "p9", name = "Iris", age = 27L, city = "Miami" });

        builder.Commit();

        Assert.Throws<InvalidOperationException>(() => builder.Commit());
    }

    [Fact]
    public void Graph_DictionaryProperties_Work()
    {
        _conn.Graph()
            .AddNode("Person", "p10", new Dictionary<string, object>
            {
                ["id"] = "p10",
                ["name"] = "Jack",
                ["age"] = 45L,
                ["city"] = "Seattle",
            })
            .AddEdge("WORKS_AT", "p10", "c2", new Dictionary<string, object>
            {
                ["since"] = 2018L,
            })
            .Commit();

        var row = _conn.Cypher("MATCH (p:Person {id: 'p10'}) RETURN p.name, p.city").FirstOrDefault();
        Assert.NotNull(row);
        Assert.Equal("Jack", row!.GetString("p.name"));
        Assert.Equal("Seattle", row.GetString("p.city"));
    }

    // ── QueryResult IEnumerable / convenience tests ──────────────────────────

    [Fact]
    public void QueryResult_Rows_ReturnsReadOnlyList()
    {
        var result = _conn.Query("MATCH (p:Person) RETURN p.name ORDER BY p.name");
        Assert.True(result.IsSuccess);

        var rows = result.Rows;
        Assert.Equal(4, rows.Count);
        Assert.Equal("Alice", rows[0].GetString("p.name"));
    }

    [Fact]
    public void QueryResult_IEnumerable_SupportsForEachLoop()
    {
        var result = _conn.Query("MATCH (p:Person) RETURN p.name ORDER BY p.name");
        var names = new List<string>();

        foreach (var row in result)
            names.Add(row.GetString("p.name")!);

        Assert.Equal(new[] { "Alice", "Bob", "Carol", "Dave" }, names);
    }

    [Fact]
    public void QueryResult_IEnumerable_SupportsLinq()
    {
        var result = _conn.Query("MATCH (p:Person) RETURN p.name, p.age");

        var youngNames = result
            .Where(row => row.GetInt64("p.age") < 30)
            .Select(row => row.GetString("p.name"))
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(new[] { "Bob", "Dave" }, youngNames);
    }

    [Fact]
    public void QueryResult_ThrowIfFailed_ThrowsOnError()
    {
        var result = _conn.Query("INVALID CYPHER SYNTAX 123");
        Assert.False(result.IsSuccess);

        var ex = Assert.Throws<InvalidOperationException>(() => result.ThrowIfFailed());
        Assert.Contains("Query failed", ex.Message);
    }

    [Fact]
    public void QueryResult_ThrowIfFailed_ReturnsSelfOnSuccess()
    {
        var result = _conn.Query("MATCH (p:Person) RETURN p.name LIMIT 1");
        var same = result.ThrowIfFailed();
        Assert.Same(result, same);
    }

    [Fact]
    public void QueryResult_ForEach_ChainsWithOtherMethods()
    {
        var ages = new List<long>();

        var result = _conn.Query("MATCH (p:Person) RETURN p.age ORDER BY p.age");
        var returned = result.ForEach(row => ages.Add(row.GetInt64("p.age")));

        Assert.Same(result, returned); // Returns this for chaining
        Assert.Equal(new long[] { 25, 28, 30, 35 }, ages);
    }

    [Fact]
    public void QueryResult_Scalar_ReturnsFirstColumnFirstRow()
    {
        var result = _conn.Query("MATCH (p:Person) RETURN COUNT(*) AS total");
        var total = result.Scalar<long>();
        Assert.Equal(4L, total);
    }

    [Fact]
    public void QueryResult_Select_ProjectsToCustomType()
    {
        var result = _conn.Query("MATCH (p:Person) RETURN p.name, p.age ORDER BY p.name");

        var people = result.Select(row => new
        {
            Name = row.GetString("p.name"),
            Age = row.GetInt64("p.age"),
        });

        Assert.Equal(4, people.Count);
        Assert.Equal("Alice", people[0].Name);
        Assert.Equal(30L, people[0].Age);
    }

    // ── End-to-end fluent pipeline test ──────────────────────────────────────

    [Fact]
    public void EndToEnd_ReadTransformWrite_Pipeline()
    {
        // 1. Build a graph
        _conn.Graph()
            .EnsureNodeTable("Bonus", new()
            {
                ["id"] = LogicalTypeID.STRING,
                ["person_id"] = LogicalTypeID.STRING,
                ["amount"] = LogicalTypeID.DOUBLE,
            })
            .Commit();

        // 2. Read salaries (using age as a proxy here) and compute bonuses
        var bonuses = _conn.Cypher("MATCH (p:Person) RETURN p.id, p.age")
            .Select(row => new
            {
                PersonId = row.GetString("p.id")!,
                Bonus = row.GetInt64("p.age") * 100.0,
            });

        // 3. Write bonuses back
        var builder = _conn.Graph();
        foreach (var b in bonuses)
        {
            builder.AddNode("Bonus", $"bonus-{b.PersonId}", new Dictionary<string, object>
            {
                ["id"] = $"bonus-{b.PersonId}",
                ["person_id"] = b.PersonId,
                ["amount"] = b.Bonus,
            });
        }
        builder.Commit();

        // 4. Verify
        var totalBonus = _conn.Cypher("MATCH (b:Bonus) RETURN SUM(b.amount) AS total")
            .Scalar<double>();

        // Alice=30, Bob=25, Carol=35, Dave=28 → sum=118 → *100 = 11800
        Assert.Equal(11800.0, totalBonus);
    }

    public void Dispose()
    {
        _conn.Dispose();
        _db.Dispose();
    }
}
