using System;
using System.Collections.Generic;
using System.IO;
using BogDb.Core.Main;

namespace BogDb.Core.Storage;

/// <summary>
/// Record kinds in the graph log (<c>graph-log.bin</c>).
/// The numeric values are the on-disk encoding — never renumber an existing member.
/// </summary>
internal enum GraphLogRecordType : byte
{
    /// <summary>Insert-or-update of a node row.</summary>
    NodeUpsert = 1,

    /// <summary>Insert-or-update of a relationship row.</summary>
    RelUpsert = 2,

    /// <summary>Removal of a node row.</summary>
    NodeDelete = 3,

    /// <summary>Removal of a relationship row.</summary>
    RelDelete = 4,

    /// <summary>
    /// Insert of a relationship row — written by relationship MERGE. Shares
    /// <see cref="RelUpsert"/>'s wire shape but applies as an insert rather than an upsert.
    /// </summary>
    RelInsert = 5,
}

/// <summary>A decoded graph-log record.</summary>
internal readonly record struct GraphLogRecord(
    GraphLogRecordType Type,
    string TableName,
    object Id,
    object? Id2,
    Dictionary<string, object> Properties)
{
    /// <summary>
    /// The edge this record addresses. Only meaningful for record types that carry a second id
    /// (see <see cref="GraphLogFormat.HasSecondId"/>).
    /// </summary>
    public EdgeKey EdgeKey => new(Id, Id2!);
}

/// <summary>
/// The single source of truth for the graph-log wire format: what each record type looks like on
/// disk, how to write one, how to read one back, and how to apply it to table data.
///
/// Everything that touches the log — <see cref="GraphLogWriter"/>, <see cref="GraphLogReader"/> and
/// <see cref="GraphStore"/> — goes through here. Previously the shape rules were restated at every
/// call site, and the readers drifted: <see cref="GraphLogRecordType.RelInsert"/> was taught to some
/// of them and not others, so a log containing a MERGE'd edge aborted recovery. Adding a record type
/// now means adding an enum member plus its shape and apply rules in this file, and nothing else.
/// </summary>
internal static class GraphLogFormat
{
    /// <summary>Record types that write a second id (the edge target) after the primary id.</summary>
    public static bool HasSecondId(GraphLogRecordType type) =>
        type is GraphLogRecordType.RelUpsert or GraphLogRecordType.RelDelete or GraphLogRecordType.RelInsert;

    /// <summary>Record types that write a property map at the end of the record.</summary>
    public static bool HasProperties(GraphLogRecordType type) =>
        type is GraphLogRecordType.NodeUpsert or GraphLogRecordType.RelUpsert or GraphLogRecordType.RelInsert;

    /// <summary>Writes one record. The caller owns flushing and page logging.</summary>
    public static void WriteRecord(
        BinaryWriter writer,
        GraphLogRecordType type,
        string tableName,
        object id,
        object? id2,
        Dictionary<string, object>? props)
    {
        writer.Write((byte)type);
        writer.Write(tableName);
        GraphDataSerializer.WriteValue(writer, id);
        if (HasSecondId(type))
        {
            GraphDataSerializer.WriteValue(writer, id2);
        }
        if (HasProperties(type))
        {
            GraphDataSerializer.WriteProperties(writer, props ?? new Dictionary<string, object>());
        }
    }

    /// <summary>
    /// Reads the next record. Returns false at end of log — either a clean EOF, a record truncated by
    /// a crash, or the zero padding that marks an unwritten tail — in every case the caller stops.
    /// Throws <see cref="InvalidDataException"/> for a record type this build does not know, since
    /// continuing would misparse every byte that follows.
    /// </summary>
    public static bool TryReadRecord(BinaryReader reader, out GraphLogRecord record)
    {
        record = default;

        byte rawType;
        try
        {
            rawType = reader.ReadByte();
        }
        catch (EndOfStreamException)
        {
            return false;
        }

        if (rawType == 0)
        {
            // Treat a zero record type as padding/EOF for resiliency. If the remainder is not all
            // zeros we still stop, rather than attempting to parse corrupted records.
            _ = IsRemainingZero(reader);
            return false;
        }

        try
        {
            var tableName = reader.ReadString();
            var id = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();

            var type = (GraphLogRecordType)rawType;
            if (!Enum.IsDefined(type))
            {
                throw new InvalidDataException($"Unknown graph log record type: {rawType}");
            }

            var id2 = HasSecondId(type)
                ? GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString()
                : null;
            var props = HasProperties(type)
                ? GraphDataSerializer.ReadProperties(reader)
                : new Dictionary<string, object>();

            record = new GraphLogRecord(type, tableName, id, id2, props);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    /// <summary>
    /// Applies a record to in-memory table data. Missing tables are created for the record types that
    /// add rows; deletes against an unknown table are a no-op.
    /// </summary>
    public static void Apply(
        in GraphLogRecord record,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables)
    {
        switch (record.Type)
        {
            case GraphLogRecordType.NodeUpsert:
                GetOrAdd(nodeTables, record.TableName).Upsert(record.Id, record.Properties);
                break;

            case GraphLogRecordType.NodeDelete:
                if (nodeTables.TryGetValue(record.TableName, out var nodeTable))
                    nodeTable.Remove(record.Id);
                break;

            case GraphLogRecordType.RelUpsert:
                GetOrAdd(relTables, record.TableName).Upsert(record.EdgeKey, record.Properties);
                break;

            case GraphLogRecordType.RelInsert:
                GetOrAdd(relTables, record.TableName).Insert(record.EdgeKey, record.Properties);
                break;

            case GraphLogRecordType.RelDelete:
                if (relTables.TryGetValue(record.TableName, out var relTable))
                    relTable.Remove(record.EdgeKey);
                break;

            default:
                // Unreachable for parsed records — TryReadRecord rejects undefined types. This fires
                // only if a new record type is added without an apply rule here.
                throw new InvalidDataException($"Graph log record type {record.Type} has no apply rule.");
        }
    }

    private static TTable GetOrAdd<TTable>(Dictionary<string, TTable> tables, string tableName)
        where TTable : new()
    {
        if (!tables.TryGetValue(tableName, out var table))
        {
            table = new TTable();
            tables[tableName] = table;
        }
        return table;
    }

    private static bool IsRemainingZero(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        var buffer = new byte[4096];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != 0)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
