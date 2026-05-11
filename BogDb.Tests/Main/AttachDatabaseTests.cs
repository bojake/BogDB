using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Extension;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

/// <summary>
/// Lightweight mock storage extension that returns canned rows for ATTACH / LOAD FROM testing.
/// </summary>
internal sealed class MockStorageExtension : IStorageExtension
{
    public string Name => "mock";

    private readonly Dictionary<string, List<(string Name, string Type)>> _tables;
    private readonly Dictionary<string, List<Dictionary<string, object?>>> _rows;

    public MockStorageExtension(
        Dictionary<string, List<(string Name, string Type)>>? tables = null,
        Dictionary<string, List<Dictionary<string, object?>>>? rows = null)
    {
        _tables = tables ?? new Dictionary<string, List<(string Name, string Type)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["users"] = new()
            {
                ("id", "INT64"),
                ("name", "STRING")
            }
        };
        _rows = rows ?? new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["users"] = new()
            {
                new() { ["id"] = 1L, ["name"] = "Alice" },
                new() { ["id"] = 2L, ["name"] = "Bob" }
            }
        };
    }

    public bool CanHandle(string dbType)
        => string.Equals(dbType, "MOCK", StringComparison.OrdinalIgnoreCase);

    public AttachedDatabaseHandle Attach(
        BogDatabase database,
        string alias,
        string path,
        IReadOnlyDictionary<string, object?> options)
    {
        var tableDict = new Dictionary<string, AttachedTableInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tableName, columns) in _tables)
            tableDict[tableName] = new AttachedTableInfo(tableName, columns);

        return new AttachedDatabaseHandle(alias, "MOCK", path, Name, tableDict);
    }

    public IEnumerable<Dictionary<string, object?>> Scan(
        AttachedDatabaseHandle attachedDatabase,
        string? tableName)
    {
        if (tableName != null && _rows.TryGetValue(tableName, out var rows))
            return rows;
        return Enumerable.Empty<Dictionary<string, object?>>();
    }
}

public class AttachDatabaseTests
{
    // ── ATTACH ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Attach_Succeeds_WithRegisteredExtension()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("mock", new MockStorageExtension());

        var result = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(db.TryGetAttachedDatabase("testdb", out var handle));
        Assert.Equal("MOCK", handle.DbType);
        Assert.Equal("test.db", handle.Path);
    }

    [Fact]
    public void Attach_RegistersTableFunctions()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("mock", new MockStorageExtension());

        var attach = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");
        Assert.True(attach.IsSuccess, attach.ErrorMessage);

        // The mock extension exposes a 'users' table, so 'testdb.users' should be registered
        Assert.True(db.StandaloneTableFunctionRegistry.Contains("testdb.users"));
    }

    [Fact]
    public void Attach_ReturnsSuccessMessage()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("mock", new MockStorageExtension());

        var result = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        var row = result.GetNext();
        Assert.Equal("Attached database successfully.", row.GetString(0));
    }

    [Fact]
    public void Attach_DuplicateAlias_Rejects()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("mock", new MockStorageExtension());

        var attach1 = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");
        Assert.True(attach1.IsSuccess, attach1.ErrorMessage);

        var attach2 = conn.Query("ATTACH 'other.db' AS testdb (DBTYPE mock)");
        Assert.False(attach2.IsSuccess);
        Assert.Contains("testdb", attach2.ErrorMessage);
    }

    [Fact]
    public void Attach_UnknownDbType_Rejects()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        var result = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE nonexistent)");
        Assert.False(result.IsSuccess);
        Assert.Contains("No loaded extension can handle database type", result.ErrorMessage);
    }

    [Fact]
    public void Attach_UnknownDbType_SuggestsDuckDB()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        var result = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE duckdb)");
        Assert.False(result.IsSuccess);
        Assert.Contains("load duckdb extension", result.ErrorMessage);
    }

    [Fact]
    public void Attach_UnknownDbType_SuggestsPostgres()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        var result = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE postgres)");
        Assert.False(result.IsSuccess);
        Assert.Contains("load postgres extension", result.ErrorMessage);
    }

    // ── DETACH ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Detach_Succeeds_AfterAttach()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("mock", new MockStorageExtension());

        var attach = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");
        Assert.True(attach.IsSuccess, attach.ErrorMessage);

        var detach = conn.Query("DETACH testdb");
        Assert.True(detach.IsSuccess, detach.ErrorMessage);
        Assert.Equal(1UL, detach.GetNumTuples());
        Assert.Equal("Detached database successfully.", detach.GetNext().GetString(0));

        Assert.False(db.TryGetAttachedDatabase("testdb", out _));
    }

    [Fact]
    public void Detach_RemovesTableFunctions()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("mock", new MockStorageExtension());

        conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");
        Assert.True(db.StandaloneTableFunctionRegistry.Contains("testdb.users"));

        conn.Query("DETACH testdb");
        Assert.False(db.StandaloneTableFunctionRegistry.Contains("testdb.users"));
    }

    [Fact]
    public void Detach_UnknownAlias_Rejects()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        var result = conn.Query("DETACH nonexistent");
        Assert.False(result.IsSuccess);
        Assert.Contains("not attached", result.ErrorMessage);
    }

    [Fact]
    public void Detach_ClearsDefaultDatabaseAlias()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("mock", new MockStorageExtension());

        conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");
        conn.Query("USE testdb");
        Assert.Equal("testdb", conn.DefaultDatabaseAlias);

        conn.Query("DETACH testdb");
        Assert.Null(conn.DefaultDatabaseAlias);
    }

    // ── USE ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Use_Succeeds_AfterAttach()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("mock", new MockStorageExtension());

        conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");

        var use = conn.Query("USE testdb");
        Assert.True(use.IsSuccess, use.ErrorMessage);
        Assert.Equal(1UL, use.GetNumTuples());
        Assert.Equal("Used database successfully.", use.GetNext().GetString(0));
        Assert.Equal("testdb", conn.DefaultDatabaseAlias);
    }

    [Fact]
    public void Use_UnknownAlias_Rejects()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        var result = conn.Query("USE nonexistent");
        Assert.False(result.IsSuccess);
        Assert.Contains("not attached", result.ErrorMessage);
    }

    // ── Round-trip ─────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Attach_Use_Detach()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("mock", new MockStorageExtension());

        // ATTACH
        var attach = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");
        Assert.True(attach.IsSuccess, attach.ErrorMessage);
        Assert.True(db.TryGetAttachedDatabase("testdb", out _));

        // USE
        var use = conn.Query("USE testdb");
        Assert.True(use.IsSuccess, use.ErrorMessage);
        Assert.Equal("testdb", conn.DefaultDatabaseAlias);

        // DETACH (should also clear default)
        var detach = conn.Query("DETACH testdb");
        Assert.True(detach.IsSuccess, detach.ErrorMessage);
        Assert.False(db.TryGetAttachedDatabase("testdb", out _));
        Assert.Null(conn.DefaultDatabaseAlias);

        // Re-attach should work
        var reattach = conn.Query("ATTACH 'test.db' AS testdb (DBTYPE mock)");
        Assert.True(reattach.IsSuccess, reattach.ErrorMessage);
        Assert.True(db.TryGetAttachedDatabase("testdb", out _));
    }

    [Fact]
    public void Attach_WithOptions_PassesOptionsToExtension()
    {
        Dictionary<string, object?>? capturedOptions = null;
        var tables = new Dictionary<string, List<(string Name, string Type)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["items"] = new() { ("id", "STRING") }
        };
        var ext = new CapturingStorageExtension(tables, opts => capturedOptions = opts);

        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        db.RegisterStorageExtension("capturing", ext);

        var result = conn.Query("ATTACH 'my.db' AS mydb (DBTYPE capturing, readonly true)");
        Assert.True(result.IsSuccess, result.ErrorMessage);

        Assert.NotNull(capturedOptions);
        Assert.True(capturedOptions!.ContainsKey("readonly"));
    }
}

/// <summary>
/// Extension that captures the options dictionary for test assertions.
/// </summary>
internal sealed class CapturingStorageExtension : IStorageExtension
{
    public string Name => "capturing";

    private readonly Dictionary<string, List<(string Name, string Type)>> _tables;
    private readonly Action<Dictionary<string, object?>> _onAttach;

    public CapturingStorageExtension(
        Dictionary<string, List<(string Name, string Type)>> tables,
        Action<Dictionary<string, object?>> onAttach)
    {
        _tables = tables;
        _onAttach = onAttach;
    }

    public bool CanHandle(string dbType)
        => string.Equals(dbType, "CAPTURING", StringComparison.OrdinalIgnoreCase);

    public AttachedDatabaseHandle Attach(
        BogDatabase database,
        string alias,
        string path,
        IReadOnlyDictionary<string, object?> options)
    {
        _onAttach(new Dictionary<string, object?>(options, StringComparer.OrdinalIgnoreCase));
        var tableDict = new Dictionary<string, AttachedTableInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tableName, columns) in _tables)
            tableDict[tableName] = new AttachedTableInfo(tableName, columns);
        return new AttachedDatabaseHandle(alias, "CAPTURING", path, Name, tableDict);
    }

    public IEnumerable<Dictionary<string, object?>> Scan(
        AttachedDatabaseHandle attachedDatabase,
        string? tableName) => Enumerable.Empty<Dictionary<string, object?>>();
}
