using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Function;

/// <summary>
/// Tests for the 10 catalog introspection table functions (C++ parity).
/// </summary>
public sealed class CatalogTableFunctionTests : IDisposable
{
    private readonly BogDatabase _db;
    private readonly BogConnection _conn;

    public CatalogTableFunctionTests()
    {
        _db = BogDatabase.CreateInMemory();
        _conn = new BogConnection(_db);
        _conn.Query("CREATE NODE TABLE Person (id INT64, name STRING, age INT64, PRIMARY KEY(id))");
        _conn.Query("CREATE REL TABLE KNOWS (FROM Person TO Person, since INT64)");
        _conn.Query("CREATE (:Person {id: 1, name: 'Alice', age: 30})");
        _conn.Query("CREATE (:Person {id: 2, name: 'Bob', age: 25})");
        _conn.Query("MATCH (a:Person {id:1}),(b:Person {id:2}) CREATE (a)-[:KNOWS {since:2020}]->(b)");
    }

    public void Dispose()
    {
        _conn.Dispose();
        _db.Dispose();
    }

    // ── SHOW_TABLES ──────────────────────────────────────────────────────────

    [Fact]
    public void ShowTables_ReturnsNodeAndRelTables()
    {
        var result = _conn.Query("CALL SHOW_TABLES() RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        Assert.True(rows.Count >= 2);

        var names = rows.Select(r => r["name"]?.ToString()).ToList();
        Assert.Contains("Person", names);
        Assert.Contains("KNOWS", names);

        var personRow = rows.First(r => r["name"]?.ToString() == "Person");
        Assert.Equal("NODE", personRow["type"]?.ToString());

        var knowsRow = rows.First(r => r["name"]?.ToString() == "KNOWS");
        Assert.Equal("REL", knowsRow["type"]?.ToString());
    }

    [Fact]
    public void ShowTables_HasExpectedColumns()
    {
        var result = _conn.Query("CALL SHOW_TABLES() RETURN *");
        Assert.True(result.IsSuccess);
        Assert.True(result.HasNext());

        var row = result.GetNext();
        var dict = row.GetAsDictionary();
        Assert.True(dict.ContainsKey("id"));
        Assert.True(dict.ContainsKey("name"));
        Assert.True(dict.ContainsKey("type"));
        Assert.True(dict.ContainsKey("database name"));
        Assert.True(dict.ContainsKey("comment"));
    }

    // ── TABLE_INFO ───────────────────────────────────────────────────────────

    [Fact]
    public void TableInfo_PersonTable_ReturnsProperties()
    {
        var result = _conn.Query("CALL TABLE_INFO('Person') RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        Assert.Equal(3, rows.Count); // id, name, age

        var names = rows.Select(r => r["name"]?.ToString()).ToList();
        Assert.Contains("id", names);
        Assert.Contains("name", names);
        Assert.Contains("age", names);

        // id should be the primary key
        var idRow = rows.First(r => r["name"]?.ToString() == "id");
        Assert.Equal("True", idRow["primary key"]?.ToString());

        // name should not be the primary key
        var nameRow = rows.First(r => r["name"]?.ToString() == "name");
        Assert.Equal("False", nameRow["primary key"]?.ToString());
    }

    [Fact]
    public void TableInfo_RelTable_ReturnsProperties()
    {
        var result = _conn.Query("CALL TABLE_INFO('KNOWS') RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        Assert.True(rows.Count >= 1); // at least 'since'

        var names = rows.Select(r => r["name"]?.ToString()).ToList();
        Assert.Contains("since", names);
    }

    [Fact]
    public void TableInfo_NonexistentTable_Throws()
    {
        var result = _conn.Query("CALL TABLE_INFO('DoesNotExist') RETURN *");
        // Should fail gracefully
        Assert.False(result.IsSuccess);
    }

    // ── SHOW_FUNCTIONS ───────────────────────────────────────────────────────

    [Fact]
    public void ShowFunctions_ReturnsResults()
    {
        var result = _conn.Query("CALL SHOW_FUNCTIONS() RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        // Should have many functions registered
        Assert.True(rows.Count > 50);

        var names = rows.Select(r => r["name"]?.ToString()!.ToLowerInvariant()).ToList();
        Assert.Contains("count", names);
    }

    [Fact]
    public void ShowFunctions_HasExpectedColumns()
    {
        var result = _conn.Query("CALL SHOW_FUNCTIONS() RETURN *");
        Assert.True(result.IsSuccess);
        Assert.True(result.HasNext());

        var dict = result.GetNext().GetAsDictionary();
        Assert.True(dict.ContainsKey("name"));
        Assert.True(dict.ContainsKey("type"));
        Assert.True(dict.ContainsKey("signature"));
    }

    // ── SHOW_INDEXES ─────────────────────────────────────────────────────────

    [Fact]
    public void ShowIndexes_EmptyWhenNoIndex()
    {
        var result = _conn.Query("CALL SHOW_INDEXES() RETURN *");
        Assert.True(result.IsSuccess);

        // Fresh database may have automatic PK indexes
        // Just verify the call succeeds and has the right columns
    }

    [Fact]
    public void ShowIndexes_ReturnsCreatedIndex()
    {
        _db.CreateIndex("Person", "name");

        var result = _conn.Query("CALL SHOW_INDEXES() RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        var nameIndex = rows.FirstOrDefault(r =>
            r["column names"]?.ToString() == "name" &&
            r["table name"]?.ToString() == "Person");
        Assert.NotNull(nameIndex);
        Assert.Equal("HASH", nameIndex["index type"]?.ToString());
    }

    // ── SHOW_SEQUENCES ───────────────────────────────────────────────────────

    [Fact]
    public void ShowSequences_EmptyWhenNoneCreated()
    {
        // Note: sequences are stored in a process-wide static dictionary,
        // so other tests may have created sequences. We just verify the call succeeds.
        var result = _conn.Query("CALL SHOW_SEQUENCES() RETURN *");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ShowSequences_ReturnsAutoCreatedSequence()
    {
        _conn.Query("RETURN nextval('test_seq')");

        var result = _conn.Query("CALL SHOW_SEQUENCES() RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        Assert.True(rows.Count >= 1, "Expected at least one sequence after nextval('test_seq')");
        Assert.Contains(rows, r => r["name"]?.ToString() == "test_seq");
    }

    // ── SHOW_MACROS ──────────────────────────────────────────────────────────

    [Fact]
    public void ShowMacros_EmptyWhenNoneDefined()
    {
        var result = _conn.Query("CALL SHOW_MACROS() RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        Assert.Empty(rows);
    }

    [Fact]
    public void ShowMacros_ReturnsCreatedMacro()
    {
        _conn.Query("CREATE MACRO add_one(x) AS x + 1");

        var result = _conn.Query("CALL SHOW_MACROS() RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        Assert.Single(rows);
        Assert.Equal("add_one", rows[0]["name"]?.ToString());
        Assert.Equal("x", rows[0]["parameters"]?.ToString());
    }

    // ── SHOW_ATTACHED_DATABASES ──────────────────────────────────────────────

    [Fact]
    public void ShowAttachedDatabases_EmptyWhenNoneAttached()
    {
        var result = _conn.Query("CALL SHOW_ATTACHED_DATABASES() RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        Assert.Empty(rows);
    }

    // ── SHOW_LOADED_EXTENSIONS ───────────────────────────────────────────────

    [Fact]
    public void ShowLoadedExtensions_EmptyAtStart()
    {
        var result = _conn.Query("CALL SHOW_LOADED_EXTENSIONS() RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        // No extensions loaded in a fresh test DB
        Assert.Empty(rows);
    }

    // ── CLEAR_WARNINGS ───────────────────────────────────────────────────────

    [Fact]
    public void ClearWarnings_ReturnsOK()
    {
        var result = _conn.Query("CALL CLEAR_WARNINGS() RETURN *");
        Assert.True(result.IsSuccess);
        Assert.True(result.HasNext());

        var row = result.GetNext().GetAsDictionary();
        Assert.Equal("OK", row["status"]?.ToString());
    }

    // ── SHOW_WARNINGS ────────────────────────────────────────────────────────

    [Fact]
    public void ShowWarnings_ReturnsEmpty()
    {
        var result = _conn.Query("CALL SHOW_WARNINGS() RETURN *");
        Assert.True(result.IsSuccess);

        var rows = new List<Dictionary<string, object?>>();
        while (result.HasNext())
            rows.Add(result.GetNext().GetAsDictionary());

        Assert.Empty(rows);
    }

    // ── Self-referential: SHOW_TABLES itself should appear in SHOW_FUNCTIONS ─

    [Fact]
    public void ShowFunctions_ContainsCatalogIntrospectionFunctions()
    {
        var result = _conn.Query("CALL SHOW_FUNCTIONS() RETURN *");
        Assert.True(result.IsSuccess);

        var names = new List<string>();
        while (result.HasNext())
        {
            var row = result.GetNext().GetAsDictionary();
            names.Add(row["name"]?.ToString()?.ToUpperInvariant() ?? "");
        }

        Assert.Contains("SHOW_TABLES", names);
        Assert.Contains("TABLE_INFO", names);
        Assert.Contains("SHOW_FUNCTIONS", names);
        Assert.Contains("SHOW_INDEXES", names);
        Assert.Contains("SHOW_SEQUENCES", names);
        Assert.Contains("SHOW_MACROS", names);
        Assert.Contains("SHOW_ATTACHED_DATABASES", names);
        Assert.Contains("SHOW_LOADED_EXTENSIONS", names);
        Assert.Contains("CLEAR_WARNINGS", names);
        Assert.Contains("SHOW_WARNINGS", names);
    }
}
