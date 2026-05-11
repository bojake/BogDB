using System;
using System.IO;
using Xunit;
using BogDb.Core.Transaction;

namespace BogDb.Tests.Storage;

/// <summary>
/// Unit tests for the logical WAL serialization / replay.
/// Verifies that WAL records round-trip through serialize → deserialize,
/// and that the dry-run scanner correctly identifies COMMIT and CHECKPOINT boundaries.
/// </summary>
public class WALTests : IDisposable
{
    private readonly string _dbPath;

    public WALTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bogdb_wal_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_dbPath);
    }

    [Fact]
    public void WAL_LogsBeginAndCommit_AndDryReplayFindsCommitBoundary()
    {
        using (var wal = new WAL(_dbPath, readOnly: false, inMemory: false))
        {
            wal.LogBeginTransaction();
            var tx = new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 100, 10);
            wal.LogAndFlushCommit(tx);
        }

        var replayer = new WALReplayer(_dbPath);
        var info = replayer.DryReplay();

        Assert.True(info.LastValidOffset > 0);
        Assert.False(info.IsLastRecordCheckpoint);

        var records = replayer.Replay();
        Assert.Equal(2, records.Count);
        Assert.IsType<BeginTransactionWALRecord>(records[0]);
        Assert.IsType<CommitWALRecord>(records[1]);
    }

    [Fact]
    public void WAL_CheckpointRecord_IsDetectedByDryReplay()
    {
        using (var wal = new WAL(_dbPath, readOnly: false, inMemory: false))
        {
            wal.LogBeginTransaction();
            wal.LogAndFlushCommit(new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 1, 1));
            wal.LogAndFlushCheckpoint();
        }

        var replayer = new WALReplayer(_dbPath);
        var info = replayer.DryReplay();

        Assert.True(info.LastValidOffset > 0);
        Assert.True(info.IsLastRecordCheckpoint);
    }

    [Fact]
    public void WAL_CorruptedTail_TruncatesBackToLastValidCommit()
    {
        using (var wal = new WAL(_dbPath, readOnly: false, inMemory: false))
        {
            wal.LogBeginTransaction();
            wal.LogAndFlushCommit(new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 1, 1));
        }

        var walPath = Path.Combine(_dbPath, "data.wal");
        long validSize = new FileInfo(walPath).Length;

        // Append garbage bytes after the valid end
        using (var fs = new FileStream(walPath, FileMode.Append, FileAccess.Write))
        {
            fs.Write(new byte[] { 0xFF, 0xEE, 0xDD, 0xCC, 0xBB });
        }

        var replayer = new WALReplayer(_dbPath);
        var records = replayer.Replay();

        // Should recover only the 2 valid records (BEGIN + COMMIT) and truncate the garbage
        Assert.Equal(2, records.Count);
        Assert.Equal(validSize, new FileInfo(walPath).Length);
    }

    [Fact]
    public void WAL_RecordSerialization_RoundTrips_AllLogicalTypes()
    {
        using (var wal = new WAL(_dbPath, readOnly: false, inMemory: false))
        {
            wal.LogBeginTransaction();
            wal.LogTableInsertion(1, 0, new[] { "id", "name" },
                new() { new() { "p1", "Alice" } });
            wal.LogNodeDeletion(1, 42, "p1");
            wal.LogNodeUpdate(1, 2, 42, "new-value");
            wal.LogRelDeletion(2, 10, 20, 30);
            wal.LogRelUpdate(2, 1, 10, 20, 30, "updated-prop");
            wal.LogDropTableRecord(99);
            wal.LogUpdateSequence(5, 100);
            wal.LogCopyTable(7);
            wal.LogLoadExtension("/path/to/ext.dll");
            wal.LogAndFlushCommit(new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 1, 1));
        }

        var replayer = new WALReplayer(_dbPath);
        var records = replayer.Replay();

        // BEGIN + 9 records + COMMIT = 11
        Assert.Equal(11, records.Count);
        Assert.IsType<BeginTransactionWALRecord>(records[0]);
        Assert.IsType<TableInsertionWALRecord>(records[1]);
        Assert.IsType<NodeDeletionWALRecord>(records[2]);
        Assert.IsType<NodeUpdateWALRecord>(records[3]);
        Assert.IsType<RelDeletionWALRecord>(records[4]);
        Assert.IsType<RelUpdateWALRecord>(records[5]);
        Assert.IsType<DropCatalogEntryWALRecord>(records[6]);
        Assert.IsType<UpdateSequenceWALRecord>(records[7]);
        Assert.IsType<CopyTableWALRecord>(records[8]);
        Assert.IsType<LoadExtensionWALRecord>(records[9]);
        Assert.IsType<CommitWALRecord>(records[10]);

        // Verify a couple of payloads survived the round-trip
        var insertion = (TableInsertionWALRecord)records[1];
        Assert.Equal(1u, insertion.TableId);
        Assert.Equal(new[] { "id", "name" }, insertion.ColumnNames);
        Assert.Single(insertion.Rows);
        Assert.Equal("p1", insertion.Rows[0][0]);
        Assert.Equal("Alice", insertion.Rows[0][1]);

        var deletion = (NodeDeletionWALRecord)records[2];
        Assert.Equal(42L, deletion.NodeOffset);
        Assert.Equal("p1", deletion.PrimaryKeyValue);

        var ext = (LoadExtensionWALRecord)records[9];
        Assert.Equal("/path/to/ext.dll", ext.Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dbPath))
        {
            Directory.Delete(_dbPath, recursive: true);
        }
    }
}
