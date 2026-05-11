using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace BogDb.Core.Main;

public static class GraphDataSerializer
{
    public static void WriteProperties(BinaryWriter writer, Dictionary<string, object> props)
    {
        writer.Write(props.Count);
        foreach (var (key, value) in props)
        {
            writer.Write(key);
            WriteValue(writer, value);
        }
    }

    public static Dictionary<string, object> ReadProperties(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var props = new Dictionary<string, object>(count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var key = reader.ReadString();
            var value = ReadValue(reader);
            props[key] = value ?? string.Empty;
        }
        return props;
    }

    public static void WriteValue(BinaryWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.Write((byte)0);
                break;
            case long l:
                writer.Write((byte)1);
                writer.Write(l);
                break;
            case int i:
                writer.Write((byte)2);
                writer.Write(i);
                break;
            case string s:
                writer.Write((byte)3);
                writer.Write(s);
                break;
            case bool b:
                writer.Write((byte)4);
                writer.Write(b);
                break;
            case double d:
                writer.Write((byte)5);
                writer.Write(d);
                break;
            case float f:
                writer.Write((byte)6);
                writer.Write(f);
                break;
            case decimal dec:
                writer.Write((byte)7);
                writer.Write(dec);
                break;
            case IEnumerable enumerable when value is not string:
            {
                var items = new List<object?>();
                foreach (var item in enumerable)
                    items.Add(item);
                writer.Write((byte)8);
                writer.Write(items.Count);
                foreach (var item in items)
                    WriteValue(writer, item);
                break;
            }
            default:
                writer.Write((byte)255);
                writer.Write(value.ToString() ?? string.Empty);
                break;
        }
    }

    public static object? ReadValue(BinaryReader reader)
    {
        return reader.ReadByte() switch
        {
            0 => null,
            1 => reader.ReadInt64(),
            2 => reader.ReadInt32(),
            3 => reader.ReadString(),
            4 => reader.ReadBoolean(),
            5 => reader.ReadDouble(),
            6 => reader.ReadSingle(),
            7 => reader.ReadDecimal(),
            8 => ReadList(reader),
            255 => reader.ReadString(),
            _ => throw new InvalidDataException("Unknown serialized value type in graph data file.")
        };
    }

    private static List<object?> ReadList(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var values = new List<object?>(count);
        for (var i = 0; i < count; i++)
            values.Add(ReadValue(reader));
        return values;
    }
}
