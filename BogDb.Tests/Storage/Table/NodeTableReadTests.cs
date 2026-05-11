using System;
using System.IO;
using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Storage;
using BogDb.Core.Storage.Compression;
using BogDb.Core.Storage.Table;
using Xunit;

namespace BogDb.Tests.Storage.Table;

public class NodeTableReadTests
{
    [Fact]
    public void ColumnChunkData_InMemoryScan_WritesValueVector()
    {
        var metadata = new CompressionMetadata(1L, 3L, CompressionType.INTEGER_BITPACKING);
        var chunk = new ColumnChunkData(new object?[] { 1L, 2L, 3L }, LogicalTypeID.INT64, metadata);
        var vector = new ValueVector(LogicalTypeID.INT64, capacity: 8);

        chunk.Scan(bm: null!, output: vector, offset: 0, length: 3, posInOutputVector: 0);

        Assert.Equal(1L, vector.GetValue<long>(0));
        Assert.Equal(2L, vector.GetValue<long>(1));
        Assert.Equal(3L, vector.GetValue<long>(2));
    }

    [Fact]
    public void NodeTable_Read_UsesColumnPath()
    {
        var age = new Column("age", chunkCapacity: 2);
        age.Append(10L);
        age.Append(20L);
        age.Append(30L);

        var score = new Column("score", chunkCapacity: 2);
        score.Append(1.5);
        score.Append(2.5);
        score.Append(3.5);

        var table = new NodeTable(tableId: 1, columns: new[] { age, score });
        var ageVector = new ValueVector(LogicalTypeID.INT64, capacity: 8);
        var scoreVector = new ValueVector(LogicalTypeID.DOUBLE, capacity: 8);

        table.Read(BogDb.Core.Transaction.Transaction.DUMMY_TRANSACTION, new[] { ageVector, scoreVector }, nodeOffsetStart: 1, numNodesToRead: 2);

        Assert.Equal(20L, ageVector.GetValue<long>(0));
        Assert.Equal(30L, ageVector.GetValue<long>(1));
        Assert.Equal(2.5, scoreVector.GetValue<double>(0));
        Assert.Equal(3.5, scoreVector.GetValue<double>(1));
    }

    [Fact]
    public void ColumnChunkData_InMemoryScan_SupportsString()
    {
        var chunk = new ColumnChunkData(new object?[] { "Alice", null, "Bob" }, LogicalTypeID.STRING);
        using var vector = new ValueVector(LogicalTypeID.STRING, capacity: 8);

        chunk.Scan(bm: null!, output: vector, offset: 0, length: 3, posInOutputVector: 0);

        Assert.Equal("Alice", vector.GetValue<KuString>(0).GetAsString());
        Assert.True(vector.IsNull(1));
        Assert.Equal("Bob", vector.GetValue<KuString>(2).GetAsString());
    }

    [Fact]
    public void NodeTable_Read_UsesColumnPath_ForStringAndInternalId()
    {
        var name = new Column("name", chunkCapacity: 2);
        name.Append("Alice");
        name.Append("Bob");

        var offset = new Column("offset", chunkCapacity: 2);
        offset.Append(new InternalID(10, 1));
        offset.Append(new InternalID(20, 1));

        var table = new NodeTable(tableId: 1, columns: new[] { name, offset });
        using var nameVector = new ValueVector(LogicalTypeID.STRING, capacity: 8);
        using var offsetVector = new ValueVector(LogicalTypeID.INTERNAL_ID, capacity: 8);

        table.Read(BogDb.Core.Transaction.Transaction.DUMMY_TRANSACTION, new[] { nameVector, offsetVector }, nodeOffsetStart: 0, numNodesToRead: 2);

        Assert.Equal("Alice", nameVector.GetValue<KuString>(0).GetAsString());
        Assert.Equal("Bob", nameVector.GetValue<KuString>(1).GetAsString());
        Assert.Equal(new InternalID(10, 1), offsetVector.GetValue<InternalID>(0));
        Assert.Equal(new InternalID(20, 1), offsetVector.GetValue<InternalID>(1));
    }

    [Fact]
    public void ColumnChunkData_InMemoryScan_SupportsListAndStructAuxPayloads()
    {
        var listChunk = new ColumnChunkData(new object?[]
        {
            new object?[] { 1L, "x" },
            null
        }, LogicalTypeID.LIST);
        using var listVector = new ValueVector(LogicalTypeID.LIST, capacity: 8);
        listChunk.Scan(bm: null!, output: listVector, offset: 0, length: 2, posInOutputVector: 0);

        var list0 = Assert.IsType<List<object?>>(listVector.GetAuxValue(0));
        Assert.Equal(2, list0.Count);
        Assert.Equal(1L, list0[0]);
        Assert.True(listVector.IsNull(1));

        var structChunk = new ColumnChunkData(new object?[]
        {
            new Dictionary<string, object?> { ["a"] = 1L },
            null
        }, LogicalTypeID.STRUCT);
        using var structVector = new ValueVector(LogicalTypeID.STRUCT, capacity: 8);
        structChunk.Scan(bm: null!, output: structVector, offset: 0, length: 2, posInOutputVector: 0);

        var struct0 = Assert.IsType<Dictionary<string, object?>>(structVector.GetAuxValue(0));
        Assert.Equal(1L, struct0["a"]);
        Assert.True(structVector.IsNull(1));
    }

    [Fact]
    public void NodeTable_Read_UsesColumnPath_ForListAndStruct()
    {
        var tags = new Column("tags", chunkCapacity: 2);
        tags.Append(new object?[] { "a", "b" });
        tags.Append(new object?[] { "c" });

        var meta = new Column("meta", chunkCapacity: 2);
        meta.Append(new Dictionary<string, object?> { ["k"] = 1L });
        meta.Append(new Dictionary<string, object?> { ["k"] = 2L });

        var table = new NodeTable(tableId: 1, columns: new[] { tags, meta });
        using var tagsVector = new ValueVector(LogicalTypeID.LIST, capacity: 8);
        using var metaVector = new ValueVector(LogicalTypeID.STRUCT, capacity: 8);

        table.Read(BogDb.Core.Transaction.Transaction.DUMMY_TRANSACTION, new[] { tagsVector, metaVector }, nodeOffsetStart: 0, numNodesToRead: 2);

        var tags0 = Assert.IsType<List<object?>>(tagsVector.GetAuxValue(0));
        Assert.Equal("a", tags0[0]);
        var meta1 = Assert.IsType<Dictionary<string, object?>>(metaVector.GetAuxValue(1));
        Assert.Equal(2L, meta1["k"]);
    }

    [Fact]
    public void ColumnChunkData_InMemoryScan_SupportsUuidUint128DecimalAndInterval()
    {
        var uuidGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var uuidChunk = new ColumnChunkData(new object?[] { uuidGuid }, LogicalTypeID.UUID);
        using var uuidVector = new ValueVector(LogicalTypeID.UUID, capacity: 8);
        uuidChunk.Scan(bm: null!, output: uuidVector, offset: 0, length: 1, posInOutputVector: 0);
        Assert.False(uuidVector.IsNull(0));

        var u128 = (UInt128.One << 100) | 12345u;
        var u128Chunk = new ColumnChunkData(new object?[] { u128 }, LogicalTypeID.UINT128);
        using var u128Vector = new ValueVector(LogicalTypeID.UINT128, capacity: 8);
        u128Chunk.Scan(bm: null!, output: u128Vector, offset: 0, length: 1, posInOutputVector: 0);
        Assert.Equal(u128, u128Vector.GetValue<UInt128>(0));

        var decimalChunk = new ColumnChunkData(new object?[] { 12.5m }, LogicalTypeID.DECIMAL);
        using var decimalVector = new ValueVector(LogicalTypeID.DECIMAL, capacity: 8);
        decimalChunk.Scan(bm: null!, output: decimalVector, offset: 0, length: 1, posInOutputVector: 0);
        Assert.Equal(12.5, decimalVector.GetValue<double>(0), 6);

        var intervalChunk = new ColumnChunkData(new object?[] { 86400L }, LogicalTypeID.INTERVAL);
        using var intervalVector = new ValueVector(LogicalTypeID.INTERVAL, capacity: 8);
        intervalChunk.Scan(bm: null!, output: intervalVector, offset: 0, length: 1, posInOutputVector: 0);
        Assert.Equal(86400L, intervalVector.GetValue<long>(0));
    }

    [Fact]
    public void ColumnChunkData_FileBackedCtor_MapsPhysicalTypesToExpectedLogicalTypes()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(
                bufferPoolSize: 4 * (long)FileHandle.BOGDB_PAGE_SIZE,
                maxDbSize: 16 * 1024 * 1024);
            using var fh = bm.GetFileHandle(path, flags: 0, fileIndex: 77);

            Assert.Equal(LogicalTypeID.UINT128, new ColumnChunkData(fh, 0, PhysicalTypeID.UINT128).DataType);
            Assert.Equal(LogicalTypeID.INTERVAL, new ColumnChunkData(fh, 0, PhysicalTypeID.INTERVAL).DataType);
            Assert.Equal(LogicalTypeID.LIST, new ColumnChunkData(fh, 0, PhysicalTypeID.LIST).DataType);
            Assert.Equal(LogicalTypeID.STRUCT, new ColumnChunkData(fh, 0, PhysicalTypeID.STRUCT).DataType);
            Assert.Equal(LogicalTypeID.FLOAT, new ColumnChunkData(fh, 0, PhysicalTypeID.ALP_EXCEPTION_FLOAT).DataType);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ColumnChunkData_FileBackedCtor_UnknownPhysicalType_FallsBackToAnyWithoutThrowing()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(
                bufferPoolSize: 4 * (long)FileHandle.BOGDB_PAGE_SIZE,
                maxDbSize: 16 * 1024 * 1024);
            using var fh = bm.GetFileHandle(path, flags: 0, fileIndex: 78);

            var unknown = (PhysicalTypeID)255;
            var chunk = new ColumnChunkData(fh, 0, unknown);
            Assert.Equal(LogicalTypeID.ANY, chunk.DataType);

            using var vector = new ValueVector(LogicalTypeID.ANY, capacity: 8);
            chunk.Scan(bm, vector, offset: 0, length: 1, posInOutputVector: 0);
            Assert.Equal(16 * vector.Capacity, vector.GetAsReadOnlySpan().Length);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
