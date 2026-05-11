using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public class RelTableAdjacencyTests
{
    [Fact]
    public void RelTableData_MaintainsOutgoingAndIncomingAdjacency_OnInsert()
    {
        var table = new RelTableData();
        table.Upsert(new EdgeKey("a", "b"), new Dictionary<string, object> { ["w"] = 1L });
        table.Upsert(new EdgeKey("a", "c"), new Dictionary<string, object> { ["w"] = 2L });
        table.Upsert(new EdgeKey("d", "a"), new Dictionary<string, object> { ["w"] = 3L });

        var outgoing = table.GetOutgoingEdgeRows("a").Select(x => x.RowIndex).OrderBy(x => x).ToArray();
        var incoming = table.GetIncomingEdgeRows("a").Select(x => x.RowIndex).OrderBy(x => x).ToArray();

        Assert.Equal(new[] { 0, 1 }, outgoing);
        Assert.Equal(new[] { 2 }, incoming);
    }

    [Fact]
    public void RelTableData_RepairsAdjacency_WhenSwapRemoveMovesTailRow()
    {
        var table = new RelTableData();
        var ab = new EdgeKey("a", "b");
        var ac = new EdgeKey("a", "c");
        var da = new EdgeKey("d", "a");
        table.Upsert(ab, new Dictionary<string, object> { ["w"] = 1L });
        table.Upsert(ac, new Dictionary<string, object> { ["w"] = 2L });
        table.Upsert(da, new Dictionary<string, object> { ["w"] = 3L });

        Assert.True(table.Remove(ac));

        var outgoingA = table.EnumerateOutgoingRows("a").Select(kvp => kvp.Key).ToArray();
        var incomingA = table.EnumerateIncomingRows("a").Select(kvp => kvp.Key).ToArray();

        Assert.Single(outgoingA);
        Assert.Equal(ab, outgoingA[0]);
        Assert.Single(incomingA);
        Assert.Equal(da, incomingA[0]);

        Assert.True(table.TryGetRowByIndex(1, out var movedKey, out var movedProps));
        Assert.Equal(da, movedKey);
        Assert.NotNull(movedProps);
    }

    [Fact]
    public void RelTableData_AdjacencyEnumeration_RespectsTransactionVisibility()
    {
        var table = new RelTableData();
        var committed = new EdgeKey("a", "b");
        table.Upsert(committed, new Dictionary<string, object> { ["w"] = 1L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 300,
            startTS: 0);
        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 10,
            startTS: 0);

        var pending = new EdgeKey("a", "c");
        table.Upsert(writer, pending, new Dictionary<string, object> { ["w"] = 2L });

        var readerOutgoing = table.EnumerateOutgoingRows("a", reader).Select(kvp => kvp.Key).ToArray();
        var writerOutgoing = table.EnumerateOutgoingRows("a", writer).Select(kvp => kvp.Key).ToArray();

        Assert.Single(readerOutgoing);
        Assert.Equal(committed, readerOutgoing[0]);
        Assert.Equal(2, writerOutgoing.Length);
        Assert.Contains(pending, writerOutgoing);
    }

    [Fact]
    public void RelTableData_AdjacencyEnumeration_OldReaderSeesOldVisibleEdge_AfterDeleteReinsertCommit()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["w"] = 1L });

        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 20,
            startTS: 0);

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 301,
            startTS: 0);

        Assert.True(table.Remove(writer, key));
        table.Upsert(writer, key, new Dictionary<string, object> { ["w"] = 2L });
        table.CommitVersions(writer, commitTS: 6);

        var oldReaderOutgoing = table.EnumerateOutgoingRows("a", oldReader).ToArray();
        Assert.Single(oldReaderOutgoing);
        Assert.Equal(key, oldReaderOutgoing[0].Key);
        Assert.Equal(1L, oldReaderOutgoing[0].Value["w"]);

        var newReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 21,
            startTS: 6);
        var newReaderOutgoing = table.EnumerateOutgoingRows("a", newReader).ToArray();
        Assert.Single(newReaderOutgoing);
        Assert.Equal(key, newReaderOutgoing[0].Key);
        Assert.Equal(2L, newReaderOutgoing[0].Value["w"]);
    }
}
