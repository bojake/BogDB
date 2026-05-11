using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public class TableDataVersionInfoTests
{
    [Fact]
    public void NodeTableData_Visibility_CommitRollback_Delete()
    {
        var table = new NodeTableData();
        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 200,
            startTS: 0);
        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 1,
            startTS: 0);

        table.Upsert(writer, "n1", new Dictionary<string, object> { ["name"] = "Alice" });
        Assert.Empty(table.EnumerateRows(oldReader));
        Assert.Single(table.EnumerateRows(writer));

        table.CommitVersions(writer, commitTS: 5);
        var newReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 2,
            startTS: 5);
        Assert.Single(table.EnumerateRows(newReader));

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 201,
            startTS: 5);
        Assert.True(table.Remove(deleter, "n1"));
        Assert.Empty(table.EnumerateRows(deleter));
        Assert.Single(table.EnumerateRows(newReader));

        table.RollbackVersions(deleter);
        Assert.Single(table.EnumerateRows(newReader));

        table.Remove(deleter, "n1");
        table.CommitVersions(deleter, commitTS: 8);
        var afterDeleteReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 3,
            startTS: 8);
        Assert.Empty(table.EnumerateRows(afterDeleteReader));
    }

    [Fact]
    public void RelTableData_Visibility_CommitRollback_Delete()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 210,
            startTS: 0);
        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 4,
            startTS: 0);

        table.Upsert(writer, key, new Dictionary<string, object> { ["since"] = 2020L });
        Assert.Empty(table.EnumerateRows(oldReader));

        table.CommitVersions(writer, commitTS: 6);
        var committedReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 5,
            startTS: 6);
        Assert.Single(table.EnumerateRows(committedReader));

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 211,
            startTS: 6);
        Assert.True(table.Remove(deleter, key));
        Assert.Empty(table.EnumerateRows(deleter));
        Assert.Single(table.EnumerateRows(committedReader));

        table.RollbackVersions(deleter);
        Assert.Single(table.EnumerateRows(committedReader));

        table.Remove(deleter, key);
        table.CommitVersions(deleter, commitTS: 9);
        var afterDeleteReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 6,
            startTS: 9);
        Assert.Empty(table.EnumerateRows(afterDeleteReader));
    }

    [Fact]
    public void NodeTableData_DetectsWriteWriteDeleteConflict()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["v"] = 1L });

        var tx1 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 220,
            startTS: 0);
        var tx2 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 221,
            startTS: 0);

        Assert.True(table.Remove(tx1, "n1"));
        var ex = Record.Exception(() => table.Remove(tx2, "n1"));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex!.Message);
    }

    [Fact]
    public void NodeTableData_RollbackInsert_ReclaimsTailOffsets()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 222,
            startTS: 0);
        table.Upsert(writer, "n2", new Dictionary<string, object> { ["name"] = "Bob" });

        Assert.Equal(2, table.Count);
        Assert.True(table.TryGetOffset("n2", out _));

        table.RollbackVersions(writer);

        Assert.Equal(1, table.Count);
        Assert.False(table.TryGetOffset("n2", out _));
        Assert.False(table.TryGetByOffset(1, out _, out _));
        var rows = table.EnumerateRows().ToList();
        Assert.Single(rows);
        Assert.Equal("n1", rows[0].Key);
    }

    [Fact]
    public void RelTableData_RollbackInsert_ReclaimsTailOffsetsAndAdjacency()
    {
        var table = new RelTableData();
        var committed = new EdgeKey("a", "b");
        table.Upsert(committed, new Dictionary<string, object> { ["since"] = 2020L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 223,
            startTS: 0);
        var pending = new EdgeKey("a", "c");
        table.Upsert(writer, pending, new Dictionary<string, object> { ["since"] = 2024L });

        Assert.Equal(2, table.Count);
        Assert.Equal(2, table.GetOutgoingEdgeRows("a").Count);

        table.RollbackVersions(writer);

        Assert.Equal(1, table.Count);
        Assert.Single(table.GetOutgoingEdgeRows("a"));
        var rows = table.EnumerateOutgoingRows("a").ToList();
        Assert.Single(rows);
        Assert.Equal(committed, rows[0].Key);
        Assert.False(table.TryGetRowByIndex(1, out _, out _));
    }

    [Fact]
    public void NodeTableData_SetProperty_ConflictsWithPendingDelete()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 224,
            startTS: 0);
        var updater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 225,
            startTS: 0);

        Assert.True(table.Remove(deleter, "n1"));
        var ex = Record.Exception(() => table.SetProperty(updater, "n1", "name", "Alicia"));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex!.Message);
    }

    [Fact]
    public void NodeTableData_Delete_ConflictsWithPendingPropertyUpdate()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var updater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 226,
            startTS: 0);
        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 227,
            startTS: 0);

        Assert.True(table.SetProperty(updater, "n1", "name", "Alicia"));
        var ex = Record.Exception(() => table.Remove(deleter, "n1"));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex!.Message);
    }

    [Fact]
    public void RelTableData_SetProperty_ConflictsWithPendingDelete()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["since"] = 2020L });

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 228,
            startTS: 0);
        var updater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 229,
            startTS: 0);

        Assert.True(table.Remove(deleter, key));
        var ex = Record.Exception(() => table.SetProperty(updater, key, "since", 2021L));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex!.Message);
    }

    [Fact]
    public void RelTableData_Delete_ConflictsWithPendingPropertyUpdate()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["since"] = 2020L });

        var updater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 230,
            startTS: 0);
        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 231,
            startTS: 0);

        Assert.True(table.SetProperty(updater, key, "since", 2021L));
        var ex = Record.Exception(() => table.Remove(deleter, key));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex!.Message);
    }

    [Fact]
    public void NodeTableData_TransactionalUpsert_OnExistingRow_UsesOverlayUntilCommit()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 232,
            startTS: 0);
        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 7,
            startTS: 0);

        table.Upsert(writer, "n1", new Dictionary<string, object> { ["name"] = "Alicia", ["age"] = 31L });

        Assert.True(table.TryGetProperties(oldReader, "n1", out var oldProps));
        Assert.Equal("Alice", oldProps!["name"]);
        Assert.Equal(30L, oldProps["age"]);

        Assert.True(table.TryGetProperties(writer, "n1", out var writerProps));
        Assert.Equal("Alicia", writerProps!["name"]);
        Assert.Equal(31L, writerProps["age"]);

        table.CommitVersions(writer, commitTS: 10);

        var committedReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 8,
            startTS: 10);
        Assert.True(table.TryGetProperties(committedReader, "n1", out var committedProps));
        Assert.Equal("Alicia", committedProps!["name"]);
        Assert.Equal(31L, committedProps["age"]);
    }

    [Fact]
    public void NodeTableData_TransactionalUpsert_OnExistingRow_RollsBackCleanly()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 233,
            startTS: 0);
        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 9,
            startTS: 0);

        table.Upsert(writer, "n1", new Dictionary<string, object> { ["name"] = "Alicia" });
        table.RollbackVersions(writer);

        Assert.True(table.TryGetProperties(reader, "n1", out var props));
        Assert.Equal("Alice", props!["name"]);
    }

    [Fact]
    public void RelTableData_TransactionalUpsert_OnExistingRow_UsesOverlayUntilCommit()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["since"] = 2020L, ["weight"] = 1L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 234,
            startTS: 0);
        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 10,
            startTS: 0);

        table.Upsert(writer, key, new Dictionary<string, object> { ["since"] = 2021L, ["weight"] = 2L });

        Assert.True(table.TryGetProperties(oldReader, key, out var oldProps));
        Assert.Equal(2020L, oldProps!["since"]);
        Assert.Equal(1L, oldProps["weight"]);

        Assert.True(table.TryGetProperties(writer, key, out var writerProps));
        Assert.Equal(2021L, writerProps!["since"]);
        Assert.Equal(2L, writerProps["weight"]);

        table.CommitVersions(writer, commitTS: 11);

        var committedReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 11,
            startTS: 11);
        Assert.True(table.TryGetProperties(committedReader, key, out var committedProps));
        Assert.Equal(2021L, committedProps!["since"]);
        Assert.Equal(2L, committedProps["weight"]);
    }

    [Fact]
    public void RelTableData_TransactionalUpsert_OnExistingRow_RollsBackCleanly()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["since"] = 2020L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 235,
            startTS: 0);
        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 12,
            startTS: 0);

        table.Upsert(writer, key, new Dictionary<string, object> { ["since"] = 2021L });
        table.RollbackVersions(writer);

        Assert.True(table.TryGetProperties(reader, key, out var props));
        Assert.Equal(2020L, props!["since"]);
    }

    [Fact]
    public void RelTableData_EnumerateOutgoingRows_UsesPendingOverlayForWriter()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["since"] = 2020L, ["weight"] = 1L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 236,
            startTS: 0);
        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 13,
            startTS: 0);

        Assert.True(table.SetProperty(writer, key, "weight", 2L));

        var writerRows = table.EnumerateOutgoingRows("a", writer).ToList();
        Assert.Single(writerRows);
        Assert.Equal(2L, writerRows[0].Value["weight"]);

        var readerRows = table.EnumerateOutgoingRows("a", reader).ToList();
        Assert.Single(readerRows);
        Assert.Equal(1L, readerRows[0].Value["weight"]);
    }

    [Fact]
    public void RelTableData_TryGetRowByIndex_UsesPendingOverlayForWriter()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["since"] = 2020L, ["weight"] = 1L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 237,
            startTS: 0);
        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 14,
            startTS: 0);

        Assert.True(table.SetProperty(writer, key, "weight", 2L));

        Assert.True(table.TryGetRowByIndex(writer, 0, out var writerKey, out var writerProps));
        Assert.Equal(key, writerKey);
        Assert.Equal(2L, writerProps!["weight"]);

        Assert.True(table.TryGetRowByIndex(reader, 0, out var readerKey, out var readerProps));
        Assert.Equal(key, readerKey);
        Assert.Equal(1L, readerProps!["weight"]);
    }

    [Fact]
    public void NodeTableData_CommittedPropertyUpdate_PreservesOlderReaderSnapshot()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });

        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 15,
            startTS: 5);
        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 238,
            startTS: 5);

        Assert.True(table.SetProperty(writer, "n1", "age", 31L));
        table.CommitVersions(writer, commitTS: 10);

        Assert.True(table.TryGetProperties(oldReader, "n1", out var oldProps));
        Assert.Equal(30L, oldProps!["age"]);

        var newReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 16,
            startTS: 10);
        Assert.True(table.TryGetProperties(newReader, "n1", out var newProps));
        Assert.Equal(31L, newProps!["age"]);
    }

    [Fact]
    public void RelTableData_CommittedPropertyUpdate_PreservesOlderReaderSnapshot()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["since"] = 2020L, ["weight"] = 1L });

        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 17,
            startTS: 5);
        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 239,
            startTS: 5);

        Assert.True(table.SetProperty(writer, key, "weight", 2L));
        table.CommitVersions(writer, commitTS: 10);

        Assert.True(table.TryGetProperties(oldReader, key, out var oldProps));
        Assert.Equal(1L, oldProps!["weight"]);

        var newReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 18,
            startTS: 10);
        Assert.True(table.TryGetProperties(newReader, key, out var newProps));
        Assert.Equal(2L, newProps!["weight"]);
    }

    [Fact]
    public void NodeTableData_Update_ConflictsWithCommittedPropertyUpdateAfterTransactionStart()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });

        var staleWriter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 240,
            startTS: 5);
        var fresherWriter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 241,
            startTS: 5);

        Assert.True(table.SetProperty(fresherWriter, "n1", "age", 31L));
        table.CommitVersions(fresherWriter, commitTS: 10);

        var ex = Record.Exception(() => table.SetProperty(staleWriter, "n1", "age", 32L));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex!.Message);
    }

    [Fact]
    public void RelTableData_Delete_ConflictsWithCommittedPropertyUpdateAfterTransactionStart()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["since"] = 2020L, ["weight"] = 1L });

        var staleDeleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 242,
            startTS: 5);
        var fresherWriter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 243,
            startTS: 5);

        Assert.True(table.SetProperty(fresherWriter, key, "weight", 2L));
        table.CommitVersions(fresherWriter, commitTS: 10);

        var ex = Record.Exception(() => table.Remove(staleDeleter, key));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex!.Message);
    }

    [Fact]
    public void NodeTableData_Remove_SwapMovePreservesPendingOverlayForMovedRow()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });
        table.Upsert("n2", new Dictionary<string, object> { ["name"] = "Bob" });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 244,
            startTS: 0);
        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 19,
            startTS: 0);

        Assert.True(table.SetProperty(writer, "n2", "name", "Bobby"));
        Assert.True(table.Remove("n1"));

        Assert.True(table.TryGetProperties(writer, "n2", out var writerProps));
        Assert.Equal("Bobby", writerProps!["name"]);

        Assert.True(table.TryGetProperties(reader, "n2", out var readerProps));
        Assert.Equal("Bob", readerProps!["name"]);
    }

    [Fact]
    public void RelTableData_Remove_SwapMovePreservesPendingOverlayForMovedRow()
    {
        var table = new RelTableData();
        var first = new EdgeKey("a", "b");
        var second = new EdgeKey("a", "c");
        table.Upsert(first, new Dictionary<string, object> { ["weight"] = 1L });
        table.Upsert(second, new Dictionary<string, object> { ["weight"] = 2L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 245,
            startTS: 0);
        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 20,
            startTS: 0);

        Assert.True(table.SetProperty(writer, second, "weight", 3L));
        Assert.True(table.Remove(first));

        Assert.True(table.TryGetProperties(writer, second, out var writerProps));
        Assert.Equal(3L, writerProps!["weight"]);

        Assert.True(table.TryGetProperties(reader, second, out var readerProps));
        Assert.Equal(2L, readerProps!["weight"]);
    }

    [Fact]
    public void NodeTableData_ReinsertAfterCommittedDelete_PreservesSnapshotBoundaries()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 246,
            startTS: 5);
        Assert.True(table.Remove(deleter, "n1"));
        table.CommitVersions(deleter, commitTS: 10);

        var reinserter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 247,
            startTS: 10);
        table.Upsert(reinserter, "n1", new Dictionary<string, object> { ["name"] = "Alicia" });
        table.CommitVersions(reinserter, commitTS: 15);

        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 21,
            startTS: 5);
        var gapReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 22,
            startTS: 12);
        var newReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 23,
            startTS: 15);

        Assert.True(table.TryGetProperties(oldReader, "n1", out var oldProps));
        Assert.Equal("Alice", oldProps!["name"]);

        Assert.False(table.TryGetProperties(gapReader, "n1", out _));

        Assert.True(table.TryGetProperties(newReader, "n1", out var newProps));
        Assert.Equal("Alicia", newProps!["name"]);
    }

    [Fact]
    public void RelTableData_ReinsertAfterCommittedDelete_PreservesSnapshotBoundaries()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["weight"] = 1L });

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 248,
            startTS: 5);
        Assert.True(table.Remove(deleter, key));
        table.CommitVersions(deleter, commitTS: 10);

        var reinserter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 249,
            startTS: 10);
        table.Upsert(reinserter, key, new Dictionary<string, object> { ["weight"] = 2L });
        table.CommitVersions(reinserter, commitTS: 15);

        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 24,
            startTS: 5);
        var gapReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 25,
            startTS: 12);
        var newReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 26,
            startTS: 15);

        Assert.True(table.TryGetProperties(oldReader, key, out var oldProps));
        Assert.Equal(1L, oldProps!["weight"]);

        Assert.False(table.TryGetProperties(gapReader, key, out _));

        Assert.True(table.TryGetProperties(newReader, key, out var newProps));
        Assert.Equal(2L, newProps!["weight"]);
    }

    [Fact]
    public void NodeTableData_DeleteReinsertSameKey_InOneTransaction_CommitsNewRow()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 260,
            startTS: 5);

        Assert.True(table.Remove(writer, "n1"));
        table.Upsert(writer, "n1", new Dictionary<string, object> { ["name"] = "Alicia", ["age"] = 31L });

        Assert.True(table.TryGetProperties(writer, "n1", out var writerProps));
        Assert.Equal("Alicia", writerProps!["name"]);
        Assert.Equal(31L, writerProps["age"]);

        table.CommitVersions(writer, commitTS: 10);

        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 33,
            startTS: 10);
        Assert.True(table.TryGetProperties(reader, "n1", out var readerProps));
        Assert.Equal("Alicia", readerProps!["name"]);
        Assert.Equal(31L, readerProps["age"]);
    }

    [Fact]
    public void RelTableData_DeleteReinsertSameKey_InOneTransaction_CommitsNewRow()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["weight"] = 1L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 261,
            startTS: 5);

        Assert.True(table.Remove(writer, key));
        table.Upsert(writer, key, new Dictionary<string, object> { ["weight"] = 2L });

        Assert.True(table.TryGetProperties(writer, key, out var writerProps));
        Assert.Equal(2L, writerProps!["weight"]);

        table.CommitVersions(writer, commitTS: 10);

        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 34,
            startTS: 10);
        Assert.True(table.TryGetProperties(reader, key, out var readerProps));
        Assert.Equal(2L, readerProps!["weight"]);
    }

    [Fact]
    public void NodeTableData_DeleteReinsertSameKey_InOneTransaction_RollbackRestoresOldRow()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 262,
            startTS: 5);

        Assert.True(table.Remove(writer, "n1"));
        table.Upsert(writer, "n1", new Dictionary<string, object> { ["name"] = "Alicia", ["age"] = 31L });
        table.RollbackVersions(writer);

        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 35,
            startTS: 5);
        Assert.True(table.TryGetProperties(reader, "n1", out var readerProps));
        Assert.Equal("Alice", readerProps!["name"]);
        Assert.Equal(30L, readerProps["age"]);
    }

    [Fact]
    public void RelTableData_DeleteReinsertSameKey_InOneTransaction_RollbackRestoresOldRow()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["weight"] = 1L });

        var writer = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 263,
            startTS: 5);

        Assert.True(table.Remove(writer, key));
        table.Upsert(writer, key, new Dictionary<string, object> { ["weight"] = 2L });
        table.RollbackVersions(writer);

        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 36,
            startTS: 5);
        Assert.True(table.TryGetProperties(reader, key, out var readerProps));
        Assert.Equal(1L, readerProps!["weight"]);
    }

    [Fact]
    public void NodeTableData_Update_OnOldVisibleVersion_ConflictsAfterDeleteReinsert()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice" });

        var staleWriter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 250,
            startTS: 5);

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 251,
            startTS: 5);
        Assert.True(table.Remove(deleter, "n1"));
        table.CommitVersions(deleter, commitTS: 10);

        var reinserter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 252,
            startTS: 10);
        table.Upsert(reinserter, "n1", new Dictionary<string, object> { ["name"] = "Alicia" });
        table.CommitVersions(reinserter, commitTS: 15);

        var ex = Record.Exception(() => table.SetProperty(staleWriter, "n1", "name", "Ally"));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex!.Message);
    }

    [Fact]
    public void RelTableData_Delete_OnOldVisibleVersion_ConflictsAfterDeleteReinsert()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["weight"] = 1L });

        var staleDeleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 253,
            startTS: 5);

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 254,
            startTS: 5);
        Assert.True(table.Remove(deleter, key));
        table.CommitVersions(deleter, commitTS: 10);

        var reinserter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 255,
            startTS: 10);
        table.Upsert(reinserter, key, new Dictionary<string, object> { ["weight"] = 2L });
        table.CommitVersions(reinserter, commitTS: 15);

        var ex = Record.Exception(() => table.Remove(staleDeleter, key));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex!.Message);
    }

    [Fact]
    public void NodeTableData_MultipleCommittedPropertyUpdates_PreserveIntermediateSnapshots()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });

        var firstUpdater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 256,
            startTS: 5);
        Assert.True(table.SetProperty(firstUpdater, "n1", "age", 31L));
        table.CommitVersions(firstUpdater, commitTS: 10);

        var secondUpdater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 257,
            startTS: 10);
        Assert.True(table.SetProperty(secondUpdater, "n1", "age", 32L));
        table.CommitVersions(secondUpdater, commitTS: 15);

        var readerBeforeUpdates = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 27,
            startTS: 5);
        var readerAfterFirstUpdate = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 28,
            startTS: 10);
        var readerAfterSecondUpdate = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 29,
            startTS: 15);

        Assert.True(table.TryGetProperties(readerBeforeUpdates, "n1", out var beforeProps));
        Assert.Equal(30L, beforeProps!["age"]);

        Assert.True(table.TryGetProperties(readerAfterFirstUpdate, "n1", out var firstProps));
        Assert.Equal(31L, firstProps!["age"]);

        Assert.True(table.TryGetProperties(readerAfterSecondUpdate, "n1", out var secondProps));
        Assert.Equal(32L, secondProps!["age"]);
    }

    [Fact]
    public void RelTableData_MultipleCommittedPropertyUpdates_PreserveIntermediateSnapshots()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["weight"] = 1L });

        var firstUpdater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 258,
            startTS: 5);
        Assert.True(table.SetProperty(firstUpdater, key, "weight", 2L));
        table.CommitVersions(firstUpdater, commitTS: 10);

        var secondUpdater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 259,
            startTS: 10);
        Assert.True(table.SetProperty(secondUpdater, key, "weight", 3L));
        table.CommitVersions(secondUpdater, commitTS: 15);

        var readerBeforeUpdates = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 30,
            startTS: 5);
        var readerAfterFirstUpdate = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 31,
            startTS: 10);
        var readerAfterSecondUpdate = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 32,
            startTS: 15);

        Assert.True(table.TryGetProperties(readerBeforeUpdates, key, out var beforeProps));
        Assert.Equal(1L, beforeProps!["weight"]);

        Assert.True(table.TryGetProperties(readerAfterFirstUpdate, key, out var firstProps));
        Assert.Equal(2L, firstProps!["weight"]);

        Assert.True(table.TryGetProperties(readerAfterSecondUpdate, key, out var secondProps));
        Assert.Equal(3L, secondProps!["weight"]);
    }

    [Fact]
    public void NodeTableData_DeleteReinsertThenUpdate_PreservesAllVisibleSnapshots()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 260,
            startTS: 5);
        Assert.True(table.Remove(deleter, "n1"));
        table.CommitVersions(deleter, commitTS: 10);

        var reinserter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 261,
            startTS: 10);
        table.Upsert(reinserter, "n1", new Dictionary<string, object> { ["name"] = "Alicia", ["age"] = 31L });
        table.CommitVersions(reinserter, commitTS: 15);

        var updater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 262,
            startTS: 15);
        Assert.True(table.SetProperty(updater, "n1", "age", 32L));
        table.CommitVersions(updater, commitTS: 20);

        var readerBeforeDelete = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 33,
            startTS: 5);
        var readerAfterReinsert = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 34,
            startTS: 15);
        var readerAfterUpdate = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 35,
            startTS: 20);

        Assert.True(table.TryGetProperties(readerBeforeDelete, "n1", out var beforeDeleteProps));
        Assert.Equal("Alice", beforeDeleteProps!["name"]);
        Assert.Equal(30L, beforeDeleteProps["age"]);

        Assert.True(table.TryGetProperties(readerAfterReinsert, "n1", out var afterReinsertProps));
        Assert.Equal("Alicia", afterReinsertProps!["name"]);
        Assert.Equal(31L, afterReinsertProps["age"]);

        Assert.True(table.TryGetProperties(readerAfterUpdate, "n1", out var afterUpdateProps));
        Assert.Equal("Alicia", afterUpdateProps!["name"]);
        Assert.Equal(32L, afterUpdateProps["age"]);
    }

    [Fact]
    public void RelTableData_DeleteReinsertThenUpdate_PreservesAllVisibleSnapshots()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["weight"] = 1L, ["since"] = 2020L });

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 263,
            startTS: 5);
        Assert.True(table.Remove(deleter, key));
        table.CommitVersions(deleter, commitTS: 10);

        var reinserter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 264,
            startTS: 10);
        table.Upsert(reinserter, key, new Dictionary<string, object> { ["weight"] = 2L, ["since"] = 2024L });
        table.CommitVersions(reinserter, commitTS: 15);

        var updater = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 265,
            startTS: 15);
        Assert.True(table.SetProperty(updater, key, "weight", 3L));
        table.CommitVersions(updater, commitTS: 20);

        var readerBeforeDelete = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 36,
            startTS: 5);
        var readerAfterReinsert = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 37,
            startTS: 15);
        var readerAfterUpdate = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 38,
            startTS: 20);

        Assert.True(table.TryGetProperties(readerBeforeDelete, key, out var beforeDeleteProps));
        Assert.Equal(1L, beforeDeleteProps!["weight"]);
        Assert.Equal(2020L, beforeDeleteProps["since"]);

        Assert.True(table.TryGetProperties(readerAfterReinsert, key, out var afterReinsertProps));
        Assert.Equal(2L, afterReinsertProps!["weight"]);
        Assert.Equal(2024L, afterReinsertProps["since"]);

        Assert.True(table.TryGetProperties(readerAfterUpdate, key, out var afterUpdateProps));
        Assert.Equal(3L, afterUpdateProps!["weight"]);
        Assert.Equal(2024L, afterUpdateProps["since"]);
    }

    [Fact]
    public void NodeTableData_TwoDeleteReinsertCycles_PreserveVisibleSnapshots()
    {
        var table = new NodeTableData();
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });

        var delete1 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 266,
            startTS: 5);
        Assert.True(table.Remove(delete1, "n1"));
        table.CommitVersions(delete1, commitTS: 10);

        var reinsert1 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 267,
            startTS: 10);
        table.Upsert(reinsert1, "n1", new Dictionary<string, object> { ["name"] = "Alicia", ["age"] = 31L });
        table.CommitVersions(reinsert1, commitTS: 15);

        var delete2 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 268,
            startTS: 15);
        Assert.True(table.Remove(delete2, "n1"));
        table.CommitVersions(delete2, commitTS: 20);

        var reinsert2 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 269,
            startTS: 20);
        table.Upsert(reinsert2, "n1", new Dictionary<string, object> { ["name"] = "Alina", ["age"] = 32L });
        table.CommitVersions(reinsert2, commitTS: 25);

        var readerBeforeDelete = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 39,
            startTS: 5);
        var readerAfterFirstReinsert = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 40,
            startTS: 15);
        var readerBetweenCycles = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 41,
            startTS: 20);
        var readerAfterSecondReinsert = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 42,
            startTS: 25);

        Assert.True(table.TryGetProperties(readerBeforeDelete, "n1", out var beforeDeleteProps));
        Assert.Equal("Alice", beforeDeleteProps!["name"]);
        Assert.Equal(30L, beforeDeleteProps["age"]);

        Assert.True(table.TryGetProperties(readerAfterFirstReinsert, "n1", out var afterFirstReinsertProps));
        Assert.Equal("Alicia", afterFirstReinsertProps!["name"]);
        Assert.Equal(31L, afterFirstReinsertProps["age"]);

        Assert.False(table.TryGetProperties(readerBetweenCycles, "n1", out _));

        Assert.True(table.TryGetProperties(readerAfterSecondReinsert, "n1", out var afterSecondReinsertProps));
        Assert.Equal("Alina", afterSecondReinsertProps!["name"]);
        Assert.Equal(32L, afterSecondReinsertProps["age"]);
    }

    [Fact]
    public void RelTableData_TwoDeleteReinsertCycles_PreserveVisibleSnapshots()
    {
        var table = new RelTableData();
        var key = new EdgeKey("a", "b");
        table.Upsert(key, new Dictionary<string, object> { ["weight"] = 1L, ["since"] = 2020L });

        var delete1 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 270,
            startTS: 5);
        Assert.True(table.Remove(delete1, key));
        table.CommitVersions(delete1, commitTS: 10);

        var reinsert1 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 271,
            startTS: 10);
        table.Upsert(reinsert1, key, new Dictionary<string, object> { ["weight"] = 2L, ["since"] = 2024L });
        table.CommitVersions(reinsert1, commitTS: 15);

        var delete2 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 272,
            startTS: 15);
        Assert.True(table.Remove(delete2, key));
        table.CommitVersions(delete2, commitTS: 20);

        var reinsert2 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 273,
            startTS: 20);
        table.Upsert(reinsert2, key, new Dictionary<string, object> { ["weight"] = 3L, ["since"] = 2025L });
        table.CommitVersions(reinsert2, commitTS: 25);

        var readerBeforeDelete = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 43,
            startTS: 5);
        var readerAfterFirstReinsert = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 44,
            startTS: 15);
        var readerBetweenCycles = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 45,
            startTS: 20);
        var readerAfterSecondReinsert = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 46,
            startTS: 25);

        Assert.True(table.TryGetProperties(readerBeforeDelete, key, out var beforeDeleteProps));
        Assert.Equal(1L, beforeDeleteProps!["weight"]);
        Assert.Equal(2020L, beforeDeleteProps["since"]);

        Assert.True(table.TryGetProperties(readerAfterFirstReinsert, key, out var afterFirstReinsertProps));
        Assert.Equal(2L, afterFirstReinsertProps!["weight"]);
        Assert.Equal(2024L, afterFirstReinsertProps["since"]);

        Assert.False(table.TryGetProperties(readerBetweenCycles, key, out _));

        Assert.True(table.TryGetProperties(readerAfterSecondReinsert, key, out var afterSecondReinsertProps));
        Assert.Equal(3L, afterSecondReinsertProps!["weight"]);
        Assert.Equal(2025L, afterSecondReinsertProps["since"]);
    }

    [Fact]
    public void NodeTableData_SwapCompactionAfterReinsertHistory_PreservesLatestVisibleRow()
    {
        var table = new NodeTableData();
        table.Upsert("other", new Dictionary<string, object> { ["name"] = "Other", ["age"] = 1L });
        table.Upsert("n1", new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 274,
            startTS: 5);
        Assert.True(table.Remove(deleter, "n1"));
        table.CommitVersions(deleter, commitTS: 10);

        var reinserter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 275,
            startTS: 10);
        table.Upsert(reinserter, "n1", new Dictionary<string, object> { ["name"] = "Alicia", ["age"] = 31L });
        table.CommitVersions(reinserter, commitTS: 15);

        Assert.True(table.Remove("other"));

        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 47,
            startTS: 15);
        Assert.True(table.TryGetProperties(reader, "n1", out var props));
        Assert.Equal("Alicia", props!["name"]);
        Assert.Equal(31L, props["age"]);
    }

    [Fact]
    public void RelTableData_SwapCompactionAfterReinsertHistory_PreservesLatestVisibleRow()
    {
        var table = new RelTableData();
        var other = new EdgeKey("x", "y");
        var key = new EdgeKey("a", "b");
        table.Upsert(other, new Dictionary<string, object> { ["weight"] = 9L });
        table.Upsert(key, new Dictionary<string, object> { ["weight"] = 1L, ["since"] = 2020L });

        var deleter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 276,
            startTS: 5);
        Assert.True(table.Remove(deleter, key));
        table.CommitVersions(deleter, commitTS: 10);

        var reinserter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 277,
            startTS: 10);
        table.Upsert(reinserter, key, new Dictionary<string, object> { ["weight"] = 2L, ["since"] = 2024L });
        table.CommitVersions(reinserter, commitTS: 15);

        Assert.True(table.Remove(other));

        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 48,
            startTS: 15);
        Assert.True(table.TryGetProperties(reader, key, out var props));
        Assert.Equal(2L, props!["weight"]);
        Assert.Equal(2024L, props["since"]);
    }
}
