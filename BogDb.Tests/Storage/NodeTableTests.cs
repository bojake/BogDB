using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.Storage;
using BogDb.Core.Storage.BufferManager;
using BogDb.Core.Storage.Table;

namespace BogDb.Tests.Storage;

public class NodeTableTests : IDisposable
{
    private readonly string _testPath;

    public NodeTableTests()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"node_table_test_{Guid.NewGuid()}.db");
    }

    [Fact]
    public unsafe void NodeTable_ReadsColumnarIntChunkSuccessfully()
    {
        long poolSize = 3 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);

        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        // Pre-fill a Memory Mapped Frame as if it were a Data Chunk dumped dynamically
        // Say, Page 0
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        int* intArr = (int*)p0;
        for (int i = 0; i < 50; i++)
        {
            intArr[i] = (i + 1) * 10; // Values: 10, 20, 30 ... 500
        }
        
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        // Mock NodeTable reading the sequence using ColumnChunkData
        var columnChunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.INT32);
        var table = new NodeTable(1, new[] { columnChunk });

        using var valueVector = new ValueVector(LogicalTypeID.INT32, capacity: 50);

        // Act: Read 50 node elements starting at relative page offset 0
        table.Read(BogDb.Core.Transaction.Transaction.DUMMY_TRANSACTION, new[] { valueVector }, 0, 50, bm);

        // Assert: Ensure execution value vector holds matching pointers implicitly via references
        for (uint i = 0; i < 50; i++)
        {
            ref int value = ref valueVector.GetValue<int>(i);
            Assert.Equal((int)(i + 1) * 10, value);
        }
    }

    [Fact]
    public unsafe void NodeTable_FileBackedComplexTypeRead_FallsBackToNulls()
    {
        long poolSize = 3 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        // Fill page with bytes; complex decode is unsupported and should null-fill safely.
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        for (int i = 0; i < 128; i++)
            p0[i] = (byte)(i % 255);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var columnChunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.LIST);
        var table = new NodeTable(1, new[] { columnChunk });
        using var valueVector = new ValueVector(LogicalTypeID.LIST, capacity: 8);

        table.Read(BogDb.Core.Transaction.Transaction.DUMMY_TRANSACTION, new[] { valueVector }, 0, 3, bm);

        Assert.True(valueVector.IsNull(0));
        Assert.True(valueVector.IsNull(1));
        Assert.True(valueVector.IsNull(2));
    }

    [Fact]
    public unsafe void NodeTable_FileBackedStringRead_DecodesShortAndOverflowStrings_AndNullsInvalidOverflow()
    {
        long poolSize = 3 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteInlineKuString(p0 + 0, "abc");
        WriteOverflowKuString(p0 + 16, fileHandle.GetPageSize() + 120, "a very long string!");
        WriteOverflowKuString(p0 + 32, fileHandle.GetPageSize() * 99, "invalid because out of file range");

        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        WriteBytes(p1 + 120, "a very long string!");
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var columnChunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        var table = new NodeTable(1, new[] { columnChunk });
        using var valueVector = new ValueVector(LogicalTypeID.STRING, capacity: 8);

        table.Read(BogDb.Core.Transaction.Transaction.DUMMY_TRANSACTION, new[] { valueVector }, 0, 3, bm);

        Assert.Equal("abc", valueVector.GetValue<KuString>(0).GetAsString());
        Assert.Equal("a very long string!", valueVector.GetValue<KuString>(1).GetAsString());
        Assert.True(valueVector.IsNull(2));
    }

    [Fact]
    public unsafe void NodeTable_FileBackedStringRead_DecodesOverflowAcrossPageBoundary()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var payload = new string('x', 32);
        var offset = fileHandle.GetPageSize() - 10; // Starts near end of page 0, continues into page 1.

        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteOverflowKuString(p0 + 0, offset, payload);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var pData0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteBytes(pData0 + (int)(fileHandle.GetPageSize() - 10), payload[..10]);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var pData1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        WriteBytes(pData1, payload[10..]);
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        Assert.Equal(payload, vector.GetValue<KuString>(0).GetAsString());
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_DecodesInlinePayload()
    {
        long poolSize = 3 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteInlineKuString(p0 + 0, "bin");
        WriteInlineKuString(p0 + 16, "data");
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);

        chunk.Scan(bm, vector, offset: 0, length: 2, posInOutputVector: 0);

        Assert.Equal("bin", vector.GetValue<KuString>(0).GetAsString());
        Assert.Equal("data", vector.GetValue<KuString>(1).GetAsString());
        Assert.False(vector.IsNull(0));
        Assert.False(vector.IsNull(1));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_PreservesNonUtf8Bytes()
    {
        long poolSize = 3 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var payload = new byte[] { 0xFF, 0x00, 0x41, 0x80 };
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteInlineKuBytes(p0 + 0, payload);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var ku = vector.GetValue<KuString>(0);
        var actual = ExtractKuBytes(ku);
        Assert.Equal(payload, actual);
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStringRead_DecodesDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildDictionaryPayload("zero", "one", "two");
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        Assert.Equal("one", vector.GetValue<KuString>(0).GetAsString());
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_DecodesDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildDictionaryPayload(new byte[] { 0x41 }, new byte[] { 0x00, 0xFF, 0x10 });
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var actual = ExtractKuBytes(vector.GetValue<KuString>(0));
        Assert.Equal(new byte[] { 0x00, 0xFF, 0x10 }, actual);
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStringRead_DecodesTypedDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 0);
        WriteDictionaryRefKuString(p0 + 16, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildTypedDictionaryPayload(null, "typed");
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 2, posInOutputVector: 0);

        Assert.True(vector.IsNull(0));
        Assert.Equal("typed", vector.GetValue<KuString>(1).GetAsString());
        Assert.False(vector.IsNull(1));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_DecodesTypedDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildTypedDictionaryPayload("text", new byte[] { 0xAB, 0x00, 0xFE });
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var actual = ExtractKuBytes(vector.GetValue<KuString>(0));
        Assert.Equal(new byte[] { 0xAB, 0x00, 0xFE }, actual);
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStringRead_DecodesOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 2);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildOffsetTableDictionaryPayload("aa", "bbb", "cccc");
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        Assert.Equal("cccc", vector.GetValue<KuString>(0).GetAsString());
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_DecodesOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildOffsetTableDictionaryPayload(
            new byte[] { 0x01 },
            new byte[] { 0x00, 0x02, 0xFF });
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var actual = ExtractKuBytes(vector.GetValue<KuString>(0));
        Assert.Equal(new byte[] { 0x00, 0x02, 0xFF }, actual);
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStringRead_DecodesTypedOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 0);
        WriteDictionaryRefKuString(p0 + 16, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildTypedOffsetTableDictionaryPayload(null, "ot-typed");
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 2, posInOutputVector: 0);

        Assert.True(vector.IsNull(0));
        Assert.Equal("ot-typed", vector.GetValue<KuString>(1).GetAsString());
        Assert.False(vector.IsNull(1));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_DecodesTypedOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildTypedOffsetTableDictionaryPayload("x", new byte[] { 0x10, 0x20, 0xFF });
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var actual = ExtractKuBytes(vector.GetValue<KuString>(0));
        Assert.Equal(new byte[] { 0x10, 0x20, 0xFF }, actual);
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStringRead_DecodesCompactTypedOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 0);
        WriteDictionaryRefKuString(p0 + 16, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildCompactTypedOffsetTableDictionaryPayload(null, "compact");
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 2, posInOutputVector: 0);

        Assert.True(vector.IsNull(0));
        Assert.Equal("compact", vector.GetValue<KuString>(1).GetAsString());
        Assert.False(vector.IsNull(1));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_DecodesCompactTypedOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildCompactTypedOffsetTableDictionaryPayload("x", new byte[] { 0xCC, 0x00, 0xAA });
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var actual = ExtractKuBytes(vector.GetValue<KuString>(0));
        Assert.Equal(new byte[] { 0xCC, 0x00, 0xAA }, actual);
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStringRead_DecodesCompressedOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildCompressedOffsetTableDictionaryPayload("left", "right");
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        Assert.Equal("right", vector.GetValue<KuString>(0).GetAsString());
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_DecodesCompressedCompactTypedOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildCompressedCompactTypedOffsetTableDictionaryPayload(
            "skip",
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var actual = ExtractKuBytes(vector.GetValue<KuString>(0));
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, actual);
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStringRead_DecodesCompressedDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildCompressedDictionaryPayload("a", "bcd");
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        Assert.Equal("bcd", vector.GetValue<KuString>(0).GetAsString());
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_DecodesCompressedTypedDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildCompressedTypedDictionaryPayload("x", new byte[] { 0x01, 0xFE });
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var actual = ExtractKuBytes(vector.GetValue<KuString>(0));
        Assert.Equal(new byte[] { 0x01, 0xFE }, actual);
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStringRead_DecodesCompressedContainerOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildCompressedContainerOffsetTableDictionaryPayload("x", "yz");
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        Assert.Equal("yz", vector.GetValue<KuString>(0).GetAsString());
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedBlobRead_DecodesCompressedContainerTypedOffsetTableDictionaryReferences()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var dictOffset = pageSize + 0UL;
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteDictionaryRefKuString(p0 + 0, dictionaryOffset: dictOffset, dictionaryIndex: 1);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var payload = BuildCompressedContainerTypedOffsetTableDictionaryPayload(
            "s",
            new byte[] { 0xAA, 0xBB });
        for (var i = 0; i < payload.Length; i++)
            p1[i] = payload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.BLOB, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var actual = ExtractKuBytes(vector.GetValue<KuString>(0));
        Assert.Equal(new byte[] { 0xAA, 0xBB }, actual);
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStringRead_DecodesHeaderAcrossPageBoundary()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var entryIndex = (uint)(pageSize / 16) - 1; // starts at page end-16, spans into next page
        var entryOffset = (ulong)entryIndex * 16;
        var record = BuildInlineKuRecord("cross");
        WriteBytesAtFileOffset(fileHandle, bm, entryOffset, record);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 4);
        chunk.Scan(bm, vector, offset: entryIndex, length: 1, posInOutputVector: 0);

        Assert.Equal("cross", vector.GetValue<KuString>(0).GetAsString());
        Assert.False(vector.IsNull(0));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedListRead_DecodesInt64Entries()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteListEntry(p0 + 0, pageSize + 0, 3);
        WriteListEntry(p0 + 12, pageSize + 24, 0);
        WriteListEntry(p0 + 24, pageSize * 99, 2);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        WriteInt64Array(p1 + 0, new long[] { 1, 2, 3 });
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.LIST);
        using var vector = new ValueVector(LogicalTypeID.LIST, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 3, posInOutputVector: 0);

        var list0 = Assert.IsType<List<object?>>(vector.GetAuxValue(0));
        Assert.Equal(3, list0.Count);
        Assert.Equal(1L, list0[0]);
        Assert.Equal(2L, list0[1]);
        Assert.Equal(3L, list0[2]);
        var list1 = Assert.IsType<List<object?>>(vector.GetAuxValue(1));
        Assert.Empty(list1);
        Assert.True(vector.IsNull(2));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStructRead_DecodesInt64FieldMap()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteStructEntry(p0 + 0, pageSize + 0);
        WriteStructEntry(p0 + 8, 0);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        WriteStructPayloadInt64Map(p1 + 0, new Dictionary<string, long>
        {
            ["a"] = 10,
            ["b"] = 20
        });
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRUCT);
        using var vector = new ValueVector(LogicalTypeID.STRUCT, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 2, posInOutputVector: 0);

        var map0 = Assert.IsType<Dictionary<string, object?>>(vector.GetAuxValue(0));
        Assert.Equal(10L, map0["a"]);
        Assert.Equal(20L, map0["b"]);
        Assert.False(vector.IsNull(0));
        Assert.True(vector.IsNull(1));
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedListRead_DecodesTypedEntries()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteListEntryTyped(p0 + 0, pageSize + 0, 5);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var typedPayload = BuildTaggedListPayload(new object?[] { 1L, "x", true, null, 2.5d });
        for (var i = 0; i < typedPayload.Length; i++)
            p1[i] = typedPayload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.LIST);
        using var vector = new ValueVector(LogicalTypeID.LIST, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var list = Assert.IsType<List<object?>>(vector.GetAuxValue(0));
        Assert.Equal(5, list.Count);
        Assert.Equal(1L, list[0]);
        Assert.Equal("x", list[1]);
        Assert.Equal(true, list[2]);
        Assert.Null(list[3]);
        Assert.Equal(2.5d, list[4]);
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStructRead_DecodesTypedFieldMap()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteStructEntry(p0 + 0, pageSize + 0);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var typedPayload = BuildTaggedStructPayload(new Dictionary<string, object?>
        {
            ["a"] = 10L,
            ["b"] = "s",
            ["c"] = true,
            ["d"] = null,
            ["e"] = 3.5d
        });
        for (var i = 0; i < typedPayload.Length; i++)
            p1[i] = typedPayload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRUCT);
        using var vector = new ValueVector(LogicalTypeID.STRUCT, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var map = Assert.IsType<Dictionary<string, object?>>(vector.GetAuxValue(0));
        Assert.Equal(10L, map["a"]);
        Assert.Equal("s", map["b"]);
        Assert.Equal(true, map["c"]);
        Assert.Null(map["d"]);
        Assert.Equal(3.5d, map["e"]);
    }

    [Fact]
    public unsafe void ColumnChunkData_FileBackedStructRead_DecodesRecursiveTypedPayloads()
    {
        long poolSize = 4 * (long)FileHandle.BOGDB_PAGE_SIZE;
        using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(poolSize, 10 * poolSize);
        using var fileHandle = bm.GetFileHandle(_testPath, 0, 0);

        var pageSize = fileHandle.GetPageSize();
        var p0 = bm.Pin(fileHandle, 0, PageReadPolicy.DONT_READ_PAGE);
        WriteStructEntry(p0 + 0, pageSize + 0);
        fileHandle.GetPageState(0).SetDirty();
        bm.Unpin(fileHandle, 0);

        var p1 = bm.Pin(fileHandle, 1, PageReadPolicy.DONT_READ_PAGE);
        var typedPayload = BuildTaggedStructPayload(new Dictionary<string, object?>
        {
            ["lst"] = new List<object?> { 1L, "x", new Dictionary<string, object?> { ["k"] = 7L } },
            ["obj"] = new Dictionary<string, object?> { ["f"] = true, ["n"] = null },
            ["blob"] = new byte[] { 0x00, 0xFF, 0x7A }
        });
        for (var i = 0; i < typedPayload.Length; i++)
            p1[i] = typedPayload[i];
        fileHandle.GetPageState(1).SetDirty();
        bm.Unpin(fileHandle, 1);

        var chunk = new ColumnChunkData(fileHandle, 0, PhysicalTypeID.STRUCT);
        using var vector = new ValueVector(LogicalTypeID.STRUCT, capacity: 8);
        chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);

        var map = Assert.IsType<Dictionary<string, object?>>(vector.GetAuxValue(0));
        var list = Assert.IsType<List<object?>>(map["lst"]);
        Assert.Equal(1L, list[0]);
        Assert.Equal("x", list[1]);
        var nested = Assert.IsType<Dictionary<string, object?>>(list[2]);
        Assert.Equal(7L, nested["k"]);

        var obj = Assert.IsType<Dictionary<string, object?>>(map["obj"]);
        Assert.Equal(true, obj["f"]);
        Assert.Null(obj["n"]);

        var blob = Assert.IsType<byte[]>(map["blob"]);
        Assert.Equal(new byte[] { 0x00, 0xFF, 0x7A }, blob);
    }

    private static unsafe void WriteInlineKuString(byte* dst, string value)
    {
        for (var i = 0; i < 16; i++)
            dst[i] = 0;

        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Assert.True(bytes.Length <= (int)KuString.SHORT_STR_LENGTH);
        *(uint*)dst = (uint)bytes.Length;
        for (var i = 0; i < Math.Min(bytes.Length, 4); i++)
            dst[4 + i] = bytes[i];
        for (var i = 4; i < bytes.Length; i++)
            dst[8 + (i - 4)] = bytes[i];
    }

    private static unsafe void WriteInlineKuBytes(byte* dst, byte[] bytes)
    {
        for (var i = 0; i < 16; i++)
            dst[i] = 0;
        Assert.True(bytes.Length <= (int)KuString.SHORT_STR_LENGTH);

        *(uint*)dst = (uint)bytes.Length;
        for (var i = 0; i < Math.Min(bytes.Length, 4); i++)
            dst[4 + i] = bytes[i];
        for (var i = 4; i < bytes.Length; i++)
            dst[8 + (i - 4)] = bytes[i];
    }

    private static unsafe void WriteOverflowKuString(byte* dst, ulong overflowOffset, string value)
    {
        for (var i = 0; i < 16; i++)
            dst[i] = 0;

        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Assert.True(bytes.Length > (int)KuString.SHORT_STR_LENGTH);
        *(uint*)dst = (uint)bytes.Length;
        for (var i = 0; i < 4; i++)
            dst[4 + i] = bytes[i];
        *(ulong*)(dst + 8) = overflowOffset;
    }

    private static unsafe void WriteListEntry(byte* dst, ulong payloadOffset, uint elementCount)
    {
        for (var i = 0; i < 12; i++)
            dst[i] = 0;
        *(ulong*)dst = payloadOffset;
        *(uint*)(dst + 8) = elementCount;
    }

    private static unsafe void WriteDictionaryRefKuString(byte* dst, ulong dictionaryOffset, uint dictionaryIndex)
    {
        const uint DictionaryFlag = 0x80000000;
        for (var i = 0; i < 16; i++)
            dst[i] = 0;
        *(uint*)dst = DictionaryFlag | dictionaryIndex;
        *(ulong*)(dst + 8) = dictionaryOffset;
    }

    private static unsafe void WriteListEntryTyped(byte* dst, ulong payloadOffset, uint elementCount)
    {
        const uint TypedFlag = 0x80000000;
        WriteListEntry(dst, payloadOffset, elementCount | TypedFlag);
    }

    private static unsafe void WriteStructEntry(byte* dst, ulong payloadOffset)
    {
        for (var i = 0; i < 8; i++)
            dst[i] = 0;
        *(ulong*)dst = payloadOffset;
    }

    private static unsafe void WriteInt64Array(byte* dst, long[] values)
    {
        for (var i = 0; i < values.Length; i++)
            ((long*)dst)[i] = values[i];
    }

    private static unsafe void WriteStructPayloadInt64Map(byte* dst, Dictionary<string, long> fields)
    {
        var cursor = dst;
        *(uint*)cursor = (uint)fields.Count;
        cursor += sizeof(uint);

        foreach (var (key, value) in fields)
        {
            var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
            *(uint*)cursor = (uint)keyBytes.Length;
            cursor += sizeof(uint);
            for (var i = 0; i < keyBytes.Length; i++)
                cursor[i] = keyBytes[i];
            cursor += keyBytes.Length;
            *(long*)cursor = value;
            cursor += sizeof(long);
        }
    }

    private static byte[] BuildTaggedListPayload(IEnumerable<object?> values)
    {
        var bytes = new List<byte>();
        foreach (var value in values)
            AppendTaggedValue(bytes, value);
        return bytes.ToArray();
    }

    private static byte[] BuildDictionaryPayload(params string[] values)
    {
        var rows = new byte[values.Length][];
        for (var i = 0; i < values.Length; i++)
            rows[i] = Encoding.UTF8.GetBytes(values[i]);
        return BuildDictionaryPayload(rows);
    }

    private static byte[] BuildDictionaryPayload(params byte[][] values)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((uint)values.Length));
        foreach (var value in values)
        {
            bytes.AddRange(BitConverter.GetBytes((uint)value.Length));
            bytes.AddRange(value);
        }
        return bytes.ToArray();
    }

    private static byte[] BuildTypedDictionaryPayload(params object?[] values)
    {
        const uint TypedFlag = 0x80000000;
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((uint)values.Length | TypedFlag));
        foreach (var value in values)
        {
            switch (value)
            {
                case null:
                    bytes.Add(0);
                    break;
                case string str:
                {
                    bytes.Add(2);
                    var strBytes = Encoding.UTF8.GetBytes(str);
                    bytes.AddRange(BitConverter.GetBytes((uint)strBytes.Length));
                    bytes.AddRange(strBytes);
                    break;
                }
                case byte[] blob:
                    bytes.Add(5);
                    bytes.AddRange(BitConverter.GetBytes((uint)blob.Length));
                    bytes.AddRange(blob);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported typed dictionary payload type {value.GetType().Name}.");
            }
        }

        return bytes.ToArray();
    }

    private static byte[] BuildOffsetTableDictionaryPayload(params string[] values)
    {
        var rows = new byte[values.Length][];
        for (var i = 0; i < values.Length; i++)
            rows[i] = Encoding.UTF8.GetBytes(values[i]);
        return BuildOffsetTableDictionaryPayload(rows);
    }

    private static byte[] BuildOffsetTableDictionaryPayload(params byte[][] values)
    {
        const uint OffsetTableFlag = 0x40000000;
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((uint)values.Length | OffsetTableFlag));

        ulong cursor = 0;
        for (var i = 0; i < values.Length; i++)
        {
            bytes.AddRange(BitConverter.GetBytes(cursor));
            cursor += (uint)values[i].Length;
        }

        bytes.AddRange(BitConverter.GetBytes((uint)cursor));
        foreach (var value in values)
            bytes.AddRange(value);
        return bytes.ToArray();
    }

    private static byte[] BuildTypedOffsetTableDictionaryPayload(params object?[] values)
    {
        const uint TypedFlag = 0x80000000;
        const uint OffsetTableFlag = 0x40000000;

        var encoded = new List<byte[]>(values.Length);
        foreach (var value in values)
        {
            switch (value)
            {
                case null:
                    encoded.Add(new byte[] { 0 });
                    break;
                case string str:
                {
                    var strBytes = Encoding.UTF8.GetBytes(str);
                    var entry = new List<byte>(1 + sizeof(uint) + strBytes.Length) { 2 };
                    entry.AddRange(BitConverter.GetBytes((uint)strBytes.Length));
                    entry.AddRange(strBytes);
                    encoded.Add(entry.ToArray());
                    break;
                }
                case byte[] blob:
                {
                    var entry = new List<byte>(1 + sizeof(uint) + blob.Length) { 5 };
                    entry.AddRange(BitConverter.GetBytes((uint)blob.Length));
                    entry.AddRange(blob);
                    encoded.Add(entry.ToArray());
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported typed offset-table dictionary value type {value.GetType().Name}.");
            }
        }

        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((uint)encoded.Count | TypedFlag | OffsetTableFlag));

        ulong cursor = 0;
        foreach (var entry in encoded)
        {
            bytes.AddRange(BitConverter.GetBytes(cursor));
            cursor += (uint)entry.Length;
        }

        bytes.AddRange(BitConverter.GetBytes((uint)cursor));
        foreach (var entry in encoded)
            bytes.AddRange(entry);
        return bytes.ToArray();
    }

    private static byte[] BuildCompactTypedOffsetTableDictionaryPayload(params object?[] values)
    {
        const uint TypedFlag = 0x80000000;
        const uint OffsetTableFlag = 0x40000000;
        const uint CompactFlag = 0x20000000;

        var encoded = new List<byte[]>(values.Length);
        foreach (var value in values)
        {
            switch (value)
            {
                case null:
                    encoded.Add(new byte[] { 0 });
                    break;
                case string str:
                {
                    var strBytes = Encoding.UTF8.GetBytes(str);
                    var entry = new byte[1 + strBytes.Length];
                    entry[0] = 2;
                    if (strBytes.Length > 0)
                        Array.Copy(strBytes, 0, entry, 1, strBytes.Length);
                    encoded.Add(entry);
                    break;
                }
                case byte[] blob:
                {
                    var entry = new byte[1 + blob.Length];
                    entry[0] = 5;
                    if (blob.Length > 0)
                        Array.Copy(blob, 0, entry, 1, blob.Length);
                    encoded.Add(entry);
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported compact typed offset-table dictionary value type {value.GetType().Name}.");
            }
        }

        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((uint)encoded.Count | TypedFlag | OffsetTableFlag | CompactFlag));

        ulong cursor = 0;
        foreach (var entry in encoded)
        {
            bytes.AddRange(BitConverter.GetBytes(cursor));
            cursor += (uint)entry.Length;
        }

        bytes.AddRange(BitConverter.GetBytes((uint)cursor));
        foreach (var entry in encoded)
            bytes.AddRange(entry);
        return bytes.ToArray();
    }

    private static byte[] BuildCompressedOffsetTableDictionaryPayload(params string[] values)
    {
        const uint OffsetTableFlag = 0x40000000;
        const uint CompressedFlag = 0x10000000;

        var rows = new byte[values.Length][];
        for (var i = 0; i < values.Length; i++)
            rows[i] = Encoding.UTF8.GetBytes(values[i]);

        var blobData = new List<byte>();
        var offsets = new List<ulong>(rows.Length);
        ulong cursor = 0;
        foreach (var row in rows)
        {
            offsets.Add(cursor);
            cursor += (uint)row.Length;
            blobData.AddRange(row);
        }

        var compressed = Deflate(blobData.ToArray());
        var result = new List<byte>();
        result.AddRange(BitConverter.GetBytes((uint)rows.Length | OffsetTableFlag | CompressedFlag));
        foreach (var offset in offsets)
            result.AddRange(BitConverter.GetBytes(offset));
        result.AddRange(BitConverter.GetBytes((uint)compressed.Length));
        result.AddRange(compressed);
        return result.ToArray();
    }

    private static byte[] BuildCompressedCompactTypedOffsetTableDictionaryPayload(params object?[] values)
    {
        const uint TypedFlag = 0x80000000;
        const uint OffsetTableFlag = 0x40000000;
        const uint CompactFlag = 0x20000000;
        const uint CompressedFlag = 0x10000000;

        var encoded = new List<byte[]>(values.Length);
        foreach (var value in values)
        {
            switch (value)
            {
                case null:
                    encoded.Add(new byte[] { 0 });
                    break;
                case string str:
                {
                    var strBytes = Encoding.UTF8.GetBytes(str);
                    var entry = new byte[1 + strBytes.Length];
                    entry[0] = 2;
                    if (strBytes.Length > 0)
                        Array.Copy(strBytes, 0, entry, 1, strBytes.Length);
                    encoded.Add(entry);
                    break;
                }
                case byte[] blob:
                {
                    var entry = new byte[1 + blob.Length];
                    entry[0] = 5;
                    if (blob.Length > 0)
                        Array.Copy(blob, 0, entry, 1, blob.Length);
                    encoded.Add(entry);
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported compressed compact typed offset-table value type {value.GetType().Name}.");
            }
        }

        var blobData = new List<byte>();
        var offsets = new List<ulong>(encoded.Count);
        ulong cursor = 0;
        foreach (var entry in encoded)
        {
            offsets.Add(cursor);
            cursor += (uint)entry.Length;
            blobData.AddRange(entry);
        }

        var compressed = Deflate(blobData.ToArray());
        var result = new List<byte>();
        result.AddRange(BitConverter.GetBytes((uint)encoded.Count | TypedFlag | OffsetTableFlag | CompactFlag | CompressedFlag));
        foreach (var offset in offsets)
            result.AddRange(BitConverter.GetBytes(offset));
        result.AddRange(BitConverter.GetBytes((uint)compressed.Length));
        result.AddRange(compressed);
        return result.ToArray();
    }

    private static byte[] BuildCompressedDictionaryPayload(params string[] values)
    {
        const uint CompressedFlag = 0x10000000;
        var raw = new List<byte>();
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            raw.AddRange(BitConverter.GetBytes((uint)bytes.Length));
            raw.AddRange(bytes);
        }

        var compressed = Deflate(raw.ToArray());
        var result = new List<byte>();
        result.AddRange(BitConverter.GetBytes((uint)values.Length | CompressedFlag));
        result.AddRange(BitConverter.GetBytes((uint)compressed.Length));
        result.AddRange(compressed);
        return result.ToArray();
    }

    private static byte[] BuildCompressedTypedDictionaryPayload(params object?[] values)
    {
        const uint TypedFlag = 0x80000000;
        const uint CompressedFlag = 0x10000000;
        var raw = new List<byte>();
        foreach (var value in values)
        {
            switch (value)
            {
                case null:
                    raw.Add(0);
                    break;
                case string str:
                {
                    raw.Add(2);
                    var bytes = Encoding.UTF8.GetBytes(str);
                    raw.AddRange(BitConverter.GetBytes((uint)bytes.Length));
                    raw.AddRange(bytes);
                    break;
                }
                case byte[] blob:
                    raw.Add(5);
                    raw.AddRange(BitConverter.GetBytes((uint)blob.Length));
                    raw.AddRange(blob);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported compressed typed dictionary value type {value.GetType().Name}.");
            }
        }

        var compressed = Deflate(raw.ToArray());
        var result = new List<byte>();
        result.AddRange(BitConverter.GetBytes((uint)values.Length | TypedFlag | CompressedFlag));
        result.AddRange(BitConverter.GetBytes((uint)compressed.Length));
        result.AddRange(compressed);
        return result.ToArray();
    }

    private static byte[] BuildCompressedContainerOffsetTableDictionaryPayload(params string[] values)
    {
        const uint OffsetTableFlag = 0x40000000;
        const uint CompressedFlag = 0x10000000;
        const uint CompressedContainerFlag = 0x08000000;

        var rows = new byte[values.Length][];
        for (var i = 0; i < values.Length; i++)
            rows[i] = Encoding.UTF8.GetBytes(values[i]);

        var container = new List<byte>();
        ulong cursor = 0;
        foreach (var row in rows)
        {
            container.AddRange(BitConverter.GetBytes(cursor));
            cursor += (uint)row.Length;
        }
        container.AddRange(BitConverter.GetBytes((uint)cursor));
        foreach (var row in rows)
            container.AddRange(row);

        var compressed = Deflate(container.ToArray());
        var result = new List<byte>();
        result.AddRange(BitConverter.GetBytes((uint)rows.Length | OffsetTableFlag | CompressedFlag | CompressedContainerFlag));
        result.AddRange(BitConverter.GetBytes((uint)compressed.Length));
        result.AddRange(compressed);
        return result.ToArray();
    }

    private static byte[] BuildCompressedContainerTypedOffsetTableDictionaryPayload(params object?[] values)
    {
        const uint TypedFlag = 0x80000000;
        const uint OffsetTableFlag = 0x40000000;
        const uint CompactFlag = 0x20000000;
        const uint CompressedFlag = 0x10000000;
        const uint CompressedContainerFlag = 0x08000000;

        var entries = new List<byte[]>(values.Length);
        foreach (var value in values)
        {
            switch (value)
            {
                case null:
                    entries.Add(new byte[] { 0 });
                    break;
                case string str:
                {
                    var bytes = Encoding.UTF8.GetBytes(str);
                    var entry = new byte[1 + bytes.Length];
                    entry[0] = 2;
                    if (bytes.Length > 0)
                        Array.Copy(bytes, 0, entry, 1, bytes.Length);
                    entries.Add(entry);
                    break;
                }
                case byte[] blob:
                {
                    var entry = new byte[1 + blob.Length];
                    entry[0] = 5;
                    if (blob.Length > 0)
                        Array.Copy(blob, 0, entry, 1, blob.Length);
                    entries.Add(entry);
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported compressed container typed offset-table value type {value.GetType().Name}.");
            }
        }

        var container = new List<byte>();
        ulong cursor = 0;
        foreach (var entry in entries)
        {
            container.AddRange(BitConverter.GetBytes(cursor));
            cursor += (uint)entry.Length;
        }
        container.AddRange(BitConverter.GetBytes((uint)cursor));
        foreach (var entry in entries)
            container.AddRange(entry);

        var compressed = Deflate(container.ToArray());
        var result = new List<byte>();
        result.AddRange(BitConverter.GetBytes((uint)entries.Count | TypedFlag | OffsetTableFlag | CompactFlag | CompressedFlag | CompressedContainerFlag));
        result.AddRange(BitConverter.GetBytes((uint)compressed.Length));
        result.AddRange(compressed);
        return result.ToArray();
    }

    private static byte[] Deflate(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(bytes, 0, bytes.Length);
        return output.ToArray();
    }

    private static byte[] BuildTaggedStructPayload(Dictionary<string, object?> fields)
    {
        const uint TypedFlag = 0x80000000;
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((uint)fields.Count | TypedFlag));
        foreach (var (key, value) in fields)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            bytes.AddRange(BitConverter.GetBytes((uint)keyBytes.Length));
            bytes.AddRange(keyBytes);
            AppendTaggedValue(bytes, value);
        }
        return bytes.ToArray();
    }

    private static void AppendTaggedValue(List<byte> bytes, object? value)
    {
        switch (value)
        {
            case null:
                bytes.Add(0);
                return;
            case long i64:
                bytes.Add(1);
                bytes.AddRange(BitConverter.GetBytes(i64));
                return;
            case string str:
                bytes.Add(2);
                var strBytes = Encoding.UTF8.GetBytes(str);
                bytes.AddRange(BitConverter.GetBytes((uint)strBytes.Length));
                bytes.AddRange(strBytes);
                return;
            case bool b:
                bytes.Add(3);
                bytes.Add((byte)(b ? 1 : 0));
                return;
            case double d:
                bytes.Add(4);
                bytes.AddRange(BitConverter.GetBytes(d));
                return;
            case byte[] blob:
                bytes.Add(5);
                bytes.AddRange(BitConverter.GetBytes((uint)blob.Length));
                bytes.AddRange(blob);
                return;
            case IEnumerable<object?> nestedList:
            {
                var list = nestedList is List<object?> known ? known : new List<object?>(nestedList);
                bytes.Add(6);
                bytes.AddRange(BitConverter.GetBytes((uint)list.Count));
                foreach (var item in list)
                    AppendTaggedValue(bytes, item);
                return;
            }
            case Dictionary<string, object?> nestedStruct:
                bytes.Add(7);
                bytes.AddRange(BitConverter.GetBytes((uint)nestedStruct.Count));
                foreach (var (key, nestedValue) in nestedStruct)
                {
                    var keyBytes = Encoding.UTF8.GetBytes(key);
                    bytes.AddRange(BitConverter.GetBytes((uint)keyBytes.Length));
                    bytes.AddRange(keyBytes);
                    AppendTaggedValue(bytes, nestedValue);
                }
                return;
            default:
                throw new NotSupportedException($"Unsupported tagged payload value type {value.GetType().Name}.");
        }
    }

    private static unsafe void WriteBytes(byte* dst, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        for (var i = 0; i < bytes.Length; i++)
            dst[i] = bytes[i];
    }

    private static unsafe byte[] ExtractKuBytes(KuString value)
    {
        if (value.Length == 0)
            return Array.Empty<byte>();

        var bytes = new byte[(int)value.Length];
        if (KuString.IsShortString(value.Length))
        {
            Span<byte> raw = stackalloc byte[16];
            MemoryMarshal.Write(raw, in value);
            var prefixLen = Math.Min((int)value.Length, 4);
            for (var i = 0; i < prefixLen; i++)
                bytes[i] = raw[4 + i];
            for (var i = 4; i < (int)value.Length; i++)
                bytes[i] = raw[8 + (i - 4)];
            return bytes;
        }

        var ptr = (byte*)value.OverflowPtr;
        for (var i = 0; i < (int)value.Length; i++)
            bytes[i] = ptr[i];
        return bytes;
    }

    private static byte[] BuildInlineKuRecord(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Assert.True(bytes.Length <= (int)KuString.SHORT_STR_LENGTH);
        var record = new byte[16];
        BitConverter.GetBytes((uint)bytes.Length).CopyTo(record, 0);
        for (var i = 0; i < Math.Min(bytes.Length, 4); i++)
            record[4 + i] = bytes[i];
        for (var i = 4; i < bytes.Length; i++)
            record[8 + (i - 4)] = bytes[i];
        return record;
    }

    private static unsafe void WriteBytesAtFileOffset(
        FileHandle fileHandle,
        BogDb.Core.Storage.BufferManager.BufferManager bm,
        ulong fileOffset,
        byte[] payload)
    {
        var pageSize = fileHandle.GetPageSize();
        var remaining = payload.Length;
        var srcPos = 0;
        var currOffset = fileOffset;
        while (remaining > 0)
        {
            var pageIdx = (uint)(currOffset / pageSize);
            var inPageOffset = (uint)(currOffset % pageSize);
            var toWrite = (int)Math.Min((ulong)remaining, pageSize - inPageOffset);

            var page = bm.Pin(fileHandle, pageIdx, PageReadPolicy.DONT_READ_PAGE);
            var dest = new Span<byte>(page + (int)inPageOffset, toWrite);
            payload.AsSpan(srcPos, toWrite).CopyTo(dest);
            fileHandle.GetPageState(pageIdx).SetDirty();
            bm.Unpin(fileHandle, pageIdx);

            remaining -= toWrite;
            srcPos += toWrite;
            currOffset += (uint)toWrite;
        }
    }

    public void Dispose()
    {
        if (File.Exists(_testPath))
        {
            File.Delete(_testPath);
        }
    }
}
