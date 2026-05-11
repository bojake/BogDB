using System;
using System.Collections.Generic;
using System.IO;
using BogDb.Core.Common;
using BogDb.Core.Main;
using BogDb.Core.Storage.Table;
using Xunit;

namespace BogDb.Tests.Storage.Table;

public class ColumnarTableSerializerTests
{
    [Fact]
    public void PersistedGraphData_UsesColumnarSnapshotHeader()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "bogdb-ng-colfmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dbPath);
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.INT64,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
                {
                    ["since"] = LogicalTypeID.INT64
                });
                conn.UpsertNode("Person", 1L, new Dictionary<string, object> { ["id"] = 1L, ["name"] = "Alice" });
                conn.UpsertNode("Person", 2L, new Dictionary<string, object> { ["id"] = 2L, ["name"] = "Bob" });
                conn.UpsertRelationship("KNOWS", 1L, 2L, new Dictionary<string, object> { ["since"] = 2024L });
                conn.Commit();
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            Assert.True(File.Exists(graphDataPath));
            using (var stream = new FileStream(graphDataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                Assert.Equal(0x53435A4B, reader.ReadInt32()); // 'KZCS'
                Assert.Equal(2, reader.ReadInt32()); // version
            }

            using var reopened = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopened);
            var qr = reopenedConn.Query("MATCH (n:Person) RETURN n.name ORDER BY n.name");
            Assert.True(qr.IsSuccess, qr.ErrorMessage);
            Assert.True(qr.HasNext());
            Assert.Equal("Alice", qr.GetNext().GetString(0));
            Assert.True(qr.HasNext());
            Assert.Equal("Bob", qr.GetNext().GetString(0));
        }
        finally
        {
            try { Directory.Delete(dbPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ColumnarSerializer_ReadsLegacyRowMajorSnapshot()
    {
        var file = Path.Combine(Path.GetTempPath(), "bogdb-ng-legacy-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                // legacy node section
                writer.Write(1); // table count
                writer.Write("Person");
                writer.Write(1); // row count
                GraphDataSerializer.WriteValue(writer, 1L);
                GraphDataSerializer.WriteProperties(writer, new Dictionary<string, object>
                {
                    ["id"] = 1L,
                    ["name"] = "LegacyAlice"
                });

                // legacy rel section
                writer.Write(0);
            }

            var nodeTables = new Dictionary<string, NodeTableData>(StringComparer.OrdinalIgnoreCase);
            var relTables = new Dictionary<string, RelTableData>(StringComparer.OrdinalIgnoreCase);

            var ok = ColumnarTableSerializer.TryReadSnapshot(file, nodeTables, relTables);
            Assert.True(ok);
            Assert.True(nodeTables.TryGetValue("Person", out var table));
            Assert.NotNull(table);
            Assert.True(table!.TryGetProperties(1L, out var props));
            Assert.NotNull(props);
            Assert.Equal("LegacyAlice", props!["name"]);
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }
}
