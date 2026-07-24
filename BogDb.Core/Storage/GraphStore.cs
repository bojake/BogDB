using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BogDb.Core.Main;
using BogDb.Core.Storage.Table;

namespace BogDb.Core.Storage;

internal sealed class GraphStore
{
    private readonly string _dbPath;
    private readonly string _graphDataPath;
    private readonly string _graphLogPath;
    private readonly bool _inMemory;

    public GraphStore(string dbPath, bool inMemory)
    {
        _dbPath = dbPath;
        _inMemory = inMemory;
        _graphDataPath = Path.Combine(dbPath, "graph-data.bin");
        _graphLogPath = Path.Combine(dbPath, "graph-log.bin");
    }

    public IEnumerable<KeyValuePair<object, Dictionary<string, object>>> EnumerateNodes(string tableName)
    {
        if (_inMemory)
            yield break;

        var orderedIds = new List<object>();
        var propertiesById = new Dictionary<object, Dictionary<string, object>>();
        var seenIds = new HashSet<object>();

        if (File.Exists(_graphDataPath))
        {
            var snapshotNodes = new Dictionary<string, NodeTableData>(StringComparer.OrdinalIgnoreCase);
            var snapshotRels = new Dictionary<string, RelTableData>(StringComparer.OrdinalIgnoreCase);
            if (ColumnarTableSerializer.TryReadSnapshot(_graphDataPath, snapshotNodes, snapshotRels) &&
                snapshotNodes.TryGetValue(tableName, out var nodeTable))
            {
                foreach (var (id, props) in nodeTable.EnumerateRows())
                {
                    orderedIds.Add(id);
                    seenIds.Add(id);
                    propertiesById[id] = props;
                }
            }
        }

        if (File.Exists(_graphLogPath))
        {
            using var stream = new FileStream(_graphLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 0)
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                while (stream.Position < stream.Length)
                {
                    if (!TryReadLogRecord(reader, out var recordType, out var currentTableName, out var id, out _, out var props))
                    {
                        yield break;
                    }

                    if (!string.Equals(currentTableName, tableName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (recordType == 1)
                    {
                        if (seenIds.Add(id))
                            orderedIds.Add(id);
                        propertiesById[id] = props;
                    }
                    else if (recordType == 3)
                    {
                        propertiesById.Remove(id);
                    }
                }
            }
        }

        foreach (var id in orderedIds)
        {
            if (propertiesById.TryGetValue(id, out var props))
                yield return new KeyValuePair<object, Dictionary<string, object>>(id, props);
        }
    }

    public IEnumerable<KeyValuePair<EdgeKey, Dictionary<string, object>>> EnumerateRels(string tableName)
    {
        if (_inMemory)
            yield break;

        var orderedEdges = new List<EdgeKey>();
        var propertiesByEdge = new Dictionary<EdgeKey, Dictionary<string, object>>();
        var seenEdges = new HashSet<EdgeKey>();

        if (File.Exists(_graphDataPath))
        {
            var snapshotNodes = new Dictionary<string, NodeTableData>(StringComparer.OrdinalIgnoreCase);
            var snapshotRels = new Dictionary<string, RelTableData>(StringComparer.OrdinalIgnoreCase);
            if (ColumnarTableSerializer.TryReadSnapshot(_graphDataPath, snapshotNodes, snapshotRels) &&
                snapshotRels.TryGetValue(tableName, out var relTable))
            {
                foreach (var (edge, props) in relTable.EnumerateRows())
                {
                    orderedEdges.Add(edge);
                    seenEdges.Add(edge);
                    propertiesByEdge[edge] = props;
                }
            }
        }

        if (File.Exists(_graphLogPath))
        {
            using var stream = new FileStream(_graphLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 0)
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                while (stream.Position < stream.Length)
                {
                    if (!TryReadLogRecord(reader, out var recordType, out var currentTableName, out var id, out var id2, out var props))
                    {
                        yield break;
                    }

                    if (!string.Equals(currentTableName, tableName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Type 5 (rel insert from MERGE) surfaces the same edge as type 2 here — this
                    // enumeration is keyed by EdgeKey, so both register the edge with its latest properties.
                    if (recordType == 2 || recordType == 5)
                    {
                        var edge = new EdgeKey(id, id2!);
                        if (seenEdges.Add(edge))
                            orderedEdges.Add(edge);
                        propertiesByEdge[edge] = props;
                    }
                    else if (recordType == 4)
                    {
                        var edge = new EdgeKey(id, id2!);
                        propertiesByEdge.Remove(edge);
                    }
                }
            }
        }

        foreach (var edge in orderedEdges)
        {
            if (propertiesByEdge.TryGetValue(edge, out var props))
                yield return new KeyValuePair<EdgeKey, Dictionary<string, object>>(edge, props);
        }
    }

    public bool TryGetNodeByOffset(string tableName, long offset, out object nodeId, out Dictionary<string, object> props)
    {
        nodeId = string.Empty;
        props = new Dictionary<string, object>();

        if (_inMemory || offset < 0)
            return false;

        long idx = 0;
        foreach (var (id, properties) in EnumerateNodes(tableName))
        {
            if (idx == offset)
            {
                nodeId = id;
                props = properties;
                return true;
            }
            idx++;
        }
        return false;
    }

    public static void BuildSnapshotFile(string dbPath, string outputPath)
    {
        var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
        var graphLogPath = Path.Combine(dbPath, "graph-log.bin");

        var nodeTables = new Dictionary<string, NodeTableData>(StringComparer.OrdinalIgnoreCase);
        var relTables = new Dictionary<string, RelTableData>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(graphDataPath))
        {
            _ = ColumnarTableSerializer.TryReadSnapshot(graphDataPath, nodeTables, relTables);
        }

        if (File.Exists(graphLogPath))
        {
            using var stream = new FileStream(graphLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 0)
            {
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                ApplyLog(reader, nodeTables, relTables);
            }
        }

        Directory.CreateDirectory(dbPath);
        ColumnarTableSerializer.WriteSnapshot(outputPath, nodeTables, relTables);
    }

    private static void ReadSnapshot(BinaryReader reader,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables)
    {
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
    }

    private static void ApplyLog(BinaryReader reader,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables)
    {
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            if (!TryReadLogRecord(reader, out var recordType, out var tableName, out var id, out var id2, out var props))
            {
                return;
            }

            if (recordType == 1)
            {
                if (!nodeTables.TryGetValue(tableName, out var table))
                {
                    table = new NodeTableData();
                    nodeTables[tableName] = table;
                }
                table.Upsert(id, props);
            }
            else if (recordType == 3)
            {
                if (nodeTables.TryGetValue(tableName, out var table))
                    table.Remove(id);
            }
            else if (recordType == 2)
            {
                if (!relTables.TryGetValue(tableName, out var table))
                {
                    table = new RelTableData();
                    relTables[tableName] = table;
                }
                var key = new EdgeKey(id, id2!);
                table.Upsert(key, props);
            }
            else if (recordType == 5)
            {
                if (!relTables.TryGetValue(tableName, out var table))
                {
                    table = new RelTableData();
                    relTables[tableName] = table;
                }
                var key = new EdgeKey(id, id2!);
                table.Insert(key, props);
            }
            else if (recordType == 4)
            {
                if (relTables.TryGetValue(tableName, out var table))
                {
                    var key = new EdgeKey(id, id2!);
                    table.Remove(key);
                }
            }
        }
    }

    private static void WriteSnapshot(BinaryWriter writer,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables)
    {
        writer.Write(nodeTables.Count);
        foreach (var (tableName, tableData) in nodeTables)
        {
            writer.Write(tableName);
            writer.Write(tableData.Count);
            foreach (var (id, properties) in tableData.EnumerateRows())
            {
                GraphDataSerializer.WriteValue(writer, id);
                GraphDataSerializer.WriteProperties(writer, properties);
            }
        }

        writer.Write(relTables.Count);
        foreach (var (tableName, tableData) in relTables)
        {
            writer.Write(tableName);
            writer.Write(tableData.Count);
            foreach (var (edgeKey, properties) in tableData.EnumerateRows())
            {
                GraphDataSerializer.WriteValue(writer, edgeKey.From);
                GraphDataSerializer.WriteValue(writer, edgeKey.To);
                GraphDataSerializer.WriteProperties(writer, properties);
            }
        }
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

    private static bool TryReadLogRecord(
        BinaryReader reader,
        out byte recordType,
        out string tableName,
        out object id,
        out object? id2,
        out Dictionary<string, object> props)
    {
        recordType = 0;
        tableName = string.Empty;
        id = string.Empty;
        id2 = null;
        props = new Dictionary<string, object>();

        try
        {
            recordType = reader.ReadByte();
        }
        catch (EndOfStreamException)
        {
            return false;
        }

        if (recordType == 0)
        {
            // Treat zero record type as padding/EOF for resiliency.
            // If the remainder is not all zeros we still stop to avoid
            // attempting to parse corrupted records.
            _ = IsRemainingZero(reader);
            return false;
        }

        try
        {
            tableName = reader.ReadString();
            id = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
            if (recordType == 1)
            {
                props = GraphDataSerializer.ReadProperties(reader);
                return true;
            }
            // Types 2 (rel upsert) and 5 (rel insert — written by relationship MERGE) share a wire shape:
            // id2 followed by properties. Both must be parsed here, or recovery aborts on a MERGE'd edge.
            if (recordType == 2 || recordType == 5)
            {
                id2 = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                props = GraphDataSerializer.ReadProperties(reader);
                return true;
            }
            if (recordType == 3)
            {
                return true;
            }
            if (recordType == 4)
            {
                id2 = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                return true;
            }

            throw new InvalidDataException($"Unknown graph log record type: {recordType}");
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }
}
