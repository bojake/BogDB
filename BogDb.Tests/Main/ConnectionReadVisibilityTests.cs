using System.Collections.Generic;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public class ConnectionReadVisibilityTests
{
    [Fact]
    public void ReadHelpers_RespectQueryManagedTransactionVisibility_ForNodes()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        db.NodeTables["Person"] = new NodeTableData();
        var table = db.NodeTables["Person"];
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        Assert.True(conn.Query("BEGIN TRANSACTION").IsSuccess);
        var tx = conn.ClientContext.ActiveTransaction!;
        Assert.True(table.Remove(tx, "n1"));

        Assert.Null(conn.ReadNode("Person", "n1"));
        var byId = conn.GetNodeById("n1", out var tableName);
        Assert.Null(byId);
        Assert.Null(tableName);

        Assert.True(conn.Query("ROLLBACK").IsSuccess);
        Assert.NotNull(conn.ReadNode("Person", "n1"));
    }

    [Fact]
    public void GetOutgoingEdges_RespectsQueryManagedTransactionVisibility()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        db.RelTables["KNOWS"] = new RelTableData();
        var relTable = db.RelTables["KNOWS"];
        var edge = new EdgeKey("a", "b");
        relTable.Upsert(edge, new Dictionary<string, object> { ["since"] = 2020L });

        Assert.Single(conn.GetOutgoingEdges("a"));

        Assert.True(conn.Query("BEGIN TRANSACTION").IsSuccess);
        var tx = conn.ClientContext.ActiveTransaction!;
        Assert.True(relTable.Remove(tx, edge));
        Assert.Empty(conn.GetOutgoingEdges("a"));

        Assert.True(conn.Query("ROLLBACK").IsSuccess);
        Assert.Single(conn.GetOutgoingEdges("a"));
    }
}
