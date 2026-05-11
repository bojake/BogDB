using System.Collections.Generic;
using BogDb.Core.Main;
using BogDb.Core.Processor.Operator.Scan;
using Xunit;

namespace BogDb.Tests.Processor;

public class ScanNodePropertyVisibilityTests
{
    [Fact]
    public void ScanNodeProperty_HidesUncommittedRowsFromOlderReader()
    {
        using var database = BogDatabase.Open(":memory:");
        database.NodeTables["Person"] = new NodeTableData();
        var table = database.NodeTables["Person"];

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 300,
            startTS: 0);
        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 1,
            startTS: 0);

        table.Upsert(writer, "n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var scanOldReader = new ScanNodeProperty(database, "Person", "p", id: 1);
        var oldContext = new BogDb.Core.Processor.ExecutionContext(oldReader, database.BufferManager);
        Assert.False(scanOldReader.GetNextTuple(oldContext));

        table.CommitVersions(writer, commitTS: 7);
        var newReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 2,
            startTS: 7);
        var scanNewReader = new ScanNodeProperty(database, "Person", "p", id: 2);
        var newContext = new BogDb.Core.Processor.ExecutionContext(newReader, database.BufferManager);
        Assert.True(scanNewReader.GetNextTuple(newContext));
        Assert.Equal("n1", newContext.CurrentNodeId);
    }
}
