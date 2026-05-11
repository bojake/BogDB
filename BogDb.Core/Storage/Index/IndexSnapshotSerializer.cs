using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Core.Storage.Index;

internal static class IndexSnapshotSerializer
{
    private const int CurrentVersion = 1;

    internal sealed class IndexSnapshot
    {
        public Dictionary<string, TableIndexSnapshot> Tables { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class TableIndexSnapshot
    {
        public Dictionary<string, PropertyIndexSnapshot> Properties { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class PropertyIndexSnapshot
    {
        public List<KeyValuePair<object, IReadOnlyList<long>>> Entries { get; } = new();
    }

    public static void Write(
        string path,
        IReadOnlyDictionary<string, NodePropertyIndex> indexes,
        IReadOnlyDictionary<string, NodeTableData> nodeTables)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        writer.Write(CurrentVersion);
        writer.Write(indexes.Count);
        foreach (var (tableName, tableIndex) in indexes.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.Write(tableName);
            if (!nodeTables.TryGetValue(tableName, out var table))
            {
                writer.Write(0);
                continue;
            }

            var visibleEntriesByProperty =
                new Dictionary<string, Dictionary<object, List<long>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var propertyName in tableIndex.IndexedProperties)
                visibleEntriesByProperty[propertyName] =
                    new Dictionary<object, List<long>>(StructuralValueComparer.Instance);

            long nodeOffset = 0;
            foreach (var (_, props) in table.EnumerateRows())
            {
                foreach (var propertyName in tableIndex.IndexedProperties)
                {
                    if (!props.TryGetValue(propertyName, out var key) || key is null)
                        continue;

                    if (!visibleEntriesByProperty[propertyName].TryGetValue(key, out var nodeOffsets))
                    {
                        nodeOffsets = new List<long>();
                        visibleEntriesByProperty[propertyName][key] = nodeOffsets;
                    }

                    nodeOffsets.Add(nodeOffset);
                }

                nodeOffset++;
            }

            writer.Write(visibleEntriesByProperty.Count);
            foreach (var (propertyName, entries) in visibleEntriesByProperty.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.Write(propertyName);
                var materializedEntries = entries.ToArray();
                writer.Write(materializedEntries.Length);
                foreach (var (key, nodeOffsets) in materializedEntries)
                {
                    GraphDataSerializer.WriteValue(writer, key);
                    writer.Write(nodeOffsets.Count);
                    foreach (var currentNodeOffset in nodeOffsets)
                        writer.Write(currentNodeOffset);
                }
            }
        }
    }

    public static IndexSnapshot? TryRead(string path)
    {
        if (!File.Exists(path))
            return null;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        if (stream.Length == 0)
            return null;

        var version = reader.ReadInt32();
        if (version != CurrentVersion)
            return null;

        var snapshot = new IndexSnapshot();
        var tableCount = reader.ReadInt32();
        for (var tableIndex = 0; tableIndex < tableCount; tableIndex++)
        {
            var tableName = reader.ReadString();
            var tableSnapshot = new TableIndexSnapshot();

            var propertyCount = reader.ReadInt32();
            for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                var propertyName = reader.ReadString();
                var propertySnapshot = new PropertyIndexSnapshot();

                var entryCount = reader.ReadInt32();
                for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
                {
                    var key = GraphDataSerializer.ReadValue(reader);
                    if (key is null)
                        continue;

                    var nodeOffsetCount = reader.ReadInt32();
                    var nodeOffsets = new long[nodeOffsetCount];
                    for (var offsetIndex = 0; offsetIndex < nodeOffsetCount; offsetIndex++)
                        nodeOffsets[offsetIndex] = reader.ReadInt64();

                    propertySnapshot.Entries.Add(
                        new KeyValuePair<object, IReadOnlyList<long>>(key, nodeOffsets));
                }

                tableSnapshot.Properties[propertyName] = propertySnapshot;
            }

            snapshot.Tables[tableName] = tableSnapshot;
        }

        return snapshot;
    }
}
