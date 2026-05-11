using System;
using System.IO;
using System.Linq;
using DuckDB.NET.Data;
using BogDb.Core.Main;
using BogDb.Extensions.DuckDB;
using Xunit;

namespace BogDb.Tests.Extension;

public sealed class DuckDbExtensionIntegrationTests : IDisposable
{
    private readonly string _dbPath;

    public DuckDbExtensionIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bogdb_duckdb_{Guid.NewGuid():N}.duckdb");
        CreateTestDatabase(_dbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static void CreateTestDatabase(string path)
    {
        using var conn = new DuckDBConnection($"DataSource={path}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE artists (id BIGINT, name VARCHAR, country VARCHAR);
            INSERT INTO artists VALUES (1, 'AC/DC', 'Australia');
            INSERT INTO artists VALUES (2, 'Miles Davis', 'USA');
            INSERT INTO artists VALUES (3, 'Beethoven', 'Germany');

            CREATE TABLE albums (id BIGINT, title VARCHAR, artist_id BIGINT);
            INSERT INTO albums VALUES (1, 'Back in Black', 1);
            INSERT INTO albums VALUES (2, 'Kind of Blue', 2);
            """;
        cmd.ExecuteNonQuery();
    }

    private static BogDatabase CreateDbWithDuckDb()
    {
        var db = BogDatabase.Open(":memory:");
        new DuckDBExtension().Load(db);
        return db;
    }

    private static string ToCypherStringLiteral(string value) =>
        "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    [Fact]
    public void LoadExtension_RegistersScanDuckDbFunction()
    {
        using var db = BogDatabase.Open(":memory:");
        Assert.False(db.FunctionRegistry.Contains("scan_duckdb"));
        Assert.False(db.StandaloneTableFunctionRegistry.Contains("scan_duckdb"));

        new DuckDBExtension().Load(db);

        Assert.True(db.FunctionRegistry.Contains("scan_duckdb"));
        Assert.True(db.StandaloneTableFunctionRegistry.Contains("scan_duckdb"));
    }

    [Fact]
    public void LoadFrom_DuckDb_ExplicitTable_ReturnsThreeRows()
    {
        using var db = CreateDbWithDuckDb();
        using var conn = new BogConnection(db);

        var result = conn.Query($"LOAD FROM '{_dbPath}|artists' RETURN *");

        Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
        Assert.Equal(3UL, result.GetNumTuples());
    }

    [Fact]
    public void LoadFrom_DuckDb_AutoTable_ReturnsRows()
    {
        using var db = CreateDbWithDuckDb();
        using var conn = new BogConnection(db);

        var result = conn.Query($"LOAD FROM '{_dbPath}' RETURN *");

        Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
        Assert.True(result.GetNumTuples() > 0);
    }

    [Fact]
    public void LoadFrom_DuckDb_WithoutExtension_ReturnsRegistryError()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query($"LOAD FROM '{_dbPath}|artists' RETURN *");

        Assert.False(result.IsSuccess);
        Assert.Contains("scan_duckdb", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CallScanDuckDb_ViaStandaloneRegistry_ReturnsRows()
    {
        using var db = CreateDbWithDuckDb();
        using var conn = new BogConnection(db);
        var pathLiteral = ToCypherStringLiteral($"{_dbPath}|artists");

        var result = conn.Query($"CALL scan_duckdb({pathLiteral}) RETURN *");

        Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
        Assert.Equal(3UL, result.GetNumTuples());
        Assert.True(result.HasNext());
        var first = result.GetNext().GetAsDictionary();
        Assert.Equal("AC/DC", first["name"]?.ToString());
    }

    [Fact]
    public void ScanDuckDb_DirectInvoke_YieldsRows()
    {
        var fn = new ScanDuckDbTableFunction();
        var rows = fn.Invoke(new[] { (object?)$"{_dbPath}|artists" }).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal("AC/DC", rows[0]["name"]?.ToString());
    }
}
