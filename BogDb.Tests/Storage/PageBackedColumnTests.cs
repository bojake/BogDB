using System;
using System.IO;
using System.Linq;
using BogDb.Core.Storage;
using BogDb.Core.Storage.BufferManager;
using BogDb.Core.Storage.Table;
using Xunit;

namespace BogDb.Tests.Storage;

/// <summary>
/// Tests for PageBackedColumn — page-level columnar storage using FileHandle.
/// </summary>
public sealed class PageBackedColumnTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BogDb.Core.Storage.BufferManager.BufferManager _bm;

    public PageBackedColumnTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bogdb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _bm = new BogDb.Core.Storage.BufferManager.BufferManager(
            bufferPoolSize: 16 * 1024 * 1024, // 16MB
            maxDbSize: 64 * 1024 * 1024);
    }

    public void Dispose()
    {
        _bm.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private FileHandle CreateFileHandle(string name, uint fileIndex)
    {
        var path = Path.Combine(_tempDir, name);
        return _bm.GetFileHandle(path, 0x00, fileIndex); // flags=0 → file-backed mode
    }

    private FileHandle CreateInMemoryFileHandle(uint fileIndex)
    {
        var path = Path.Combine(_tempDir, $"mem_{fileIndex}");
        return _bm.GetFileHandle(path, 0x02, fileIndex); // flags=0x02 → in-memory mode
    }

    // ─── Int64 ─────────────────────────────────────────────────────────

    [Fact]
    public void Int64_AppendAndLookup()
    {
        using var fh = CreateInMemoryFileHandle(1);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);

        col.Append(42L);
        col.Append(100L);
        col.Append(-7L);

        Assert.Equal(3, col.Count);
        Assert.Equal(42L, col.Lookup(0));
        Assert.Equal(100L, col.Lookup(1));
        Assert.Equal(-7L, col.Lookup(2));
    }

    [Fact]
    public void Int64_NullValues()
    {
        using var fh = CreateInMemoryFileHandle(2);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);

        col.Append(1L);
        col.Append(null);
        col.Append(3L);

        Assert.Equal(1L, col.Lookup(0));
        Assert.Null(col.Lookup(1));
        Assert.Equal(3L, col.Lookup(2));
    }

    [Fact]
    public void Int64_Update()
    {
        using var fh = CreateInMemoryFileHandle(3);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);

        col.Append(10L);
        col.Append(20L);

        col.Update(0, 99L);
        col.Update(1, null);

        Assert.Equal(99L, col.Lookup(0));
        Assert.Null(col.Lookup(1));
    }

    [Fact]
    public void Int64_MultiPage()
    {
        // 256 slots per page → 300 values spans 2 data pages
        using var fh = CreateInMemoryFileHandle(4);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);

        for (int i = 0; i < 300; i++)
            col.Append((long)i);

        Assert.Equal(300, col.Count);
        Assert.Equal(0L, col.Lookup(0));
        Assert.Equal(255L, col.Lookup(255));
        Assert.Equal(256L, col.Lookup(256));
        Assert.Equal(299L, col.Lookup(299));
    }

    [Fact]
    public void Int64_Scan()
    {
        using var fh = CreateInMemoryFileHandle(5);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);

        for (int i = 0; i < 10; i++)
            col.Append((long)(i * 10));

        var scanned = col.Scan(3, 4).ToList();
        Assert.Equal(4, scanned.Count);
        Assert.Equal(30L, scanned[0]);
        Assert.Equal(40L, scanned[1]);
        Assert.Equal(50L, scanned[2]);
        Assert.Equal(60L, scanned[3]);
    }

    // ─── Double ────────────────────────────────────────────────────────

    [Fact]
    public void Double_AppendAndLookup()
    {
        using var fh = CreateInMemoryFileHandle(10);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Double);

        col.Append(3.14);
        col.Append(-0.001);
        col.Append(null);

        Assert.Equal(3.14, col.Lookup(0));
        Assert.Equal(-0.001, col.Lookup(1));
        Assert.Null(col.Lookup(2));
    }

    // ─── Int32 ─────────────────────────────────────────────────────────

    [Fact]
    public void Int32_AppendAndLookup()
    {
        using var fh = CreateInMemoryFileHandle(20);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int32);

        col.Append(42);
        col.Append(-1);

        Assert.Equal(42, col.Lookup(0));
        Assert.Equal(-1, col.Lookup(1));
    }

    // ─── Bool ──────────────────────────────────────────────────────────

    [Fact]
    public void Bool_AppendAndLookup()
    {
        using var fh = CreateInMemoryFileHandle(30);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Bool);

        col.Append(true);
        col.Append(false);
        col.Append(null);

        Assert.Equal(true, col.Lookup(0));
        Assert.Equal(false, col.Lookup(1));
        Assert.Null(col.Lookup(2));
    }

    // ─── String ────────────────────────────────────────────────────────

    [Fact]
    public void String_AppendAndLookup()
    {
        using var fh = CreateInMemoryFileHandle(40);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.String);

        col.Append("hello");
        col.Append("world");
        col.Append(null);
        col.Append("a longer string that exceeds short string length");

        Assert.Equal(4, col.Count);
        Assert.Equal("hello", col.Lookup(0));
        Assert.Equal("world", col.Lookup(1));
        Assert.Null(col.Lookup(2));
        Assert.Equal("a longer string that exceeds short string length", col.Lookup(3));
    }

    [Fact]
    public void String_Update()
    {
        using var fh = CreateInMemoryFileHandle(41);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.String);

        col.Append("original");
        Assert.Equal("original", col.Lookup(0));

        col.Update(0, "updated");
        Assert.Equal("updated", col.Lookup(0));
    }

    // ─── File-backed persistence ───────────────────────────────────────

    [Fact]
    public void FileBacked_Int64_PersistsAcrossReopen()
    {
        var path = Path.Combine(_tempDir, "persist_test.dat");

        // Write
        {
            using var fh = _bm.GetFileHandle(path, 0x00, 100);
            using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);

            col.Append(42L);
            col.Append(99L);
            col.Append(null);
            col.Flush();
        }

        // Read back
        {
            using var fh = _bm.GetFileHandle(path, 0x00, 101);
            using var col = new PageBackedColumn(fh); // open existing

            Assert.Equal(3, col.Count);
            Assert.Equal(42L, col.Lookup(0));
            Assert.Equal(99L, col.Lookup(1));
            Assert.Null(col.Lookup(2));
        }
    }

    // ─── Edge cases ────────────────────────────────────────────────────

    [Fact]
    public void OutOfBounds_Throws()
    {
        using var fh = CreateInMemoryFileHandle(50);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);

        col.Append(1L);

        Assert.Throws<ArgumentOutOfRangeException>(() => col.Lookup(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => col.Lookup(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => col.Update(1, 2L));
    }

    [Fact]
    public void LargeDataset_1000Values()
    {
        using var fh = CreateInMemoryFileHandle(60);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);

        for (int i = 0; i < 1000; i++)
            col.Append((long)i);

        Assert.Equal(1000, col.Count);

        // Spot checks across page boundaries
        Assert.Equal(0L, col.Lookup(0));
        Assert.Equal(255L, col.Lookup(255));
        Assert.Equal(256L, col.Lookup(256));
        Assert.Equal(511L, col.Lookup(511));
        Assert.Equal(512L, col.Lookup(512));
        Assert.Equal(999L, col.Lookup(999));
    }

    [Fact]
    public void ReadAfterFlush_WithoutReopen()
    {
        // Regression: DiskArray.CheckpointInMemoryIfNecessary must promote
        // _headerForReadTrx after clearing _hasTransactionalUpdates,
        // otherwise Get() rejects valid indices post-flush.
        using var fh = CreateInMemoryFileHandle(70);
        using var col = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Int64);

        col.Append(10L);
        col.Append(20L);
        col.Append(30L);

        // Flush checkpoints DiskArray state
        col.Flush();

        // These reads must still work after flush
        Assert.Equal(3, col.Count);
        Assert.Equal(10L, col.Lookup(0));
        Assert.Equal(20L, col.Lookup(1));
        Assert.Equal(30L, col.Lookup(2));

        // Appending after flush must also work
        col.Append(40L);
        Assert.Equal(4, col.Count);
        Assert.Equal(40L, col.Lookup(3));
    }
    // ─── Overflow persistence ──────────────────────────────────────────

    [Fact]
    public void StringOverflow_SurvivesFlushAndReopen()
    {
        var fh1 = CreateInMemoryFileHandle(80);
        var col1 = new PageBackedColumn(fh1, PageBackedColumn.ColumnTypeTag.String);

        col1.Append("hello");
        col1.Append("world");
        col1.Append(null);
        col1.Append("bogdb-ng");

        col1.Flush();

        // Reopen from same FileHandle
        var col2 = new PageBackedColumn(fh1);

        Assert.Equal(4, col2.Count);
        Assert.Equal("hello", col2.Lookup(0));
        Assert.Equal("world", col2.Lookup(1));
        Assert.Null(col2.Lookup(2));
        Assert.Equal("bogdb-ng", col2.Lookup(3));
    }

    [Fact]
    public void DynamicOverflow_ArraysAndComplexTypes_SurviveReopen()
    {
        var fh = CreateInMemoryFileHandle(81);
        var col1 = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.Dynamic);

        // Write heterogeneous values
        col1.Append(42L);                                           // scalar long
        col1.Append("test string");                                 // string overflow
        col1.Append(new System.Collections.Generic.List<object?> { 1L, 2L, 3L }); // list
        col1.Append(3.14);                                          // scalar double
        col1.Append(null);                                           // null
        col1.Append(new System.Collections.Generic.List<object?> { 1.0f, 2.0f }); // float list

        col1.Flush();

        var col2 = new PageBackedColumn(fh);
        Assert.Equal(6, col2.Count);

        Assert.Equal(42L, col2.Lookup(0));
        Assert.Equal("test string", col2.Lookup(1));

        var list1 = col2.Lookup(2) as System.Collections.Generic.List<object?>;
        Assert.NotNull(list1);
        Assert.Equal(3, list1!.Count);
        Assert.Equal(1L, list1[0]);
        Assert.Equal(2L, list1[1]);
        Assert.Equal(3L, list1[2]);

        Assert.Equal(3.14, col2.Lookup(3));
        Assert.Null(col2.Lookup(4));

        var list2 = col2.Lookup(5) as System.Collections.Generic.List<object?>;
        Assert.NotNull(list2);
        Assert.Equal(2, list2!.Count);
    }

    [Fact]
    public void OverflowPersistence_ManyValues_SpansMultiplePages()
    {
        var fh = CreateInMemoryFileHandle(82);
        var col1 = new PageBackedColumn(fh, PageBackedColumn.ColumnTypeTag.String);

        // Write enough strings to span multiple 4KB overflow pages
        var values = Enumerable.Range(0, 500).Select(i => $"value_{i}_padding_to_make_it_longer_{new string('x', 50)}").ToArray();
        foreach (var v in values)
            col1.Append(v);

        col1.Flush();

        var col2 = new PageBackedColumn(fh);
        Assert.Equal(500, col2.Count);

        for (int i = 0; i < 500; i++)
            Assert.Equal(values[i], col2.Lookup(i));
    }
}
