using BogDb.Core.Storage.Table;
using Xunit;

namespace BogDb.Tests.Storage.Table;

public class UpdateInfoTests
{
    [Fact]
    public void UpdateInfo_DetectsWriteWriteConflict()
    {
        var updates = new UpdateInfo();
        var tx1 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 1,
            startTS: 0);
        var tx2 = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 2,
            startTS: 0);

        updates.Update(tx1, rowOffset: 3, value: 42L);

        var ex = Record.Exception(() =>
            updates.Update(tx2, rowOffset: 3, value: 99L));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Write-write conflict", ex.Message);
    }

    [Fact]
    public void Column_UpdateInfo_RespectsVisibilityCommitAndRollback()
    {
        var col = new Column("age", chunkCapacity: 4);
        col.Append(10L);
        col.Append(20L);

        var txWriter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 10,
            startTS: 0);
        var txOldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 11,
            startTS: 0);

        col.Update(txWriter, rowOffset: 0, value: 15L);
        Assert.Equal(15L, col.Lookup(txWriter, 0));
        Assert.Equal(10L, col.Lookup(txOldReader, 0));

        col.CommitUpdates(txWriter, commitTS: 5);

        var txNewReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 12,
            startTS: 5);
        Assert.Equal(10L, col.Lookup(txOldReader, 0));
        Assert.Equal(15L, col.Lookup(txNewReader, 0));

        var txRollback = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 20,
            startTS: 5);
        col.Update(txRollback, rowOffset: 1, value: 99L);
        Assert.Equal(99L, col.Lookup(txRollback, 1));
        col.RollbackUpdates(txRollback);
        Assert.Equal(20L, col.Lookup(txNewReader, 1));
    }

    [Fact]
    public void Column_Truncate_DropsVersionedUpdatesForReusedRowOffsets()
    {
        var col = new Column("age", chunkCapacity: 4);
        col.Append(10L);
        col.Append(20L);

        var txWriter = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.WRITE,
            BogDb.Core.Transaction.Transaction.START_TRANSACTION_ID + 30,
            startTS: 0);

        col.Update(txWriter, rowOffset: 1, value: 25L);
        col.CommitUpdates(txWriter, commitTS: 5);

        col.Truncate(1);
        col.Append(30L);

        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 13,
            startTS: 10);

        Assert.Equal(30L, col.Lookup(reader, 1));
    }
}
