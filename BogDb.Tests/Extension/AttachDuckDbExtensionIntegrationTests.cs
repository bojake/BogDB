using System;
using System.IO;
using DuckDB.NET.Data;
using BogDb.Core.Main;
using BogDb.Extensions.DuckDB;
using Xunit;

namespace BogDb.Tests.Extension;

public sealed class AttachDuckDbExtensionIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _queryPath;

    public AttachDuckDbExtensionIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bogdb_attach_duckdb_{Guid.NewGuid():N}.duckdb");
        _queryPath = _dbPath.Replace("\\", "\\\\");
        using var conn = new DuckDBConnection($"DataSource={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE artists (id BIGINT, name VARCHAR, country VARCHAR);
            INSERT INTO artists VALUES (1, 'AC/DC', 'Australia');
            INSERT INTO artists VALUES (2, 'Miles Davis', 'USA');
            INSERT INTO artists VALUES (3, 'Beethoven', 'Germany');
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void Attach_DuckDb_RegistersAttachedDatabaseAndTables()
    {
        using var db = BogDatabase.Open(":memory:");
        new DuckDBExtension().Load(db);
        using var conn = new BogConnection(db);

        var result = conn.Query($"ATTACH '{_queryPath}' AS music (dbtype duckdb)");

        Assert.True(result.IsSuccess, $"ATTACH failed: {result.ErrorMessage}");
        Assert.True(db.TryGetAttachedDatabase("music", out var attached));
        Assert.Equal("duckdb", attached.DbType);
        Assert.True(attached.Tables.ContainsKey("artists"));
        Assert.True(db.StandaloneTableFunctionRegistry.Contains("music.artists"));
    }

    [Fact]
    public void Attach_DuckDb_WithoutExtension_ReturnsHelpfulError()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query($"ATTACH '{_queryPath}' AS music (dbtype duckdb)");

        Assert.False(result.IsSuccess);
        Assert.Contains("No loaded extension can handle database type: duckdb", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFrom_AttachedDuckDbAlias_ReturnsRows()
    {
        using var db = BogDatabase.Open(":memory:");
        new DuckDBExtension().Load(db);
        using var conn = new BogConnection(db);

        var attach = conn.Query($"ATTACH '{_queryPath}' AS music (dbtype duckdb)");
        Assert.True(attach.IsSuccess, $"ATTACH failed: {attach.ErrorMessage}");

        var result = conn.Query("LOAD FROM 'attached:music|artists' RETURN *");

        Assert.True(result.IsSuccess, $"Attached LOAD FROM failed: {result.ErrorMessage}");
        Assert.Equal(3UL, result.GetNumTuples());
    }

    [Fact]
    public void Call_AttachedDuckDbTableFunction_ReturnsRows()
    {
        using var db = BogDatabase.Open(":memory:");
        new DuckDBExtension().Load(db);
        using var conn = new BogConnection(db);

        var attach = conn.Query($"ATTACH '{_queryPath}' AS music (dbtype duckdb)");
        Assert.True(attach.IsSuccess, $"ATTACH failed: {attach.ErrorMessage}");

        var result = conn.Query("CALL `music.artists`() RETURN *");

        Assert.True(result.IsSuccess, $"Attached CALL failed: {result.ErrorMessage}");
        Assert.Equal(3UL, result.GetNumTuples());
    }
}
