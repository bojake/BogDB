using System;
using System.IO;
using System.Linq;
using BogDb.Core.Storage;
using BogDb.Core.Storage.Table;
using Xunit;

namespace BogDb.Tests.Storage;

/// <summary>
/// Integration tests for Column with page-backed storage.
/// Verifies that Column delegates correctly to PageBackedColumn
/// while preserving the UpdateInfo MVCC layer.
/// </summary>
public sealed class ColumnPageBackedIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BogDb.Core.Storage.BufferManager.BufferManager _bm;

    public ColumnPageBackedIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bogdb_col_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _bm = new BogDb.Core.Storage.BufferManager.BufferManager(
            bufferPoolSize: 16 * 1024 * 1024,
            maxDbSize: 64 * 1024 * 1024);
    }

    public void Dispose()
    {
        _bm.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private FileHandle CreateInMemoryFileHandle(uint fileIndex)
    {
        var path = Path.Combine(_tempDir, $"mem_{fileIndex}");
        return _bm.GetFileHandle(path, 0x02, fileIndex);
    }

    [Fact]
    public void PageBacked_Column_AppendLookup()
    {
        using var fh = CreateInMemoryFileHandle(1);
        using var pbc = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);
        var col = new Column("age", pbc);

        Assert.True(col.IsPageBacked);

        col.Append(25L);
        col.Append(30L);
        col.Append(null);

        Assert.Equal(3, col.Count);
        Assert.Equal(25L, col.Lookup(0));
        Assert.Equal(30L, col.Lookup(1));
        Assert.Null(col.Lookup(2));
    }

    [Fact]
    public void PageBacked_Column_Update()
    {
        using var fh = CreateInMemoryFileHandle(2);
        using var pbc = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Double);
        var col = new Column("salary", pbc);

        col.Append(50000.0);
        col.Append(60000.0);

        col.Update(0, 55000.0);
        Assert.Equal(55000.0, col.Lookup(0));
        Assert.Equal(60000.0, col.Lookup(1));
    }

    [Fact]
    public void PageBacked_Column_Scan()
    {
        using var fh = CreateInMemoryFileHandle(3);
        using var pbc = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);
        var col = new Column("id", pbc);

        for (int i = 0; i < 10; i++)
            col.Append((long)i);

        var scanned = col.Scan(2, 5).Cast<long>().ToList();
        Assert.Equal(5, scanned.Count);
        Assert.Equal(new long[] { 2, 3, 4, 5, 6 }, scanned.ToArray());
    }

    [Fact]
    public void PageBacked_Column_Strings()
    {
        using var fh = CreateInMemoryFileHandle(4);
        using var pbc = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.String);
        var col = new Column("name", pbc);

        col.Append("Alice");
        col.Append("Bob");
        col.Append(null);
        col.Append("Charlie");

        Assert.Equal(4, col.Count);
        Assert.Equal("Alice", col.Lookup(0));
        Assert.Equal("Bob", col.Lookup(1));
        Assert.Null(col.Lookup(2));
        Assert.Equal("Charlie", col.Lookup(3));
    }

    [Fact]
    public void InMemory_Column_Unchanged()
    {
        // Regular in-memory Column should still work exactly as before
        var col = new Column("test");
        Assert.False(col.IsPageBacked);

        col.Append(42L);
        col.Append(100L);
        col.Append(null);

        Assert.Equal(3, col.Count);
        Assert.Equal(42L, col.Lookup(0));
        Assert.Equal(100L, col.Lookup(1));
        Assert.Null(col.Lookup(2));
    }

    [Fact]
    public void PageBacked_MultiPage_300Values()
    {
        // 256 values per page → 300 values requires 2 data pages
        using var fh = CreateInMemoryFileHandle(5);
        using var pbc = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);
        var col = new Column("seq", pbc);

        for (int i = 0; i < 300; i++)
            col.Append((long)(i * 2));

        Assert.Equal(300, col.Count);
        Assert.Equal(0L, col.Lookup(0));
        Assert.Equal(510L, col.Lookup(255));
        Assert.Equal(512L, col.Lookup(256));
        Assert.Equal(598L, col.Lookup(299));
    }
}
