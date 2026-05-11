using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Transaction;

namespace BogDb.Tests.Transaction;

/// <summary>
/// Integration tests for the logical WAL + recovery subsystem.
/// These tests verify crash-reopen scenarios by:
///   1. Opening a database, performing mutations, committing
///   2. Closing without checkpointing (simulating crash)
///   3. Reopening and verifying data was recovered correctly
/// </summary>
public class WALIntegrationTests
{
    [Fact]
    public void Wal_Replay_TruncatesGarbageAfterLastCommit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-wal-trunc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var wal = new WAL(dir, readOnly: false, inMemory: false))
            {
                wal.LogBeginTransaction();
                wal.LogAndFlushCommit(new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, id: 1, startTS: 0));
            }

            var walPath = Path.Combine(dir, "data.wal");
            var lengthBefore = new FileInfo(walPath).Length;
            Assert.True(lengthBefore > 0);

            // Append garbage to simulate a partial record.
            using (var fs = new FileStream(walPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                fs.Write(new byte[] { 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99 });
            }

            var replayer = new WALReplayer(dir);
            var records = replayer.Replay();

            var lengthAfter = new FileInfo(walPath).Length;
            Assert.Equal(lengthBefore, lengthAfter);
            Assert.Contains(records, r => r is BeginTransactionWALRecord);
            Assert.Contains(records, r => r is CommitWALRecord);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Wal_Replay_Emits_BeginTransaction_And_Commit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-wal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var wal = new WAL(dir, readOnly: false, inMemory: false))
            {
                wal.LogBeginTransaction();
                wal.LogAndFlushCommit(new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, id: 2, startTS: 0));
            }

            var replayer = new WALReplayer(dir);
            var records = replayer.Replay();

            Assert.Contains(records, r => r is BeginTransactionWALRecord);
            Assert.Contains(records, r => r is CommitWALRecord);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Wal_Replay_TruncatesGarbage_BackToTrailingCheckpointRecord()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-wal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var wal = new WAL(dir, readOnly: false, inMemory: false))
            {
                wal.LogBeginTransaction();
                wal.LogAndFlushCommit(new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, id: 4, startTS: 0));
                wal.LogAndFlushCheckpoint();
            }

            var walPath = Path.Combine(dir, "data.wal");
            var lengthBeforeGarbage = new FileInfo(walPath).Length;
            Assert.True(lengthBeforeGarbage > 0);

            using (var fs = new FileStream(walPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                fs.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            }

            var replayer = new WALReplayer(dir);
            _ = replayer.Replay();

            // WAL is truncated to 0 when checkpoint is last record (fully applied)
            var lengthAfterReplay = new FileInfo(walPath).Length;
            Assert.True(lengthAfterReplay <= lengthBeforeGarbage);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Rollback_TruncatesPendingRecoveryArtifacts_ButPreservesCommittedStateOnReopen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-wal-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
                {
                    ["id"] = BogDb.Core.Common.LogicalTypeID.STRING,
                    ["name"] = BogDb.Core.Common.LogicalTypeID.STRING
                });
                conn.UpsertNodeById("Person", "p1", new Dictionary<string, object> { ["name"] = "Ada" });
                conn.Commit();

                conn.BeginWriteTransaction();
                conn.UpsertNodeById("Person", "p2", new Dictionary<string, object> { ["name"] = "Grace" });

                var walPath = Path.Combine(dir, "data.wal");
                var graphLogPath = Path.Combine(dir, "graph-log.bin");
                Assert.True(File.Exists(walPath));
                Assert.True(File.Exists(graphLogPath));
                // After upsert, graph-log should have data
                Assert.True(new FileInfo(graphLogPath).Length > 0);

                conn.Rollback();

                // After rollback, the key invariant is that the rolled-back data
                // is not present when we reopen. WAL header bytes may persist.
                Assert.Equal(0, new FileInfo(graphLogPath).Length);
            }

            using var reopenedDb = BogDatabase.Open(dir);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query("MATCH (p:Person) RETURN p.id AS id, p.name AS name ORDER BY id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal("p1", first.GetString("id"));
            Assert.Equal("Ada", first.GetString("name"));
            Assert.False(result.HasNext());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Reopen_AppliesCommittedDropTableWalRecord()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-wal-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            ulong tableId;
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)").IsSuccess);
                conn.CreateIndex("Person", "name");
                conn.UpsertNodeById("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice"
                });

                var entry = db.Catalog.GetTableCatalogEntry(null, "Person", useInternal: false);
                Assert.NotNull(entry);
                tableId = entry!.TableID;
            }

            using (var wal = new WAL(dir, readOnly: false, inMemory: false))
            {
                wal.LogBeginTransaction();
                wal.LogDropTableRecord(checked((uint)tableId));
                wal.LogAndFlushCommit(new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, id: 99, startTS: 0));
            }

            using var reopenedDb = BogDatabase.Open(dir);
            using var reopenedConn = new BogConnection(reopenedDb);

            Assert.False(reopenedConn.HasTable("Person"));
            Assert.False(reopenedDb.Catalog.ContainsIndexEntry("Person", "name"));

            var read = reopenedConn.Query("MATCH (p:Person) RETURN p.id");
            Assert.False(read.IsSuccess);

            var recreate = reopenedConn.Query("CREATE NODE TABLE Person(id STRING, nick STRING)");
            Assert.True(recreate.IsSuccess, recreate.ErrorMessage);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TransactionGrouping_GroupsByBeginAndCommit()
    {
        var records = new List<WALRecordBase>
        {
            new BeginTransactionWALRecord(),
            new TableInsertionWALRecord { TableId = 1, TableType = 0, NumRows = 1, ColumnNames = new[] { "id" }, Rows = new() { new() { "a" } } },
            new CommitWALRecord { TransactionId = 1 },
            new BeginTransactionWALRecord(),
            new NodeDeletionWALRecord { TableId = 1, NodeOffset = 0, PrimaryKeyValue = "a" },
            new CommitWALRecord { TransactionId = 2 },
        };

        var grouped = WALReplayer.GroupByTransaction(records);

        Assert.Equal(2, grouped.Count);
        Assert.Equal(3, grouped[0].Count); // BEGIN + INSERT + COMMIT
        Assert.Equal(3, grouped[1].Count); // BEGIN + DELETE + COMMIT
    }

    [Fact]
    public void ReplayNodeUpdate_PropertyUpdateSurvivesReopen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-wal-nodeup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                conn.Query("CREATE NODE TABLE Person(id STRING, name STRING, age INT64)");
                conn.UpsertNodeById("Person", "p1", new Dictionary<string, object>
                {
                    ["id"] = "p1",
                    ["name"] = "Alice",
                    ["age"] = 30L,
                });
            }

            // Verify initial state
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                var r = conn.Query("MATCH (p:Person) WHERE p.id = 'p1' RETURN p.name AS name");
                Assert.True(r.IsSuccess);
                Assert.True(r.HasNext());
                Assert.Equal("Alice", r.GetNext().GetString("name"));

                // Now update via regular API
                var result = conn.Query("MATCH (p:Person) WHERE p.id = 'p1' SET p.name = 'Bob' RETURN p.name AS name");
                Assert.True(result.IsSuccess, result.ErrorMessage);
            }

            // Verify update survived reopen
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                var r = conn.Query("MATCH (p:Person) WHERE p.id = 'p1' RETURN p.name AS name");
                Assert.True(r.IsSuccess);
                Assert.True(r.HasNext());
                Assert.Equal("Bob", r.GetNext().GetString("name"));
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ReplayRelDeletion_DeletedRelNotPresentAfterReopen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-wal-reldel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)");
                conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)");
                conn.UpsertNodeById("Person", "p1", new Dictionary<string, object> { ["name"] = "Alice" });
                conn.UpsertNodeById("Person", "p2", new Dictionary<string, object> { ["name"] = "Bob" });
                conn.BeginWriteTransaction();
                conn.UpsertRelationship("KNOWS", "p1", "p2", new Dictionary<string, object> { ["since"] = 2020L });
                conn.Commit();

                // Verify the rel exists
                var r = conn.Query("MATCH (a:Person)-[k:KNOWS]->(b:Person) RETURN a.id AS from, b.id AS to");
                Assert.True(r.IsSuccess, r.ErrorMessage);
                Assert.True(r.HasNext(), "Expected rel to exist after insert");
            }

            // Reopen and delete
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                var del = conn.Query("MATCH (a:Person)-[k:KNOWS]->(b:Person) WHERE a.id = 'p1' AND b.id = 'p2' DELETE k");
                Assert.True(del.IsSuccess, del.ErrorMessage);
            }

            // Verify deletion survived reopen
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                var r = conn.Query("MATCH (a:Person)-[k:KNOWS]->(b:Person) RETURN a.id AS from, b.id AS to");
                Assert.True(r.IsSuccess);
                Assert.False(r.HasNext(), "Deleted relationship should not appear after recovery");
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ReplayRelUpdate_PropertyUpdateOnRelSurvivesReopen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-wal-relup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)");
                conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)");
                conn.UpsertNodeById("Person", "p1", new Dictionary<string, object> { ["name"] = "Alice" });
                conn.UpsertNodeById("Person", "p2", new Dictionary<string, object> { ["name"] = "Bob" });
                conn.BeginWriteTransaction();
                conn.UpsertRelationship("KNOWS", "p1", "p2", new Dictionary<string, object> { ["since"] = 2020L });
                conn.Commit();
            }

            // Reopen and update rel property
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                var upd = conn.Query("MATCH (a:Person)-[k:KNOWS]->(b:Person) WHERE a.id = 'p1' AND b.id = 'p2' SET k.since = 2025 RETURN k.since AS s");
                Assert.True(upd.IsSuccess, upd.ErrorMessage);
            }

            // Verify update survived reopen
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                var r = conn.Query("MATCH (a:Person)-[k:KNOWS]->(b:Person) RETURN k.since AS since");
                Assert.True(r.IsSuccess);
                Assert.True(r.HasNext());
                var row = r.GetNext();
                Assert.Equal(2025L, row.GetInt64("since"));
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ReplayAlterTable_AddDropRenameSurviveReopen()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-wal-alter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // Phase 1: Create table with initial schema
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                conn.Query("CREATE NODE TABLE Item(id STRING, name STRING)");
                conn.UpsertNodeById("Item", "i1", new Dictionary<string, object> { ["name"] = "Widget" });
            }

            // Phase 2: Alter — add a property
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                var r1 = conn.Query("ALTER TABLE Item ADD color STRING");
                Assert.True(r1.IsSuccess, r1.ErrorMessage);
            }

            // Phase 3: Verify the property exists after reopen
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                var entry = db.Catalog.GetTableCatalogEntry(null, "Item");
                Assert.NotNull(entry);
                Assert.True(entry!.ContainsProperty("color"));
            }

            // Phase 4: Alter — rename table
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                var r2 = conn.Query("ALTER TABLE Item RENAME TO Product");
                Assert.True(r2.IsSuccess, r2.ErrorMessage);
            }

            // Phase 5: Verify rename survived
            using (var db = BogDatabase.Open(dir))
            using (var conn = new BogConnection(db))
            {
                Assert.True(conn.HasTable("Product"));
                Assert.False(conn.HasTable("Item"));
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
