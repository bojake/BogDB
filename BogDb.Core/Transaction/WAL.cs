//------------------------------------------------------------------------------
// WAL.cs — Write-Ahead Log (logical WAL, C++ parity)
//
// C++ parity: src/storage/wal/wal.cpp / wal.h
//
// This WAL logs logical operations (insert/update/delete/DDL) instead of
// physical page writes. On recovery, operations are replayed through the
// table APIs so indexes and constraints are rebuilt correctly.
//------------------------------------------------------------------------------

using System;
using System.IO;

namespace BogDb.Core.Transaction;

/// <summary>
/// The central Write-Ahead Log. Serializes WALRecords into the WAL file.
/// Thread-safe via a global lock.
/// </summary>
public sealed class WAL : IDisposable
{
    private FileStream? _walStream;
    private BinaryWriter? _writer;
    private readonly object _lock = new();

    private readonly string _walPath;
    private readonly bool _readOnly;
    private readonly bool _inMemory;
    private readonly Guid _databaseId;
    private bool _headerWritten;

    public WAL(string dbPath, bool readOnly, bool inMemory)
    {
        _walPath = Path.Combine(dbPath, "data.wal");
        _readOnly = readOnly;
        _inMemory = inMemory;
        _databaseId = Guid.NewGuid();

        if (!readOnly && !inMemory)
        {
            _walStream = new FileStream(_walPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            _walStream.Seek(0, SeekOrigin.End);
            _writer = new BinaryWriter(_walStream);
        }
    }

    public string WalPath => _walPath;
    public Guid DatabaseId => _databaseId;

    // ── Header ───────────────────────────────────────────────────────────────

    private void EnsureHeader()
    {
        if (_headerWritten || _writer == null) return;
        if (_walStream!.Length > 0) { _headerWritten = true; return; }

        var header = new WALFileHeader
        {
            DatabaseId = _databaseId,
            EnableChecksums = false,
        };
        header.Serialize(_writer);
        _headerWritten = true;
    }

    // ── Public log methods ───────────────────────────────────────────────────

    public void LogBeginTransaction()
    {
        LogRecord(new BeginTransactionWALRecord());
    }

    public void LogAndFlushCommit(Transaction transaction, long graphLogOffset = 0)
    {
        lock (_lock)
        {
            if (_readOnly || _inMemory) return;
            EnsureHeader();

            var record = new CommitWALRecord
            {
                TransactionId = transaction.ID,
                GraphLogCommittedOffset = graphLogOffset,
            };
            record.SerializeWithHeader(_writer!);
            _walStream!.Flush(flushToDisk: true);
        }
    }

    public void LogAndFlushCheckpoint()
    {
        lock (_lock)
        {
            if (_readOnly || _inMemory) return;
            EnsureHeader();

            var record = new CheckpointWALRecord();
            record.SerializeWithHeader(_writer!);
            _walStream!.Flush(flushToDisk: true);
        }
    }

    // ── DDL logging ──────────────────────────────────────────────────────────

    public void LogCreateTableRecord(uint tableID, BogDb.Core.Catalog.CatalogEntry entry)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)entry.Type);
        entry.Serialize(bw);
        var payload = ms.ToArray();

        LogRecord(new CreateCatalogEntryWALRecord
        {
            TableId = tableID,
            EntryType = (byte)entry.Type,
            SerializedEntry = payload,
            IsInternal = false,
        });
    }

    public void LogDropTableRecord(uint tableID)
    {
        LogRecord(new DropCatalogEntryWALRecord
        {
            EntryId = tableID,
            EntryType = 0, // generic — replayed by entryId
        });
    }

    public void LogAlterTableEntry(uint tableId, byte alterType, string propertyName,
        string newName = "", byte propertyTypeId = 0, byte[]? defaultPayload = null)
    {
        LogRecord(new AlterTableEntryWALRecord
        {
            TableId = tableId,
            AlterType = alterType,
            PropertyName = propertyName,
            NewName = newName,
            PropertyTypeId = propertyTypeId,
            DefaultValuePayload = defaultPayload ?? Array.Empty<byte>(),
        });
    }

    // ── DML logging ──────────────────────────────────────────────────────────

    public void LogTableInsertion(uint tableId, byte tableType, string[] columnNames,
        System.Collections.Generic.List<System.Collections.Generic.List<object?>> rows)
    {
        LogRecord(new TableInsertionWALRecord
        {
            TableId = tableId,
            TableType = tableType,
            NumRows = rows.Count,
            ColumnNames = columnNames,
            Rows = rows,
        });
    }

    public void LogNodeDeletion(uint tableId, long nodeOffset, object? pkValue)
    {
        LogRecord(new NodeDeletionWALRecord
        {
            TableId = tableId,
            NodeOffset = nodeOffset,
            PrimaryKeyValue = pkValue,
        });
    }

    public void LogNodeUpdate(uint tableId, uint columnId, long nodeOffset, object? newValue)
    {
        LogRecord(new NodeUpdateWALRecord
        {
            TableId = tableId,
            ColumnId = columnId,
            NodeOffset = nodeOffset,
            NewValue = newValue,
        });
    }

    public void LogRelDeletion(uint tableId, long srcNodeId, long dstNodeId, long relId)
    {
        LogRecord(new RelDeletionWALRecord
        {
            TableId = tableId,
            SrcNodeId = srcNodeId,
            DstNodeId = dstNodeId,
            RelId = relId,
        });
    }

    public void LogRelUpdate(uint tableId, uint columnId, long srcNodeId, long dstNodeId,
        long relId, object? newValue)
    {
        LogRecord(new RelUpdateWALRecord
        {
            TableId = tableId,
            ColumnId = columnId,
            SrcNodeId = srcNodeId,
            DstNodeId = dstNodeId,
            RelId = relId,
            NewValue = newValue,
        });
    }

    public void LogUpdateSequence(uint sequenceId, ulong kCount)
    {
        LogRecord(new UpdateSequenceWALRecord
        {
            SequenceId = sequenceId,
            KCount = kCount,
        });
    }

    public void LogCopyTable(uint tableId)
    {
        LogRecord(new CopyTableWALRecord { TableId = tableId });
    }

    public void LogLoadExtension(string path)
    {
        LogRecord(new LoadExtensionWALRecord { Path = path });
    }

    // ── Legacy API (backward compat for existing callers) ────────────────────

    public void LogPageUpdate(uint fileIdx, ulong pageIdx)
    {
        // No-op logically here, handled practically by LogPageUpdateWithData
    }

    public void LogPageUpdateWithData(string filePath, ulong pageIdx, ReadOnlySpan<byte> data, uint pageSize)
    {
        LogRecord(new PageUpdateWALRecord
        {
            FilePath = filePath,
            PageIdx = pageIdx,
            PageData = data.ToArray(),
        });
    }

    // ── File management ──────────────────────────────────────────────────────

    public ulong GetFileSize()
    {
        lock (_lock)
        {
            return _walStream != null ? (ulong)_walStream.Length : 0;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_walStream != null)
            {
                _walStream.SetLength(0);
                _walStream.Flush(flushToDisk: true);
                _headerWritten = false;
            }
        }
    }

    public void Truncate(ulong length)
    {
        lock (_lock)
        {
            if (_walStream == null) return;
            _walStream.SetLength(checked((long)length));
            _walStream.Seek(0, SeekOrigin.End);
            _walStream.Flush(flushToDisk: true);
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _walStream?.Dispose();
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private void LogRecord(WALRecordBase record)
    {
        lock (_lock)
        {
            if (_readOnly || _inMemory) return;
            EnsureHeader();
            record.SerializeWithHeader(_writer!);
            _writer!.Flush();
        }
    }
}
