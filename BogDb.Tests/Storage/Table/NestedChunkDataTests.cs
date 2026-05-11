using System.Collections.Generic;
using BogDb.Core.Storage.Table;
using Xunit;

namespace BogDb.Tests.Storage.Table;

public class NestedChunkDataTests
{
    [Fact]
    public void ListChunkData_AppendsAndUpdatesEntries()
    {
        var chunk = new ListChunkData(capacity: 8);
        chunk.Append(new object?[] { 1L, "a" });
        chunk.Append(null);
        chunk.Append(new List<object?> { "x" });

        Assert.Equal(3, chunk.Count);
        Assert.Equal(3, chunk.ChildValueCount);

        chunk.Update(2, new object?[] { "x", "y" });
        var updated = Assert.IsType<List<object?>>(chunk.Lookup(2));
        Assert.Equal(2, updated.Count);
        Assert.Equal("y", updated[1]);
    }

    [Fact]
    public void StructChunkData_TracksFieldsAcrossRows()
    {
        var chunk = new StructChunkData(capacity: 8);
        chunk.Append(new Dictionary<string, object?> { ["a"] = 1L });
        chunk.Append(new Dictionary<string, object?> { ["b"] = "x" });
        chunk.Append(null);

        Assert.Equal(3, chunk.Count);
        Assert.Contains("a", chunk.FieldNames);
        Assert.Contains("b", chunk.FieldNames);

        var row0 = Assert.IsType<Dictionary<string, object?>>(chunk.Lookup(0));
        Assert.Equal(1L, row0["a"]);
        Assert.Null(chunk.Lookup(2));
    }
}
