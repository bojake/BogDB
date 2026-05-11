using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Storage.Table;
using Xunit;

namespace BogDb.Tests.Storage.Table;

public class NodeGroupVersionInfoTests
{
    [Fact]
    public void NodeGroup_InsertVisibility_RespectsTransactionStartAndCommit()
    {
        var group = new NodeGroup(capacity: 8);
        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 100,
            startTS: 0);

        group.AppendRow(writer, "n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 1,
            startTS: 0);
        Assert.Empty(group.EnumerateRows(oldReader));
        Assert.Single(group.EnumerateRows(writer));

        group.CommitVersions(writer, commitTS: 5);
        var newReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 2,
            startTS: 5);
        Assert.Single(group.EnumerateRows(newReader));
    }

    [Fact]
    public void NodeGroup_DeleteVisibility_CommitAndRollbackBehaveCorrectly()
    {
        var group = new NodeGroup(capacity: 8);
        group.AppendRow("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 101,
            startTS: 0);
        group.MarkDeleted(deleter, rowIdx: 0);

        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 3,
            startTS: 0);
        Assert.Single(group.EnumerateRows(oldReader));
        Assert.Empty(group.EnumerateRows(deleter));

        group.RollbackVersions(deleter);
        Assert.Single(group.EnumerateRows(oldReader));

        var deleter2 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 102,
            startTS: 0);
        group.MarkDeleted(deleter2, rowIdx: 0);
        group.CommitVersions(deleter2, commitTS: 7);

        var readerBeforeCommit = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 4,
            startTS: 6);
        var readerAfterCommit = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 5,
            startTS: 7);

        Assert.Single(group.EnumerateRows(readerBeforeCommit));
        Assert.Empty(group.EnumerateRows(readerAfterCommit));
    }

    [Fact]
    public void NodeGroupCollection_EnumerateRows_UsesVersionVisibility()
    {
        var collection = new NodeGroupCollection(groupCapacity: 4);
        collection.AppendRow("n1", new Dictionary<string, object> { ["v"] = 1L });
        collection.AppendRow("n2", new Dictionary<string, object> { ["v"] = 2L });

        var group = collection.GetNodeGroup(0);
        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 103,
            startTS: 0);
        group.MarkDeleted(deleter, rowIdx: 1);
        group.CommitVersions(deleter, commitTS: 9);

        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 6,
            startTS: 9);
        var rows = collection.EnumerateRows(reader).ToList();
        Assert.Single(rows);
        Assert.Equal("n1", rows[0].Key);
    }

    [Fact]
    public void NodeGroup_RollbackInsert_ReclaimsTailCapacity()
    {
        var group = new NodeGroup(capacity: 2);
        group.AppendRow("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 200,
            startTS: 0);
        group.AppendRow(writer, "n2", new Dictionary<string, object> { ["name"] = "Bob" });
        Assert.True(group.IsFull);

        group.RollbackVersions(writer);

        Assert.False(group.IsFull);
        Assert.Equal((ulong)1, group.GetNumRows());
        var rows = group.EnumerateRows().ToList();
        Assert.Single(rows);
        Assert.Equal("n1", rows[0].Key);

        group.AppendRow("n3", new Dictionary<string, object> { ["name"] = "Cara" });
        var finalRows = group.EnumerateRows().ToList();
        Assert.Equal(2, finalRows.Count);
        Assert.Equal("n3", finalRows[1].Key);
    }

    [Fact]
    public void NodeGroup_RollbackInsert_ReclaimsTailAcrossSpecializedColumns()
    {
        var group = new NodeGroup(capacity: 3);
        group.AppendRow("n1", new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["tags"] = new object?[] { "a" },
            ["meta"] = new Dictionary<string, object?> { ["role"] = "seed" }
        });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 201,
            startTS: 0);
        group.AppendRow(writer, "n2", new Dictionary<string, object>
        {
            ["name"] = "Bob",
            ["tags"] = new object?[] { "b", "c" },
            ["meta"] = new Dictionary<string, object?> { ["role"] = "temp", ["extra"] = 1L }
        });

        group.RollbackVersions(writer);

        var nameColumn = group.Columns["name"];
        Assert.Equal(1, nameColumn.Count);
        Assert.Equal("Alice", nameColumn.Lookup(0));
        Assert.True(nameColumn.Chunks[0].IsDictionaryEncodedString);
        Assert.Equal(1, nameColumn.Chunks[0].DistinctStringCount);

        var tagsColumn = group.Columns["tags"];
        Assert.Equal(1, tagsColumn.Count);
        Assert.True(tagsColumn.Chunks[0].IsListEncoded);
        Assert.Equal(1, tagsColumn.Chunks[0].ListChildValueCount);

        var metaColumn = group.Columns["meta"];
        Assert.Equal(1, metaColumn.Count);
        Assert.True(metaColumn.Chunks[0].IsStructEncoded);
        Assert.Equal(1, metaColumn.Chunks[0].StructFieldCount);
    }
}
