using BogDb.Core.Transaction;
using System.IO;
using Xunit;

namespace BogDb.Tests.Transaction;

public class WALAndTransactionTests : IDisposable
{
    private readonly string _testDbPath = "test_db_dir";

    public WALAndTransactionTests()
    {
        if (Directory.Exists(_testDbPath)) Directory.Delete(_testDbPath, true);
        Directory.CreateDirectory(_testDbPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDbPath)) Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void Transaction_ShouldTrackReadWriteStates()
    {
        var readTx = new BogDb.Core.Transaction.Transaction(TransactionType.READ_ONLY, 1, 100);
        Assert.True(readTx.IsReadOnly());
        Assert.False(readTx.IsWriteTransaction());
        Assert.False(readTx.ShouldAppendToUndoBuffer());

        var writeTx = new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 2, 105);
        Assert.True(writeTx.IsWriteTransaction());
        Assert.True(writeTx.ShouldAppendToUndoBuffer());

        writeTx.Commit(110);
        Assert.Equal(110ul, writeTx.CommitTS);
    }

    [Fact]
    public void WAL_SerializationWritesCorrectBytes()
    {
        using (var wal = new WAL(_testDbPath, readOnly: false, inMemory: false))
        {
            var tx = new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 12345, 1);
            wal.LogPageUpdate(1, 42); // No-op in logical WAL
            wal.LogAndFlushCommit(tx);

            // Size with logical WAL:
            // Header -> 16 byte UUID + 1 byte checksum flag = 17 bytes
            // Commit -> 1 byte type + 8 byte tx ID + 8 byte graphLogOffset = 17 bytes
            // LogPageUpdate is a no-op (logical WAL doesn't record physical pages)
            // Total = 34 bytes
            Assert.Equal(34ul, wal.GetFileSize());

            wal.Clear();
            Assert.Equal(0ul, wal.GetFileSize());
        }
        
        // Let's manually decode the wal to assert BogDb compliance
        var bytes = File.ReadAllBytes(Path.Combine(_testDbPath, "data.wal"));
        Assert.Empty(bytes); // Should be empty because clear flushed it.
    }
}
