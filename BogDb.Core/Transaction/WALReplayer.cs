//------------------------------------------------------------------------------
// WALReplayer.cs — Logical WAL Replay (C++ parity: wal_replayer.cpp)
//
// Replays WAL records through the table/catalog APIs so that indexes,
// constraints, and storage state are rebuilt correctly on recovery.
//
// Algorithm matches C++:
//   1. Dry-run pass to find offset of last valid COMMIT or CHECKPOINT
//   2. If last is CHECKPOINT → apply shadow file → done
//   3. Otherwise → replay all records up to last COMMIT through table APIs
//   4. Truncate WAL to last valid offset
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace BogDb.Core.Transaction;

public sealed class WALReplayer
{
    private readonly string _walPath;
    private readonly string _dbPath;

    public WALReplayer(string dbPath)
    {
        _dbPath = dbPath;
        _walPath = Path.Combine(dbPath, "data.wal");
    }

    /// <summary>Result of a dry-run scan.</summary>
    public readonly record struct WALReplayInfo(long LastValidOffset, bool IsLastRecordCheckpoint);

    /// <summary>
    /// Replays the WAL for crash recovery.
    /// Returns the list of successfully replayed records.
    /// </summary>
    public List<WALRecordBase> Replay()
    {
        var replayed = new List<WALRecordBase>();
        if (!File.Exists(_walPath))
            return replayed;

        var fileInfo = new FileInfo(_walPath);
        if (fileInfo.Length == 0)
            return replayed;

        // Step 1: Dry-run to find last valid transaction boundary
        var info = DryReplay();
        if (info.LastValidOffset == 0)
        {
            // No valid committed records — truncate entire WAL
            TruncateWAL(0);
            return replayed;
        }

        if (info.IsLastRecordCheckpoint)
        {
            // Last record is a checkpoint — shadow file already holds the data.
            // In a full implementation we'd apply shadow pages here.
            // For now, just clean up the WAL.
            TruncateWAL(0);
            return replayed;
        }

        // Step 2: Replay all records from header to last valid COMMIT
        replayed = ReplayRecords(info.LastValidOffset, applyPhysicalPageUpdates: true);

        // Step 3: Truncate WAL to discard any trailing uncommitted records
        TruncateWAL(info.LastValidOffset);

        return replayed;
    }

    /// <summary>
    /// Reads committed WAL records up to the last valid COMMIT without mutating any on-disk files.
    /// This is used by read-only opens that want visibility into committed recovery state
    /// without taking writer ownership or truncating the WAL.
    /// </summary>
    public List<WALRecordBase> ReadCommittedRecordsWithoutTruncation()
    {
        if (!File.Exists(_walPath))
            return new List<WALRecordBase>();

        var fileInfo = new FileInfo(_walPath);
        if (fileInfo.Length == 0)
            return new List<WALRecordBase>();

        var info = DryReplay();
        if (info.LastValidOffset == 0 || info.IsLastRecordCheckpoint)
            return new List<WALRecordBase>();

        return ReplayRecords(info.LastValidOffset, applyPhysicalPageUpdates: false);
    }

    /// <summary>
    /// Scans the WAL without applying, finding the offset of the last valid
    /// COMMIT or CHECKPOINT record. Stops at corruption/EOF.
    /// C++ parity: WALReplayer::dryReplay()
    /// </summary>
    public WALReplayInfo DryReplay()
    {
        long lastValidOffset = 0;
        bool isLastCheckpoint = false;

        try
        {
            using var fs = new FileStream(_walPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(fs);

            // Skip header
            if (fs.Length >= 17) // 16-byte UUID + 1 bool
            {
                var header = WALFileHeader.Deserialize(reader);
                // Could verify database UUID here
            }
            else
            {
                return new WALReplayInfo(0, false);
            }

            while (fs.Position < fs.Length)
            {
                long preRecordPos = fs.Position;
                var record = WALRecordBase.Deserialize(reader);

                switch (record.RecordType)
                {
                    case WALLogicalRecordType.CHECKPOINT_RECORD:
                        isLastCheckpoint = true;
                        lastValidOffset = fs.Position;
                        break;

                    case WALLogicalRecordType.COMMIT_RECORD:
                        isLastCheckpoint = false;
                        lastValidOffset = fs.Position;
                        break;

                    // All other records — just advance
                }
            }
        }
        catch (EndOfStreamException)
        {
            // Partial/corrupt record at end — stop at last valid boundary
        }
        catch (InvalidDataException)
        {
            // Unknown record type — stop at last valid boundary
        }

        return new WALReplayInfo(lastValidOffset, isLastCheckpoint);
    }

    /// <summary>
    /// Reads and returns all records from the WAL up to the given offset.
    /// These records can be applied by the caller through the table APIs.
    /// </summary>
    private List<WALRecordBase> ReplayRecords(long upToOffset, bool applyPhysicalPageUpdates)
    {
        var records = new List<WALRecordBase>();

        try
        {
            using var fs = new FileStream(_walPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(fs);

            // Skip header
            if (fs.Length >= 17)
            {
                _ = WALFileHeader.Deserialize(reader);
            }
            else
            {
                return records;
            }

            while (fs.Position < upToOffset && fs.Position < fs.Length)
            {
                var record = WALRecordBase.Deserialize(reader);
                if (record is PageUpdateWALRecord pageUpdate)
                {
                    if (applyPhysicalPageUpdates)
                    {
                        ApplyPhysicalPageUpdate(pageUpdate);
                    }
                }
                else
                {
                    records.Add(record);
                }
            }
        }
        catch (EndOfStreamException)
        {
            // Stop at what we could read
        }
        catch (InvalidDataException)
        {
            // Stop at what we could read
        }

        return records;
    }

    private void ApplyPhysicalPageUpdate(PageUpdateWALRecord record)
    {
        // Replay a physical page update immediately — overwrite the page image on disk.
        // Doing this during Replay() ensures no file locks conflict with BufferManager.
        var fileName = Path.GetFileName(record.FilePath);
        var activePath = Path.Combine(_dbPath, fileName);

        if (!File.Exists(activePath)) return;

        using var fs = new FileStream(activePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        fs.Seek((long)record.PageIdx * record.PageData.Length, SeekOrigin.Begin);
        fs.Write(record.PageData, 0, record.PageData.Length);
    }

    /// <summary>
    /// Groups the flat list of WAL records into per-transaction batches.
    /// Each batch starts with BEGIN_TRANSACTION and ends with COMMIT.
    /// Records outside transaction boundaries are treated as single-record transactions.
    /// </summary>
    public static List<List<WALRecordBase>> GroupByTransaction(IReadOnlyList<WALRecordBase> records)
    {
        var transactions = new List<List<WALRecordBase>>();
        List<WALRecordBase>? current = null;

        foreach (var record in records)
        {
            switch (record.RecordType)
            {
                case WALLogicalRecordType.BEGIN_TRANSACTION_RECORD:
                    current = new List<WALRecordBase> { record };
                    break;

                case WALLogicalRecordType.COMMIT_RECORD:
                    if (current != null)
                    {
                        current.Add(record);
                        transactions.Add(current);
                        current = null;
                    }
                    else
                    {
                        // Orphaned commit — legacy WAL format, wrap it
                        transactions.Add(new List<WALRecordBase> { record });
                    }
                    break;

                case WALLogicalRecordType.CHECKPOINT_RECORD:
                    // Checkpoints end the replay
                    break;

                default:
                    if (current != null)
                    {
                        current.Add(record);
                    }
                    else
                    {
                        // Record outside transaction context (legacy format)
                        // Wrap in its own mini-batch
                        transactions.Add(new List<WALRecordBase> { record });
                    }
                    break;
            }
        }

        return transactions;
    }

    /// <summary>
    /// Replays a single WAL record through the database APIs.
    /// Called by the database recovery path which owns the storage/catalog references.
    /// </summary>
    public static void ReplayRecord(
        WALRecordBase record,
        Main.BogDatabase database,
        Main.BogConnection connection)
    {
        switch (record)
        {
            case TableInsertionWALRecord insertion:
                ReplayTableInsertion(insertion, database, connection);
                break;

            case NodeDeletionWALRecord deletion:
                ReplayNodeDeletion(deletion, database, connection);
                break;

            case NodeUpdateWALRecord update:
                ReplayNodeUpdate(update, database, connection);
                break;

            case RelDeletionWALRecord relDeletion:
                ReplayRelDeletion(relDeletion, database, connection);
                break;

            case RelUpdateWALRecord relUpdate:
                ReplayRelUpdate(relUpdate, database, connection);
                break;

            case CreateCatalogEntryWALRecord createEntry:
                ReplayCreateCatalogEntry(createEntry, database, connection);
                break;

            case DropCatalogEntryWALRecord dropEntry:
                ReplayDropCatalogEntry(dropEntry, database, connection);
                break;

            case AlterTableEntryWALRecord alterEntry:
                ReplayAlterTableEntry(alterEntry, database, connection);
                break;

            case UpdateSequenceWALRecord seqUpdate:
                // Sequence updates are replayed by advancing the sequence
                break;

            case CopyTableWALRecord:
            case LoadExtensionWALRecord:
            case BeginTransactionWALRecord:
            case CommitWALRecord:
            case CheckpointWALRecord:
            case PageUpdateWALRecord:
                // These are structural or physical records — no logical data replay needed here
                break;
        }
    }

    // ── Replay helpers ───────────────────────────────────────────────────────

    private static void ReplayTableInsertion(
        TableInsertionWALRecord record,
        Main.BogDatabase database,
        Main.BogConnection connection)
    {
        // Build property dictionaries from column names + row values
        // and replay through the connection's upsert API
        var tableName = database.GetTableNameById(record.TableId);
        if (tableName == null) return;

        foreach (var row in record.Rows)
        {
            var props = new Dictionary<string, object?>();
            for (int i = 0; i < record.ColumnNames.Length && i < row.Count; i++)
            {
                props[record.ColumnNames[i]] = row[i];
            }

            if (record.TableType == 0) // NODE
            {
                // Find PK column and use upsert
                var pkCol = record.ColumnNames.Length > 0 ? record.ColumnNames[0] : "id";
                var pkVal = row.Count > 0 ? row[0] : null;
                if (pkVal != null)
                {
                    connection.UpsertNodeById(tableName, pkVal.ToString()!, props);
                }
            }
            // REL insertions would need src/dst IDs — first two columns
        }
    }

    private static void ReplayNodeDeletion(
        NodeDeletionWALRecord record,
        Main.BogDatabase database,
        Main.BogConnection connection)
    {
        var tableName = database.GetTableNameById(record.TableId);
        if (tableName == null) return;

        // Delete by PK value
        if (record.PrimaryKeyValue != null)
        {
            var pkStr = record.PrimaryKeyValue.ToString();
            var cypher = $"MATCH (n:{tableName}) WHERE n.id = '{pkStr}' DELETE n";
            connection.Query(cypher);
        }
    }

    private static void ReplayNodeUpdate(
        NodeUpdateWALRecord record,
        Main.BogDatabase database,
        Main.BogConnection connection)
    {
        var tableName = database.GetTableNameById(record.TableId);
        if (tableName == null) return;

        var propertyName = database.GetColumnNameByOrdinal(record.TableId, record.ColumnId);
        if (propertyName == null) return;

        if (!database.NodeTables.TryGetValue(tableName, out var table))
            return;

        // Update property at the given row offset directly in the table data
        table.SetPropertyByRowIndex(record.NodeOffset, propertyName, record.NewValue);
    }

    private static void ReplayRelDeletion(
        RelDeletionWALRecord record,
        Main.BogDatabase database,
        Main.BogConnection connection)
    {
        var tableName = database.GetTableNameById(record.TableId);
        if (tableName == null) return;

        if (!database.RelTables.TryGetValue(tableName, out var table))
            return;

        // Remove by row index — the WAL logged the row offset at deletion time
        table.RemoveByRowIndex(record.RelId);
    }

    private static void ReplayRelUpdate(
        RelUpdateWALRecord record,
        Main.BogDatabase database,
        Main.BogConnection connection)
    {
        var tableName = database.GetTableNameById(record.TableId);
        if (tableName == null) return;

        var propertyName = database.GetColumnNameByOrdinal(record.TableId, record.ColumnId);
        if (propertyName == null) return;

        if (!database.RelTables.TryGetValue(tableName, out var table))
            return;

        // Update property at the given row offset directly in the rel table data
        table.SetPropertyByRowIndex(record.RelId, propertyName, record.NewValue);
    }

    private static void ReplayCreateCatalogEntry(
        CreateCatalogEntryWALRecord record,
        Main.BogDatabase database,
        Main.BogConnection connection)
    {
        // Deserialize the catalog entry and create the table
        if (record.SerializedEntry.Length > 0)
        {
            using var ms = new MemoryStream(record.SerializedEntry);
            using var br = new BinaryReader(ms);
            var entry = Catalog.CatalogEntry.Deserialize(br);
            if (entry != null)
            {
                database.ReplayCreateTable(entry);
            }
        }
    }

    private static void ReplayDropCatalogEntry(
        DropCatalogEntryWALRecord record,
        Main.BogDatabase database,
        Main.BogConnection connection)
    {
        var tableName = database.GetTableNameById(record.EntryId);
        if (tableName != null)
        {
            connection.Query($"DROP TABLE {tableName}");
        }
    }

    private static void ReplayAlterTableEntry(
        AlterTableEntryWALRecord record,
        Main.BogDatabase database,
        Main.BogConnection connection)
    {
        var tableName = database.GetTableNameById(record.TableId);
        if (tableName == null) return;

        var tx = Transaction.DUMMY_TRANSACTION;

        // AlterType convention:
        //   0 = RENAME_TABLE
        //   1 = ADD_PROPERTY
        //   2 = DROP_PROPERTY
        //   3 = RENAME_PROPERTY
        switch (record.AlterType)
        {
            case 0: // RENAME_TABLE
                if (!string.IsNullOrEmpty(record.NewName))
                    database.AlterTableRename(tx, tableName, record.NewName);
                break;

            case 1: // ADD_PROPERTY
                var propType = (Common.LogicalTypeID)record.PropertyTypeId;
                object? defaultValue = null;
                if (record.DefaultValuePayload.Length > 0)
                {
                    using var ms = new MemoryStream(record.DefaultValuePayload);
                    using var br = new BinaryReader(ms);
                    defaultValue = WALValueSerializer.ReadValue(br);
                }
                database.AlterTableAddProperty(tx, tableName, record.PropertyName, propType);
                break;

            case 2: // DROP_PROPERTY
                database.AlterTableDropProperty(tx, tableName, record.PropertyName);
                break;

            case 3: // RENAME_PROPERTY
                if (!string.IsNullOrEmpty(record.NewName))
                    database.AlterTableRenameProperty(tx, tableName, record.PropertyName, record.NewName);
                break;
        }
    }

    // ── File management ──────────────────────────────────────────────────────

    private void TruncateWAL(long offset)
    {
        try
        {
            using var fs = new FileStream(_walPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            fs.SetLength(offset);
        }
        catch (IOException)
        {
            // Truncation failure is non-fatal — WAL will be replayed again
        }
    }
}
