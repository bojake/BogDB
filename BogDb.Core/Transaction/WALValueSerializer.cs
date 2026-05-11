using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BogDb.Core.Common;

namespace BogDb.Core.Transaction;

/// <summary>
/// Self-contained type-tagged serialization for property values in WAL records.
/// Each value is written as: [TypeTag:byte] [ValuePayload:variable]
/// Supports all LogicalTypeID values that appear in DML operations.
/// C++ parity: mirrors the ValueVector serialization used in wal_record.cpp.
/// </summary>
public static class WALValueSerializer
{
    private enum ValueTag : byte
    {
        Null = 0,
        Bool = 1,
        Int8 = 2,
        Int16 = 3,
        Int32 = 4,
        Int64 = 5,
        UInt8 = 6,
        UInt16 = 7,
        UInt32 = 8,
        UInt64 = 9,
        Float = 10,
        Double = 11,
        String = 12,
        Blob = 13,
        Date = 14,
        Timestamp = 15,
        Interval = 16,
        InternalId = 17,
        List = 18,
        Struct = 19,
        Map = 20,
        UUID = 21,
        Int128 = 22,
    }

    /// <summary>Serialize a single property value to the writer.</summary>
    public static void WriteValue(BinaryWriter writer, object? value, LogicalTypeID typeHint = LogicalTypeID.ANY)
    {
        if (value is null || value is DBNull)
        {
            writer.Write((byte)ValueTag.Null);
            return;
        }

        switch (value)
        {
            case bool b:
                writer.Write((byte)ValueTag.Bool);
                writer.Write(b);
                break;
            case sbyte sb:
                writer.Write((byte)ValueTag.Int8);
                writer.Write(sb);
                break;
            case short s:
                writer.Write((byte)ValueTag.Int16);
                writer.Write(s);
                break;
            case int i:
                writer.Write((byte)ValueTag.Int32);
                writer.Write(i);
                break;
            case long l:
                writer.Write((byte)ValueTag.Int64);
                writer.Write(l);
                break;
            case byte ub:
                writer.Write((byte)ValueTag.UInt8);
                writer.Write(ub);
                break;
            case ushort us:
                writer.Write((byte)ValueTag.UInt16);
                writer.Write(us);
                break;
            case uint ui:
                writer.Write((byte)ValueTag.UInt32);
                writer.Write(ui);
                break;
            case ulong ul:
                writer.Write((byte)ValueTag.UInt64);
                writer.Write(ul);
                break;
            case float f:
                writer.Write((byte)ValueTag.Float);
                writer.Write(f);
                break;
            case double d:
                writer.Write((byte)ValueTag.Double);
                writer.Write(d);
                break;
            case string str:
                writer.Write((byte)ValueTag.String);
                var strBytes = Encoding.UTF8.GetBytes(str);
                writer.Write(strBytes.Length);
                writer.Write(strBytes);
                break;
            case byte[] blob:
                writer.Write((byte)ValueTag.Blob);
                writer.Write(blob.Length);
                writer.Write(blob);
                break;
            case IList<object?> list:
                writer.Write((byte)ValueTag.List);
                writer.Write(list.Count);
                foreach (var item in list)
                    WriteValue(writer, item);
                break;
            case IReadOnlyList<object?> roList:
                writer.Write((byte)ValueTag.List);
                writer.Write(roList.Count);
                foreach (var item in roList)
                    WriteValue(writer, item);
                break;
            case IDictionary<string, object?> dict:
                // Could be struct or map depending on context
                writer.Write((byte)ValueTag.Struct);
                writer.Write(dict.Count);
                foreach (var kv in dict)
                {
                    var keyBytes = Encoding.UTF8.GetBytes(kv.Key);
                    writer.Write(keyBytes.Length);
                    writer.Write(keyBytes);
                    WriteValue(writer, kv.Value);
                }
                break;
            case Guid guid:
                writer.Write((byte)ValueTag.UUID);
                writer.Write(guid.ToByteArray());
                break;
            default:
                // Fallback: serialize as string
                writer.Write((byte)ValueTag.String);
                var fallback = Encoding.UTF8.GetBytes(value.ToString() ?? "");
                writer.Write(fallback.Length);
                writer.Write(fallback);
                break;
        }
    }

    /// <summary>Deserialize a single property value from the reader.</summary>
    public static object? ReadValue(BinaryReader reader)
    {
        var tag = (ValueTag)reader.ReadByte();
        return tag switch
        {
            ValueTag.Null => null,
            ValueTag.Bool => reader.ReadBoolean(),
            ValueTag.Int8 => reader.ReadSByte(),
            ValueTag.Int16 => reader.ReadInt16(),
            ValueTag.Int32 => reader.ReadInt32(),
            ValueTag.Int64 => reader.ReadInt64(),
            ValueTag.UInt8 => reader.ReadByte(),
            ValueTag.UInt16 => reader.ReadUInt16(),
            ValueTag.UInt32 => reader.ReadUInt32(),
            ValueTag.UInt64 => reader.ReadUInt64(),
            ValueTag.Float => reader.ReadSingle(),
            ValueTag.Double => reader.ReadDouble(),
            ValueTag.String => ReadString(reader),
            ValueTag.Blob => reader.ReadBytes(reader.ReadInt32()),
            ValueTag.List => ReadList(reader),
            ValueTag.Struct => ReadStruct(reader),
            ValueTag.UUID => new Guid(reader.ReadBytes(16)),
            ValueTag.Date => reader.ReadInt64(),       // epoch days
            ValueTag.Timestamp => reader.ReadInt64(),  // epoch microseconds
            ValueTag.Interval => reader.ReadInt64(),   // interval microseconds
            ValueTag.InternalId => reader.ReadInt64(),  // internal offset
            ValueTag.Int128 => ReadInt128(reader),
            _ => throw new InvalidDataException($"Unknown WAL value tag: {tag}")
        };
    }

    /// <summary>Serialize a row of property values (one per column).</summary>
    public static void WriteRow(BinaryWriter writer, IReadOnlyList<object?> values)
    {
        writer.Write(values.Count);
        foreach (var v in values)
            WriteValue(writer, v);
    }

    /// <summary>Deserialize a row of property values.</summary>
    public static List<object?> ReadRow(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var values = new List<object?>(count);
        for (int i = 0; i < count; i++)
            values.Add(ReadValue(reader));
        return values;
    }

    /// <summary>Serialize multiple rows.</summary>
    public static void WriteRows(BinaryWriter writer, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        writer.Write(rows.Count);
        foreach (var row in rows)
            WriteRow(writer, row);
    }

    /// <summary>Deserialize multiple rows.</summary>
    public static List<List<object?>> ReadRows(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var rows = new List<List<object?>>(count);
        for (int i = 0; i < count; i++)
            rows.Add(ReadRow(reader));
        return rows;
    }

    private static string ReadString(BinaryReader reader)
    {
        var len = reader.ReadInt32();
        var bytes = reader.ReadBytes(len);
        return Encoding.UTF8.GetString(bytes);
    }

    private static List<object?> ReadList(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var list = new List<object?>(count);
        for (int i = 0; i < count; i++)
            list.Add(ReadValue(reader));
        return list;
    }

    private static Dictionary<string, object?> ReadStruct(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var dict = new Dictionary<string, object?>(count);
        for (int i = 0; i < count; i++)
        {
            var key = ReadString(reader);
            var val = ReadValue(reader);
            dict[key] = val;
        }
        return dict;
    }

    private static object ReadInt128(BinaryReader reader)
    {
        var lo = reader.ReadUInt64();
        var hi = reader.ReadInt64();
        // Return as a tuple for now — consumers can interpret
        return (hi, lo);
    }
}
