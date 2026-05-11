using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Storage.Table;
using Xunit;

namespace BogDb.Tests.Storage.Table;

public class ColumnChunkTests
{
    [Fact]
    public void Column_AppendsAcrossChunks_AndUpdatesMetadata()
    {
        var col = new Column("age", chunkCapacity: 2);
        col.Append(10L);
        col.Append(20L);
        col.Append(30L);

        Assert.Equal(3, col.Count);
        Assert.Equal(2, col.NumChunks);
        Assert.Equal(30L, col.Lookup(2));

        col.Update(1, 25L);
        Assert.Equal(25L, col.Lookup(1));

        var firstChunk = col.Chunks[0];
        Assert.Equal(2, firstChunk.Count);
        Assert.Equal(10L, firstChunk.Metadata.MinValue);
        Assert.Equal(25L, firstChunk.Metadata.MaxValue);
        Assert.Equal(0, firstChunk.Metadata.NullCount);
    }

    [Fact]
    public void NodeGroup_EnumeratesColumnStoredRows()
    {
        var group = new NodeGroup(capacity: 8);
        group.AppendRow(1L, new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });
        group.AppendRow(2L, new Dictionary<string, object> { ["name"] = "Bob" });

        Assert.Equal((ulong)2, group.GetNumRows());
        Assert.True(group.Columns.ContainsKey("name"));
        Assert.True(group.Columns.ContainsKey("age"));

        var rows = group.EnumerateRows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0].Value["name"]);
        Assert.Equal(30L, rows[0].Value["age"]);
        Assert.Equal("Bob", rows[1].Value["name"]);
        Assert.False(rows[1].Value.ContainsKey("age"));
    }

    [Fact]
    public void ColumnChunk_UsesDictionaryEncoding_ForStringData()
    {
        var col = new Column("name", chunkCapacity: 8);
        col.Append("Alice");
        col.Append("Bob");
        col.Append("Alice");
        col.Append(null);

        var chunk = col.Chunks[0];
        Assert.True(chunk.IsDictionaryEncodedString);
        Assert.Equal(2, chunk.DistinctStringCount);
        Assert.Equal("Alice", col.Lookup(0));
        Assert.Equal("Bob", col.Lookup(1));
        Assert.Equal("Alice", col.Lookup(2));
        Assert.Null(col.Lookup(3));
    }

    [Fact]
    public void ColumnChunk_SwitchesToDictionaryEncoding_AfterNullPrefill()
    {
        var col = new Column("name", chunkCapacity: 8);
        col.Append(null);
        col.Append(null);
        col.Update(1, "Alice");

        var chunk = col.Chunks[0];
        Assert.True(chunk.IsDictionaryEncodedString);
        Assert.Equal(1, chunk.DistinctStringCount);
        Assert.Null(col.Lookup(0));
        Assert.Equal("Alice", col.Lookup(1));
    }

    [Fact]
    public void ColumnChunk_UsesListEntryEncoding_ForListValues()
    {
        var col = new Column("tags", chunkCapacity: 8);
        col.Append(new object?[] { "a", 1L });
        col.Append(new List<object?> { "b" });
        col.Append(null);
        col.Update(1, new object?[] { "b", "c", 2L });

        var chunk = col.Chunks[0];
        Assert.True(chunk.IsListEncoded);
        Assert.True(chunk.ListChildValueCount >= 5);

        var first = Assert.IsType<List<object?>>(col.Lookup(0));
        Assert.Equal(2, first.Count);
        Assert.Equal("a", first[0]);

        var second = Assert.IsType<List<object?>>(col.Lookup(1));
        Assert.Equal(3, second.Count);
        Assert.Equal("c", second[1]);
        Assert.Null(col.Lookup(2));
    }

    [Fact]
    public void ColumnChunk_UsesStructEncoding_ForMapValues()
    {
        var col = new Column("meta", chunkCapacity: 8);
        col.Append(new Dictionary<string, object?> { ["a"] = 1L });
        col.Append(null);
        col.Update(1, new Dictionary<string, object?> { ["b"] = "x" });

        var chunk = col.Chunks[0];
        Assert.True(chunk.IsStructEncoded);
        Assert.Equal(2, chunk.StructFieldCount);

        var first = Assert.IsType<Dictionary<string, object?>>(col.Lookup(0));
        Assert.Equal(1L, first["a"]);
        var second = Assert.IsType<Dictionary<string, object?>>(col.Lookup(1));
        Assert.Equal("x", second["b"]);
    }

    [Fact]
    public void ColumnChunk_CountTracksSpecializedModes()
    {
        var stringCol = new Column("name", chunkCapacity: 8);
        stringCol.Append("Alice");
        stringCol.Append("Bob");
        Assert.Equal(2, stringCol.Chunks[0].Count);

        var listCol = new Column("tags", chunkCapacity: 8);
        listCol.Append(new object?[] { 1L });
        listCol.Append(new object?[] { 2L, 3L });
        Assert.Equal(2, listCol.Chunks[0].Count);

        var structCol = new Column("meta", chunkCapacity: 8);
        structCol.Append(new Dictionary<string, object?> { ["a"] = 1L });
        structCol.Append(new Dictionary<string, object?> { ["b"] = 2L });
        Assert.Equal(2, structCol.Chunks[0].Count);
    }

    [Fact]
    public void ColumnChunk_UsesNullEncoding_AndCanTransitionOnUpdate()
    {
        var col = new Column("maybe_name", chunkCapacity: 8);
        col.Append(null);
        col.Append(null);

        var chunk = col.Chunks[0];
        Assert.True(chunk.IsNullEncoded);
        Assert.Equal(2, chunk.Count);
        Assert.Equal(0, chunk.Values.Count(v => v is not null));

        col.Update(1, "Alice");
        chunk = col.Chunks[0];
        Assert.True(chunk.IsDictionaryEncodedString);
        Assert.Equal(2, chunk.Count);
        Assert.Null(col.Lookup(0));
        Assert.Equal("Alice", col.Lookup(1));
    }

    [Fact]
    public void Column_Truncate_ReclaimsTailAcrossSpecializedModes()
    {
        var names = new Column("name", chunkCapacity: 8);
        names.Append("Alice");
        names.Append("Bob");
        names.Append("Cara");
        names.Truncate(2);
        Assert.Equal(2, names.Count);
        Assert.Equal("Bob", names.Lookup(1));
        Assert.True(names.Chunks[0].IsDictionaryEncodedString);
        Assert.Equal(2, names.Chunks[0].DistinctStringCount);

        var tags = new Column("tags", chunkCapacity: 8);
        tags.Append(new object?[] { "a" });
        tags.Append(new object?[] { "b", "c" });
        tags.Append(new object?[] { "d", "e", "f" });
        tags.Truncate(2);
        Assert.Equal(2, tags.Count);
        Assert.True(tags.Chunks[0].IsListEncoded);
        Assert.Equal(3, tags.Chunks[0].ListChildValueCount);

        var meta = new Column("meta", chunkCapacity: 8);
        meta.Append(new Dictionary<string, object?> { ["a"] = 1L });
        meta.Append(new Dictionary<string, object?> { ["b"] = 2L });
        meta.Append(new Dictionary<string, object?> { ["c"] = 3L });
        meta.Truncate(2);
        Assert.Equal(2, meta.Count);
        Assert.True(meta.Chunks[0].IsStructEncoded);
        Assert.Equal(2, meta.Chunks[0].StructFieldCount);
    }
}
