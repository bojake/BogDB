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
                    if (!GraphLogFormat.TryReadRecord(reader, out var record))
                    {
                        yield break;
                    }

                    if (!string.Equals(record.TableName, tableName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (record.Type == GraphLogRecordType.NodeUpsert)
                    {
                        if (seenIds.Add(record.Id))
                            orderedIds.Add(record.Id);
                        propertiesById[record.Id] = record.Properties;
                    }
                    else if (record.Type == GraphLogRecordType.NodeDelete)
                    {
                        propertiesById.Remove(record.Id);
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
                    if (!GraphLogFormat.TryReadRecord(reader, out var record))
                    {
                        yield break;
                    }

                    if (!string.Equals(record.TableName, tableName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // RelInsert (from MERGE) surfaces the same edge as RelUpsert here — this enumeration
                    // is keyed by EdgeKey, so both register the edge with its latest properties.
                    if (record.Type is GraphLogRecordType.RelUpsert or GraphLogRecordType.RelInsert)
                    {
                        var edge = record.EdgeKey;
                        if (seenEdges.Add(edge))
                            orderedEdges.Add(edge);
                        propertiesByEdge[edge] = record.Properties;
                    }
                    else if (record.Type == GraphLogRecordType.RelDelete)
                    {
                        propertiesByEdge.Remove(record.EdgeKey);
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

    private static void ApplyLog(BinaryReader reader,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables)
    {
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            if (!GraphLogFormat.TryReadRecord(reader, out var record))
                return;

            GraphLogFormat.Apply(record, nodeTables, relTables);
        }
    }
}
