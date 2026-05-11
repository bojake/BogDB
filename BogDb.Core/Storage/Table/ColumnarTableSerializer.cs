using System;
using System.Collections.Generic;
using System.IO;
using BogDb.Core.Main;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Disk serializer for graph table snapshots in a column-major layout.
/// Format v1:
/// - int32 magic ('KZCS')
/// - int32 version
/// - node section
/// - rel section
///
/// Backward compatibility:
/// If magic/version are missing, falls back to the legacy row-major layout.
/// </summary>
internal static class ColumnarTableSerializer
{
    private const int Magic = 0x53435A4B; // 'KZCS'
    private const int Version = 2;

    public static void WriteSnapshot(
        string outputPath,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables)
    {
        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(stream);

        writer.Write(Magic);
        writer.Write(Version);

        WriteNodeSection(writer, nodeTables);
        WriteRelSection(writer, relTables);
    }

    public static bool TryReadSnapshot(
        string inputPath,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables)
    {
        if (!File.Exists(inputPath))
            return false;

        nodeTables.Clear();
        relTables.Clear();

        if (TryReadColumnarSnapshot(inputPath, nodeTables, relTables))
            return true;

        return TryReadLegacySnapshot(inputPath, nodeTables, relTables);
    }

    private static void WriteNodeSection(BinaryWriter writer, Dictionary<string, NodeTableData> nodeTables)
    {
        writer.Write(nodeTables.Count);
        foreach (var (tableName, table) in nodeTables)
        {
            writer.Write(tableName);
            var rows = new List<KeyValuePair<object, Dictionary<string, object>>>(table.EnumerateRows());
            writer.Write(rows.Count);

            var propNames = CollectPropertyNames(rows);
            writer.Write(propNames.Count);
            foreach (var prop in propNames)
                writer.Write(prop);

            // ID column
            foreach (var (id, _) in rows)
                GraphDataSerializer.WriteValue(writer, id);

            // Property columns
            foreach (var propName in propNames)
            {
                foreach (var (_, props) in rows)
                {
                    if (props.TryGetValue(propName, out var value) && value is not null)
                    {
                        writer.Write(true);
                        GraphDataSerializer.WriteValue(writer, value);
                    }
                    else
                    {
                        writer.Write(false);
                    }
                }
            }
        }
    }

    private static void WriteRelSection(BinaryWriter writer, Dictionary<string, RelTableData> relTables)
    {
        writer.Write(relTables.Count);
        foreach (var (tableName, table) in relTables)
        {
            writer.Write(tableName);
            writer.Write(table.FromTableName);
            writer.Write(table.ToTableName);
            var rows = new List<KeyValuePair<EdgeKey, Dictionary<string, object>>>(table.EnumerateRows());
            writer.Write(rows.Count);

            var propNames = CollectRelPropertyNames(rows);
            writer.Write(propNames.Count);
            foreach (var prop in propNames)
                writer.Write(prop);

            // Endpoint columns
            foreach (var (edge, _) in rows)
            {
                GraphDataSerializer.WriteValue(writer, edge.From);
                GraphDataSerializer.WriteValue(writer, edge.To);
            }

            // Property columns
            foreach (var propName in propNames)
            {
                foreach (var (_, props) in rows)
                {
                    if (props.TryGetValue(propName, out var value) && value is not null)
                    {
                        writer.Write(true);
                        GraphDataSerializer.WriteValue(writer, value);
                    }
                    else
                    {
                        writer.Write(false);
                    }
                }
            }
        }
    }

    private static bool TryReadColumnarSnapshot(
        string inputPath,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables)
    {
        try
        {
            using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            if (stream.Length < sizeof(int) * 2)
                return false;

            var magic = reader.ReadInt32();
            var version = reader.ReadInt32();
            if (magic != Magic || (version != 1 && version != Version))
                return false;

            var nodeTableCount = reader.ReadInt32();
            for (var i = 0; i < nodeTableCount; i++)
            {
                var tableName = reader.ReadString();
                var rowCount = reader.ReadInt32();
                var numProps = reader.ReadInt32();
                var propNames = new string[numProps];
                for (var p = 0; p < numProps; p++)
                    propNames[p] = reader.ReadString();

                var ids = new object[rowCount];
                for (var r = 0; r < rowCount; r++)
                    ids[r] = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();

                var columns = new object?[numProps][];
                for (var p = 0; p < numProps; p++)
                {
                    columns[p] = new object?[rowCount];
                    for (var r = 0; r < rowCount; r++)
                    {
                        var hasValue = reader.ReadBoolean();
                        columns[p][r] = hasValue ? GraphDataSerializer.ReadValue(reader) : null;
                    }
                }

                var table = new NodeTableData();
                for (var r = 0; r < rowCount; r++)
                {
                    var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (var p = 0; p < numProps; p++)
                    {
                        var value = columns[p][r];
                        if (value is not null)
                            props[propNames[p]] = value;
                    }
                    table.Upsert(ids[r], props);
                }
                nodeTables[tableName] = table;
            }

            var relTableCount = reader.ReadInt32();
            for (var i = 0; i < relTableCount; i++)
            {
                var tableName = reader.ReadString();
                var fromTable = version >= 2 ? reader.ReadString() : string.Empty;
                var toTable = version >= 2 ? reader.ReadString() : string.Empty;
                var rowCount = reader.ReadInt32();
                var numProps = reader.ReadInt32();
                var propNames = new string[numProps];
                for (var p = 0; p < numProps; p++)
                    propNames[p] = reader.ReadString();

                var edges = new EdgeKey[rowCount];
                for (var r = 0; r < rowCount; r++)
                {
                    var from = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                    var to = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                    edges[r] = new EdgeKey(from, to);
                }

                var columns = new object?[numProps][];
                for (var p = 0; p < numProps; p++)
                {
                    columns[p] = new object?[rowCount];
                    for (var r = 0; r < rowCount; r++)
                    {
                        var hasValue = reader.ReadBoolean();
                        columns[p][r] = hasValue ? GraphDataSerializer.ReadValue(reader) : null;
                    }
                }

                var table = new RelTableData(fromTable, toTable);
                for (var r = 0; r < rowCount; r++)
                {
                    var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (var p = 0; p < numProps; p++)
                    {
                        var value = columns[p][r];
                        if (value is not null)
                            props[propNames[p]] = value;
                    }
                    table.Insert(edges[r], props);
                }
                relTables[tableName] = table;
            }

            return true;
        }
        catch
        {
            nodeTables.Clear();
            relTables.Clear();
            return false;
        }
    }

    private static bool TryReadLegacySnapshot(
        string inputPath,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables)
    {
        try
        {
            using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            var nodeTableCount = reader.ReadInt32();
            for (var i = 0; i < nodeTableCount; i++)
            {
                var tableName = reader.ReadString();
                var rowCount = reader.ReadInt32();
                var table = new NodeTableData();
                for (var row = 0; row < rowCount; row++)
                {
                    var id = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                    table.Upsert(id, GraphDataSerializer.ReadProperties(reader));
                }
                nodeTables[tableName] = table;
            }

            var relTableCount = reader.ReadInt32();
            for (var i = 0; i < relTableCount; i++)
            {
                var tableName = reader.ReadString();
                var rowCount = reader.ReadInt32();
                var table = new RelTableData();
                for (var row = 0; row < rowCount; row++)
                {
                    var from = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                    var to = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                    table.Insert(new EdgeKey(from, to), GraphDataSerializer.ReadProperties(reader));
                }
                relTables[tableName] = table;
            }
            return true;
        }
        catch
        {
            nodeTables.Clear();
            relTables.Clear();
            return false;
        }
    }

    private static List<string> CollectPropertyNames(List<KeyValuePair<object, Dictionary<string, object>>> rows)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, props) in rows)
        {
            foreach (var key in props.Keys)
            {
                if (seen.Add(key))
                    names.Add(key);
            }
        }
        return names;
    }

    private static List<string> CollectRelPropertyNames(List<KeyValuePair<EdgeKey, Dictionary<string, object>>> rows)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, props) in rows)
        {
            foreach (var key in props.Keys)
            {
                if (seen.Add(key))
                    names.Add(key);
            }
        }
        return names;
    }
}
