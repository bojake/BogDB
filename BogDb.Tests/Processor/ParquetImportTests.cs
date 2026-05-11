using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BogDb.Core.Main;
using Parquet;
using Parquet.Schema;
using Xunit;

namespace BogDb.Tests.Processor;

/// <summary>
/// Integration tests for Parquet file import via COPY FROM.
/// Generates .parquet test files programmatically using Parquet.Net,
/// then verifies end-to-end import into BogDb graph tables.
/// </summary>
public sealed class ParquetImportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BogDatabase _db;
    private readonly BogConnection _conn;

    public ParquetImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bogdb_parquet_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _db = BogDatabase.CreateInMemory();
        _conn = new BogConnection(_db);
    }

    public void Dispose()
    {
        _conn.Dispose();
        _db.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { /* best-effort cleanup */ }
    }

    // ── Node import ──────────────────────────────────────────────────────────

    [Fact]
    public void CopyFrom_Parquet_ImportsNodeTable()
    {
        // Arrange: create schema
        _conn.Query("CREATE NODE TABLE Person (id INT64, name STRING, age INT64, PRIMARY KEY(id))");

        // Arrange: write a Parquet file
        var filePath = WritePersonParquet(new[]
        {
            (1L, "Alice", 30L),
            (2L, "Bob", 25L),
            (3L, "Charlie", 35L)
        });

        // Act
        var result = _conn.Query($"COPY Person FROM '{filePath}'");

        // Assert
        Assert.True(result.IsSuccess, $"COPY failed: {result.ErrorMessage}");

        var countResult = _conn.Query("MATCH (p:Person) RETURN count(p) AS cnt");
        Assert.True(countResult.HasNext());
        var row = countResult.GetNext().GetAsDictionary();
        Assert.Equal(3L, Convert.ToInt64(row["cnt"]));
    }

    [Fact]
    public void CopyFrom_Parquet_PreservesDataTypes()
    {
        _conn.Query("CREATE NODE TABLE Employee (id INT64, name STRING, salary DOUBLE, PRIMARY KEY(id))");

        var filePath = WriteEmployeeParquet(new[]
        {
            (1L, "Diana", 75000.50),
            (2L, "Eve", 82000.00)
        });

        _conn.Query($"COPY Employee FROM '{filePath}'");

        var result = _conn.Query("MATCH (e:Employee) WHERE e.name = 'Diana' RETURN e.salary AS sal");
        Assert.True(result.HasNext());
        var row = result.GetNext().GetAsDictionary();
        Assert.Equal(75000.50, Convert.ToDouble(row["sal"]), 2);
    }

    [Fact]
    public void CopyFrom_Parquet_MultipleRowGroups()
    {
        _conn.Query("CREATE NODE TABLE Item (id INT64, label STRING, PRIMARY KEY(id))");

        // Write a Parquet file with 2 row groups
        var filePath = Path.Combine(_tempDir, "items_multi_rg.parquet");
        var idField = new DataField<long>("id");
        var labelField = new DataField<string>("label");
        var schema = new ParquetSchema(idField, labelField);

        using (var fs = File.Create(filePath))
        {
            var writer = ParquetWriter.CreateAsync(schema, fs).GetAwaiter().GetResult();
            try
            {
                // Row group 1
                using (var rg1 = writer.CreateRowGroup())
                {
                    WriteColumn(rg1, idField, new long[] { 1, 2, 3 });
                    WriteColumn(rg1, labelField, new[] { "A", "B", "C" });
                }
                // Row group 2
                using (var rg2 = writer.CreateRowGroup())
                {
                    WriteColumn(rg2, idField, new long[] { 4, 5 });
                    WriteColumn(rg2, labelField, new[] { "D", "E" });
                }
            }
            finally
            {
                DisposeWriter(writer);
            }
        }

        _conn.Query($"COPY Item FROM '{ToCypherPath(filePath)}'");

        var count = _conn.Query("MATCH (i:Item) RETURN count(i) AS cnt");
        Assert.True(count.HasNext());
        Assert.Equal(5L, Convert.ToInt64(count.GetNext().GetAsDictionary()["cnt"]));
    }

    // ── Rel import ───────────────────────────────────────────────────────────

    [Fact]
    public void CopyFrom_Parquet_ImportsRelTable()
    {
        // Setup nodes first
        _conn.Query("CREATE NODE TABLE Person (id INT64, name STRING, age INT64, PRIMARY KEY(id))");
        _conn.Query("CREATE REL TABLE KNOWS (FROM Person TO Person, since INT64)");
        _conn.Query("CREATE (:Person {id: 1, name: 'Alice', age: 30})");
        _conn.Query("CREATE (:Person {id: 2, name: 'Bob', age: 25})");
        _conn.Query("CREATE (:Person {id: 3, name: 'Charlie', age: 35})");

        // Write a rel parquet file
        var filePath = WriteRelParquet(new[]
        {
            (1L, 2L, 2020L),
            (2L, 3L, 2021L)
        });

        var result = _conn.Query($"COPY KNOWS FROM '{filePath}'");
        Assert.True(result.IsSuccess, $"COPY rel failed: {result.ErrorMessage}");

        var relCount = _conn.Query("MATCH ()-[k:KNOWS]->() RETURN count(k) AS cnt");
        Assert.True(relCount.HasNext());
        Assert.Equal(2L, Convert.ToInt64(relCount.GetNext().GetAsDictionary()["cnt"]));
    }

    // ── CSV vs Parquet equivalence ───────────────────────────────────────────

    [Fact]
    public void CopyFrom_CsvAndParquet_ProduceSameResults()
    {
        // CSV import
        var csvDb = BogDatabase.CreateInMemory();
        var csvConn = new BogConnection(csvDb);
        csvConn.Query("CREATE NODE TABLE Product (id INT64, name STRING, price DOUBLE, PRIMARY KEY(id))");

        var csvPath = Path.Combine(_tempDir, "products.csv");
        File.WriteAllText(csvPath, "id,name,price\n1,Widget,9.99\n2,Gadget,19.99\n3,Doohickey,4.50\n");
        csvConn.Query($"COPY Product FROM '{ToCypherPath(csvPath)}'");

        // Parquet import
        _conn.Query("CREATE NODE TABLE Product (id INT64, name STRING, price DOUBLE, PRIMARY KEY(id))");
        var parquetPath = WriteProductParquet(new[]
        {
            (1L, "Widget", 9.99),
            (2L, "Gadget", 19.99),
            (3L, "Doohickey", 4.50)
        });
        _conn.Query($"COPY Product FROM '{parquetPath}'");

        // Compare counts
        var csvCount = csvConn.Query("MATCH (p:Product) RETURN count(p) AS cnt");
        var pqCount = _conn.Query("MATCH (p:Product) RETURN count(p) AS cnt");

        Assert.Equal(
            Convert.ToInt64(csvCount.GetNext().GetAsDictionary()["cnt"]),
            Convert.ToInt64(pqCount.GetNext().GetAsDictionary()["cnt"]));

        // Compare specific values
        var csvWidget = csvConn.Query("MATCH (p:Product {id: 1}) RETURN p.price AS price");
        var pqWidget = _conn.Query("MATCH (p:Product {id: 1}) RETURN p.price AS price");

        Assert.Equal(
            Convert.ToDouble(csvWidget.GetNext().GetAsDictionary()["price"]),
            Convert.ToDouble(pqWidget.GetNext().GetAsDictionary()["price"]),
            2);

        csvConn.Dispose();
        csvDb.Dispose();
    }

    // ── Error handling ───────────────────────────────────────────────────────

    [Fact]
    public void CopyFrom_Parquet_NonexistentFile_Fails()
    {
        _conn.Query("CREATE NODE TABLE Missing (id INT64, PRIMARY KEY(id))");

        var result = _conn.Query("COPY Missing FROM '/nonexistent/path.parquet'");
        Assert.False(result.IsSuccess);
    }

    // ── Extension detection ──────────────────────────────────────────────────

    [Fact]
    public void IsParquetFile_DetectsExtensions()
    {
        Assert.True(BogDb.Core.Processor.Operator.Persistent.CopyNode.IsParquetFile("data.parquet"));
        Assert.True(BogDb.Core.Processor.Operator.Persistent.CopyNode.IsParquetFile("data.PARQUET"));
        Assert.True(BogDb.Core.Processor.Operator.Persistent.CopyNode.IsParquetFile("data.pq"));
        Assert.False(BogDb.Core.Processor.Operator.Persistent.CopyNode.IsParquetFile("data.csv"));
        Assert.False(BogDb.Core.Processor.Operator.Persistent.CopyNode.IsParquetFile("data.json"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ToCypherPath(string path) => path.Replace('\\', '/');

    private string WritePersonParquet(IReadOnlyList<(long id, string name, long age)> data)
    {
        var filePath = Path.Combine(_tempDir, $"persons_{Guid.NewGuid():N}.parquet");
        var idField = new DataField<long>("id");
        var nameField = new DataField<string>("name");
        var ageField = new DataField<long>("age");
        var schema = new ParquetSchema(idField, nameField, ageField);

        using var fs = File.Create(filePath);
        var writer = ParquetWriter.CreateAsync(schema, fs).GetAwaiter().GetResult();
        try
        {
            using var rg = writer.CreateRowGroup();

            WriteColumn(rg, idField, data.Select(d => d.id).ToArray());
            WriteColumn(rg, nameField, data.Select(d => d.name).ToArray());
            WriteColumn(rg, ageField, data.Select(d => d.age).ToArray());

            return ToCypherPath(filePath);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    private string WriteEmployeeParquet(IReadOnlyList<(long id, string name, double salary)> data)
    {
        var filePath = Path.Combine(_tempDir, $"employees_{Guid.NewGuid():N}.parquet");
        var idField = new DataField<long>("id");
        var nameField = new DataField<string>("name");
        var salaryField = new DataField<double>("salary");
        var schema = new ParquetSchema(idField, nameField, salaryField);

        using var fs = File.Create(filePath);
        var writer = ParquetWriter.CreateAsync(schema, fs).GetAwaiter().GetResult();
        try
        {
            using var rg = writer.CreateRowGroup();

            WriteColumn(rg, idField, data.Select(d => d.id).ToArray());
            WriteColumn(rg, nameField, data.Select(d => d.name).ToArray());
            WriteColumn(rg, salaryField, data.Select(d => d.salary).ToArray());

            return ToCypherPath(filePath);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    private string WriteProductParquet(IReadOnlyList<(long id, string name, double price)> data)
    {
        var filePath = Path.Combine(_tempDir, $"products_{Guid.NewGuid():N}.parquet");
        var idField = new DataField<long>("id");
        var nameField = new DataField<string>("name");
        var priceField = new DataField<double>("price");
        var schema = new ParquetSchema(idField, nameField, priceField);

        using var fs = File.Create(filePath);
        var writer = ParquetWriter.CreateAsync(schema, fs).GetAwaiter().GetResult();
        try
        {
            using var rg = writer.CreateRowGroup();

            WriteColumn(rg, idField, data.Select(d => d.id).ToArray());
            WriteColumn(rg, nameField, data.Select(d => d.name).ToArray());
            WriteColumn(rg, priceField, data.Select(d => d.price).ToArray());

            return ToCypherPath(filePath);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    private string WriteRelParquet(IReadOnlyList<(long fromId, long toId, long since)> data)
    {
        var filePath = Path.Combine(_tempDir, $"rels_{Guid.NewGuid():N}.parquet");
        var fromIdField = new DataField<long>("from_id");
        var toIdField = new DataField<long>("to_id");
        var sinceField = new DataField<long>("since");
        var schema = new ParquetSchema(fromIdField, toIdField, sinceField);

        using var fs = File.Create(filePath);
        var writer = ParquetWriter.CreateAsync(schema, fs).GetAwaiter().GetResult();
        try
        {
            using var rg = writer.CreateRowGroup();

            WriteColumn(rg, fromIdField, data.Select(d => d.fromId).ToArray());
            WriteColumn(rg, toIdField, data.Select(d => d.toId).ToArray());
            WriteColumn(rg, sinceField, data.Select(d => d.since).ToArray());

            return ToCypherPath(filePath);
        }
        finally
        {
            DisposeWriter(writer);
        }
    }

    private static void WriteColumn<T>(ParquetRowGroupWriter rowGroupWriter, DataField field, T[] values)
        where T : struct
    {
        rowGroupWriter.WriteAsync<T>(field, values.AsMemory(), null, null, default)
            .GetAwaiter().GetResult();
    }

    private static void WriteColumn(ParquetRowGroupWriter rowGroupWriter, DataField field, string[] values)
    {
        rowGroupWriter.WriteAsync(field, values, null)
            .GetAwaiter().GetResult();
    }

    private static void DisposeWriter(ParquetWriter writer)
    {
        writer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
