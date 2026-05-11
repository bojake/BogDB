using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BogDb.Core.Common;

namespace BogDb.Core.Transaction;

// ── WAL Record Type Enum (C++ parity: wal_record.h) ──────────────────────────

/// <summary>
/// WAL record types matching the C++ enum in wal_record.h.
/// Numeric values are identical to the C++ counterparts to maintain
/// binary compatibility for any cross-language tooling.
/// </summary>
public enum WALLogicalRecordType : byte
{
    INVALID_RECORD = 0,
    BEGIN_TRANSACTION_RECORD = 1,
    COMMIT_RECORD = 2,

    COPY_TABLE_RECORD = 13,
    CREATE_CATALOG_ENTRY_RECORD = 14,
    DROP_CATALOG_ENTRY_RECORD = 16,
    ALTER_TABLE_ENTRY_RECORD = 17,
    UPDATE_SEQUENCE_RECORD = 18,
    PAGE_UPDATE_RECORD = 19,

    TABLE_INSERTION_RECORD = 30,
    NODE_DELETION_RECORD = 31,
    NODE_UPDATE_RECORD = 32,
    REL_DELETION_RECORD = 33,
    REL_DETACH_DELETE_RECORD = 34,
    REL_UPDATE_RECORD = 35,

    LOAD_EXTENSION_RECORD = 100,

    CHECKPOINT_RECORD = 254,
}

// ── WAL Header ───────────────────────────────────────────────────────────────

/// <summary>
/// WAL file header — written once at the start of the WAL file.
/// Contains a database UUID for cross-database corruption detection
/// and a checksum flag.
/// </summary>
public struct WALFileHeader
{
    public Guid DatabaseId;
    public bool EnableChecksums;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(DatabaseId.ToByteArray());
        writer.Write(EnableChecksums);
    }

    public static WALFileHeader Deserialize(BinaryReader reader)
    {
        return new WALFileHeader
        {
            DatabaseId = new Guid(reader.ReadBytes(16)),
            EnableChecksums = reader.ReadBoolean(),
        };
    }
}

// ── Base WAL Record ──────────────────────────────────────────────────────────

/// <summary>
/// Base class for all WAL records. Provides type-based dispatch for
/// serialization and deserialization.
/// C++ parity: WALRecord in wal_record.h.
/// </summary>
public abstract class WALRecordBase
{
    public abstract WALLogicalRecordType RecordType { get; }

    public void SerializeWithHeader(BinaryWriter writer)
    {
        writer.Write((byte)RecordType);
        Serialize(writer);
    }

    protected abstract void Serialize(BinaryWriter writer);

    protected static void WriteUtf8String(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    protected static string ReadUtf8String(BinaryReader reader)
    {
        var len = reader.ReadInt32();
        return Encoding.UTF8.GetString(reader.ReadBytes(len));
    }

    public static WALRecordBase Deserialize(BinaryReader reader)
    {
        var type = (WALLogicalRecordType)reader.ReadByte();
        return type switch
        {
            WALLogicalRecordType.BEGIN_TRANSACTION_RECORD => BeginTransactionWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.COMMIT_RECORD => CommitWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.CHECKPOINT_RECORD => CheckpointWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.CREATE_CATALOG_ENTRY_RECORD => CreateCatalogEntryWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.DROP_CATALOG_ENTRY_RECORD => DropCatalogEntryWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.ALTER_TABLE_ENTRY_RECORD => AlterTableEntryWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.TABLE_INSERTION_RECORD => TableInsertionWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.NODE_DELETION_RECORD => NodeDeletionWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.NODE_UPDATE_RECORD => NodeUpdateWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.REL_DELETION_RECORD => RelDeletionWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.REL_DETACH_DELETE_RECORD => RelDetachDeleteWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.REL_UPDATE_RECORD => RelUpdateWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.UPDATE_SEQUENCE_RECORD => UpdateSequenceWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.PAGE_UPDATE_RECORD => PageUpdateWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.COPY_TABLE_RECORD => CopyTableWALRecord.DeserializeBody(reader),
            WALLogicalRecordType.LOAD_EXTENSION_RECORD => LoadExtensionWALRecord.DeserializeBody(reader),
            _ => throw new InvalidDataException($"Unknown or invalid WAL record type: {type}")
        };
    }
}

// ── Transaction boundary records ─────────────────────────────────────────────

public sealed class BeginTransactionWALRecord : WALRecordBase
{
    public override WALLogicalRecordType RecordType => WALLogicalRecordType.BEGIN_TRANSACTION_RECORD;
    protected override void Serialize(BinaryWriter writer) { /* no payload */ }
    public static BeginTransactionWALRecord DeserializeBody(BinaryReader reader) => new();
}

public sealed class CommitWALRecord : WALRecordBase
{
    public ulong TransactionId { get; init; }
    /// <summary>
    /// The graph-log file length at the time of commit. Used during recovery
    /// to truncate uncommitted tails from the graph-log.
    /// </summary>
    public long GraphLogCommittedOffset { get; init; }
    public override WALLogicalRecordType RecordType => WALLogicalRecordType.COMMIT_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(TransactionId);
        writer.Write(GraphLogCommittedOffset);
    }

    public static CommitWALRecord DeserializeBody(BinaryReader reader)
        => new()
        {
            TransactionId = reader.ReadUInt64(),
            GraphLogCommittedOffset = reader.ReadInt64(),
        };
}

public sealed class CheckpointWALRecord : WALRecordBase
{
    public override WALLogicalRecordType RecordType => WALLogicalRecordType.CHECKPOINT_RECORD;
    protected override void Serialize(BinaryWriter writer) { /* no payload */ }
    public static CheckpointWALRecord DeserializeBody(BinaryReader reader) => new();
}

// ── Catalog DDL records ──────────────────────────────────────────────────────

public sealed class CreateCatalogEntryWALRecord : WALRecordBase
{
    public uint TableId { get; init; }
    public byte EntryType { get; init; }  // CatalogEntryType
    public byte[] SerializedEntry { get; init; } = Array.Empty<byte>();
    public bool IsInternal { get; init; }

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.CREATE_CATALOG_ENTRY_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(TableId);
        writer.Write(EntryType);
        writer.Write(IsInternal);
        writer.Write(SerializedEntry.Length);
        writer.Write(SerializedEntry);
    }

    public static CreateCatalogEntryWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        TableId = reader.ReadUInt32(),
        EntryType = reader.ReadByte(),
        IsInternal = reader.ReadBoolean(),
        SerializedEntry = reader.ReadBytes(reader.ReadInt32()),
    };
}

public sealed class DropCatalogEntryWALRecord : WALRecordBase
{
    public uint EntryId { get; init; }
    public byte EntryType { get; init; }  // CatalogEntryType

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.DROP_CATALOG_ENTRY_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(EntryId);
        writer.Write(EntryType);
    }

    public static DropCatalogEntryWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        EntryId = reader.ReadUInt32(),
        EntryType = reader.ReadByte(),
    };
}

/// <summary>
/// Alter table WAL record. Carries the alter type and serialized alter info.
/// </summary>
public sealed class AlterTableEntryWALRecord : WALRecordBase
{
    public uint TableId { get; init; }
    public byte AlterType { get; init; }  // AlterType enum
    public string PropertyName { get; init; } = "";
    public string NewName { get; init; } = "";
    public byte PropertyTypeId { get; init; }
    public byte[] DefaultValuePayload { get; init; } = Array.Empty<byte>();

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.ALTER_TABLE_ENTRY_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(TableId);
        writer.Write(AlterType);
        WriteUtf8String(writer, PropertyName);
        WriteUtf8String(writer, NewName);
        writer.Write(PropertyTypeId);
        writer.Write(DefaultValuePayload.Length);
        writer.Write(DefaultValuePayload);
    }

    public static AlterTableEntryWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        TableId = reader.ReadUInt32(),
        AlterType = reader.ReadByte(),
        PropertyName = ReadUtf8String(reader),
        NewName = ReadUtf8String(reader),
        PropertyTypeId = reader.ReadByte(),
        DefaultValuePayload = reader.ReadBytes(reader.ReadInt32()),
    };
}

// ── DML records ──────────────────────────────────────────────────────────────

/// <summary>
/// Logs row insertions for both node and rel tables.
/// C++ parity: TableInsertionRecord in wal_record.h.
/// </summary>
public sealed class TableInsertionWALRecord : WALRecordBase
{
    public uint TableId { get; init; }
    public byte TableType { get; init; }  // 0 = NODE, 1 = REL
    public int NumRows { get; init; }
    /// <summary>Column names in insertion order.</summary>
    public string[] ColumnNames { get; init; } = Array.Empty<string>();
    /// <summary>Rows of property values — each row has one value per column.</summary>
    public List<List<object?>> Rows { get; init; } = new();

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.TABLE_INSERTION_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(TableId);
        writer.Write(TableType);
        writer.Write(NumRows);
        writer.Write(ColumnNames.Length);
        foreach (var col in ColumnNames)
            WriteUtf8String(writer, col);
        WALValueSerializer.WriteRows(writer, Rows.ConvertAll(r => (IReadOnlyList<object?>)r));
    }

    public static TableInsertionWALRecord DeserializeBody(BinaryReader reader)
    {
        var tableId = reader.ReadUInt32();
        var tableType = reader.ReadByte();
        var numRows = reader.ReadInt32();
        var numCols = reader.ReadInt32();
        var colNames = new string[numCols];
        for (int i = 0; i < numCols; i++)
            colNames[i] = ReadUtf8String(reader);
        var rows = WALValueSerializer.ReadRows(reader);
        return new()
        {
            TableId = tableId,
            TableType = tableType,
            NumRows = numRows,
            ColumnNames = colNames,
            Rows = rows,
        };
    }
}

/// <summary>
/// Logs node deletion. C++ parity: NodeDeletionRecord.
/// </summary>
public sealed class NodeDeletionWALRecord : WALRecordBase
{
    public uint TableId { get; init; }
    public long NodeOffset { get; init; }
    public object? PrimaryKeyValue { get; init; }

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.NODE_DELETION_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(TableId);
        writer.Write(NodeOffset);
        WALValueSerializer.WriteValue(writer, PrimaryKeyValue);
    }

    public static NodeDeletionWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        TableId = reader.ReadUInt32(),
        NodeOffset = reader.ReadInt64(),
        PrimaryKeyValue = WALValueSerializer.ReadValue(reader),
    };
}

/// <summary>
/// Logs node property update. C++ parity: NodeUpdateRecord.
/// </summary>
public sealed class NodeUpdateWALRecord : WALRecordBase
{
    public uint TableId { get; init; }
    public uint ColumnId { get; init; }
    public long NodeOffset { get; init; }
    public object? NewValue { get; init; }

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.NODE_UPDATE_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(TableId);
        writer.Write(ColumnId);
        writer.Write(NodeOffset);
        WALValueSerializer.WriteValue(writer, NewValue);
    }

    public static NodeUpdateWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        TableId = reader.ReadUInt32(),
        ColumnId = reader.ReadUInt32(),
        NodeOffset = reader.ReadInt64(),
        NewValue = WALValueSerializer.ReadValue(reader),
    };
}

/// <summary>
/// Logs relationship deletion. C++ parity: RelDeletionRecord.
/// </summary>
public sealed class RelDeletionWALRecord : WALRecordBase
{
    public uint TableId { get; init; }
    public long SrcNodeId { get; init; }
    public long DstNodeId { get; init; }
    public long RelId { get; init; }

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.REL_DELETION_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(TableId);
        writer.Write(SrcNodeId);
        writer.Write(DstNodeId);
        writer.Write(RelId);
    }

    public static RelDeletionWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        TableId = reader.ReadUInt32(),
        SrcNodeId = reader.ReadInt64(),
        DstNodeId = reader.ReadInt64(),
        RelId = reader.ReadInt64(),
    };
}

/// <summary>
/// Logs relationship detach-delete. C++ parity: RelDetachDeleteRecord.
/// </summary>
public sealed class RelDetachDeleteWALRecord : WALRecordBase
{
    public uint TableId { get; init; }
    public byte Direction { get; init; }  // FWD=0, BWD=1
    public long SrcNodeId { get; init; }

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.REL_DETACH_DELETE_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(TableId);
        writer.Write(Direction);
        writer.Write(SrcNodeId);
    }

    public static RelDetachDeleteWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        TableId = reader.ReadUInt32(),
        Direction = reader.ReadByte(),
        SrcNodeId = reader.ReadInt64(),
    };
}

/// <summary>
/// Logs relationship property update. C++ parity: RelUpdateRecord.
/// </summary>
public sealed class RelUpdateWALRecord : WALRecordBase
{
    public uint TableId { get; init; }
    public uint ColumnId { get; init; }
    public long SrcNodeId { get; init; }
    public long DstNodeId { get; init; }
    public long RelId { get; init; }
    public object? NewValue { get; init; }

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.REL_UPDATE_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(TableId);
        writer.Write(ColumnId);
        writer.Write(SrcNodeId);
        writer.Write(DstNodeId);
        writer.Write(RelId);
        WALValueSerializer.WriteValue(writer, NewValue);
    }

    public static RelUpdateWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        TableId = reader.ReadUInt32(),
        ColumnId = reader.ReadUInt32(),
        SrcNodeId = reader.ReadInt64(),
        DstNodeId = reader.ReadInt64(),
        RelId = reader.ReadInt64(),
        NewValue = WALValueSerializer.ReadValue(reader),
    };
}

// ── Sequence / Copy / Extension records ──────────────────────────────────────

public sealed class UpdateSequenceWALRecord : WALRecordBase
{
    public uint SequenceId { get; init; }
    public ulong KCount { get; init; }

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.UPDATE_SEQUENCE_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        writer.Write(SequenceId);
        writer.Write(KCount);
    }

    public static UpdateSequenceWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        SequenceId = reader.ReadUInt32(),
        KCount = reader.ReadUInt64(),
    };
}

public sealed class PageUpdateWALRecord : WALRecordBase
{
    public string FilePath { get; init; } = "";
    public ulong PageIdx { get; init; }
    public byte[] PageData { get; init; } = Array.Empty<byte>();

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.PAGE_UPDATE_RECORD;

    protected override void Serialize(BinaryWriter writer)
    {
        WriteUtf8String(writer, FilePath);
        writer.Write(PageIdx);
        writer.Write(PageData.Length);
        writer.Write(PageData);
    }

    public static PageUpdateWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        FilePath = ReadUtf8String(reader),
        PageIdx = reader.ReadUInt64(),
        PageData = reader.ReadBytes(reader.ReadInt32()),
    };
}

public sealed class CopyTableWALRecord : WALRecordBase
{
    public uint TableId { get; init; }

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.COPY_TABLE_RECORD;

    protected override void Serialize(BinaryWriter writer) => writer.Write(TableId);

    public static CopyTableWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        TableId = reader.ReadUInt32(),
    };
}

public sealed class LoadExtensionWALRecord : WALRecordBase
{
    public string Path { get; init; } = "";

    public override WALLogicalRecordType RecordType => WALLogicalRecordType.LOAD_EXTENSION_RECORD;

    protected override void Serialize(BinaryWriter writer) => WriteUtf8String(writer, Path);

    public static LoadExtensionWALRecord DeserializeBody(BinaryReader reader) => new()
    {
        Path = ReadUtf8String(reader),
    };
}

