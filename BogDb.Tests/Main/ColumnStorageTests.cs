using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public class ColumnStorageTests
{
    [Fact]
    public void NodeTable_UpsertMaintainsColumnVectors()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.INT64,
            ["name"] = LogicalTypeID.STRING,
            ["age"] = LogicalTypeID.INT64
        });
        conn.UpsertNode("Person", 1L, new Dictionary<string, object> { ["id"] = 1L, ["name"] = "Alice", ["age"] = 30L });
        conn.UpsertNode("Person", 2L, new Dictionary<string, object> { ["id"] = 2L, ["name"] = "Bob" });
        conn.UpsertNode("Person", 2L, new Dictionary<string, object> { ["id"] = 2L, ["name"] = "Bobby", ["age"] = 41L });
        conn.Commit();

        var table = db.NodeTables["Person"];
        Assert.Equal(2, table.Count);

        Assert.True(table.TryGetOffset(1L, out var row0));
        Assert.True(table.TryGetOffset(2L, out var row1));

        Assert.Equal("Alice", table.Columns["name"][(int)row0]);
        Assert.Equal("Bobby", table.Columns["name"][(int)row1]);
        Assert.Equal(30L, table.Columns["age"][(int)row0]);
        Assert.Equal(41L, table.Columns["age"][(int)row1]);
    }

    [Fact]
    public void RelTable_UpsertMaintainsColumnVectors()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID> { ["id"] = LogicalTypeID.INT64 });
        conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
        {
            ["since"] = LogicalTypeID.INT64
        });
        conn.UpsertNode("Person", 1L, new Dictionary<string, object> { ["id"] = 1L });
        conn.UpsertNode("Person", 2L, new Dictionary<string, object> { ["id"] = 2L });
        conn.UpsertRelationship("KNOWS", 1L, 2L, new Dictionary<string, object> { ["since"] = 2020L });
        conn.UpsertRelationship("KNOWS", 1L, 2L, new Dictionary<string, object> { ["since"] = 2024L });
        conn.Commit();

        var table = db.RelTables["KNOWS"];
        Assert.Equal(1, table.Count);
        Assert.Single(table.Columns["since"]);
        Assert.Equal(2024L, table.Columns["since"][0]);
    }
}
