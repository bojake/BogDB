using System.Collections.Generic;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.GraphDataScience;

public class GdsTransactionVisibilityTests
{
    [Fact]
    public void GdsCall_UsesQueryManagedTransactionVisibility_ForUncommittedInsert()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        db.NodeTables["Person"] = new NodeTableData();
        var table = db.NodeTables["Person"];
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var outsideResult = conn.Query("CALL pagerank() RETURN *");
        Assert.True(outsideResult.IsSuccess);
        Assert.Equal((ulong)1, outsideResult.GetNumTuples());

        Assert.True(conn.Query("BEGIN TRANSACTION").IsSuccess);
        var tx = conn.ClientContext.ActiveTransaction!;
        table.Upsert(tx, "n2", new Dictionary<string, object> { ["name"] = "Bob" });

        var inTxResult = conn.Query("CALL pagerank() RETURN *");
        Assert.True(inTxResult.IsSuccess);
        Assert.Equal((ulong)2, inTxResult.GetNumTuples());

        Assert.True(conn.Query("ROLLBACK").IsSuccess);

        var afterRollbackResult = conn.Query("CALL pagerank() RETURN *");
        Assert.True(afterRollbackResult.IsSuccess);
        Assert.Equal((ulong)1, afterRollbackResult.GetNumTuples());
    }

    [Fact]
    public void GdsCall_UsesQueryManagedTransactionVisibility_ForUncommittedDelete()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        db.NodeTables["Person"] = new NodeTableData();
        var table = db.NodeTables["Person"];
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });
        table.Upsert("n2", new Dictionary<string, object> { ["name"] = "Bob" });

        var outsideResult = conn.Query("CALL wcc() RETURN *");
        Assert.True(outsideResult.IsSuccess);
        Assert.Equal((ulong)2, outsideResult.GetNumTuples());

        Assert.True(conn.Query("BEGIN TRANSACTION").IsSuccess);
        var tx = conn.ClientContext.ActiveTransaction!;
        Assert.True(table.Remove(tx, "n2"));

        var inTxResult = conn.Query("CALL wcc() RETURN *");
        Assert.True(inTxResult.IsSuccess);
        Assert.Equal((ulong)1, inTxResult.GetNumTuples());

        Assert.True(conn.Query("ROLLBACK").IsSuccess);

        var afterRollbackResult = conn.Query("CALL wcc() RETURN *");
        Assert.True(afterRollbackResult.IsSuccess);
        Assert.Equal((ulong)2, afterRollbackResult.GetNumTuples());
    }
}
