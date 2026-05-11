using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Storage;

namespace BogDb.Tests.Storage;

public class GraphStoreTests
{
    [Fact]
    public void GraphStore_Reads_From_Log_When_Snapshot_Missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var db = BogDatabase.Open(dir))
            {
                var props = new Dictionary<string, object> { ["name"] = "alice" };
                db.GraphLog.AppendNode("Person", 1L, props);

                if (File.Exists(graphDataPath))
                {
                    File.Delete(graphDataPath);
                }

                var store = new GraphStore(dir, inMemory: false);
                var nodes = new List<KeyValuePair<object, Dictionary<string, object>>>(store.EnumerateNodes("Person"));

                Assert.Single(nodes);
                Assert.Equal(1L, nodes[0].Key);
                Assert.Equal("alice", nodes[0].Value["name"]);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void GraphStore_Combines_Snapshot_And_Log()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var stream = new FileStream(graphDataPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1); // node tables
                writer.Write("Person");
                writer.Write(1); // rows
                GraphDataSerializer.WriteValue(writer, 1L);
                GraphDataSerializer.WriteProperties(writer, new Dictionary<string, object> { ["name"] = "alice" });
                writer.Write(0); // rel tables
            }

            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendNode("Person", 2L, new Dictionary<string, object> { ["name"] = "bob" });

                var store = new GraphStore(dir, inMemory: false);
                var nodes = new List<KeyValuePair<object, Dictionary<string, object>>>(store.EnumerateNodes("Person"));

                Assert.Equal(2, nodes.Count);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void GraphStore_NodeLogOverlay_UsesLatestCommittedValue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendNode("Person", 1L, new Dictionary<string, object> { ["name"] = "alice" });
                db.GraphLog.AppendNode("Person", 1L, new Dictionary<string, object> { ["name"] = "alicia" });

                var store = new GraphStore(dir, inMemory: false);
                var nodes = new List<KeyValuePair<object, Dictionary<string, object>>>(store.EnumerateNodes("Person"));

                Assert.Single(nodes);
                Assert.Equal(1L, nodes[0].Key);
                Assert.Equal("alicia", nodes[0].Value["name"]);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void GraphStore_RelSnapshotAndLogOverlay_UsesLatestCommittedValue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var stream = new FileStream(graphDataPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0); // node tables
                writer.Write(1); // rel tables
                writer.Write("KNOWS");
                writer.Write(1); // rows
                GraphDataSerializer.WriteValue(writer, 1L);
                GraphDataSerializer.WriteValue(writer, 2L);
                GraphDataSerializer.WriteProperties(writer, new Dictionary<string, object> { ["since"] = 2020L });
            }

            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendRel("KNOWS", 1L, 2L, new Dictionary<string, object> { ["since"] = 2024L });

                var store = new GraphStore(dir, inMemory: false);
                var rels = new List<KeyValuePair<EdgeKey, Dictionary<string, object>>>(store.EnumerateRels("KNOWS"));

                Assert.Single(rels);
                Assert.Equal(new EdgeKey(1L, 2L), rels[0].Key);
                Assert.Equal(2024L, rels[0].Value["since"]);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void GraphStore_NodeDeleteLogOverlay_RemovesSnapshotRow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var stream = new FileStream(graphDataPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1); // node tables
                writer.Write("Person");
                writer.Write(1); // rows
                GraphDataSerializer.WriteValue(writer, 1L);
                GraphDataSerializer.WriteProperties(writer, new Dictionary<string, object> { ["name"] = "alice" });
                writer.Write(0); // rel tables
            }

            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendNodeDelete("Person", 1L);

                var store = new GraphStore(dir, inMemory: false);
                var nodes = new List<KeyValuePair<object, Dictionary<string, object>>>(store.EnumerateNodes("Person"));

                Assert.Empty(nodes);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void GraphStore_RelDeleteLogOverlay_RemovesSnapshotRow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var stream = new FileStream(graphDataPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0); // node tables
                writer.Write(1); // rel tables
                writer.Write("KNOWS");
                writer.Write(1); // rows
                GraphDataSerializer.WriteValue(writer, 1L);
                GraphDataSerializer.WriteValue(writer, 2L);
                GraphDataSerializer.WriteProperties(writer, new Dictionary<string, object> { ["since"] = 2020L });
            }

            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendRelDelete("KNOWS", 1L, 2L);

                var store = new GraphStore(dir, inMemory: false);
                var rels = new List<KeyValuePair<EdgeKey, Dictionary<string, object>>>(store.EnumerateRels("KNOWS"));

                Assert.Empty(rels);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void GraphStore_NodeDeleteThenReinsertLogOverlay_UsesLatestVisibleRow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var stream = new FileStream(graphDataPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1); // node tables
                writer.Write("Person");
                writer.Write(1); // rows
                GraphDataSerializer.WriteValue(writer, 1L);
                GraphDataSerializer.WriteProperties(writer, new Dictionary<string, object> { ["name"] = "alice", ["age"] = 30L });
                writer.Write(0); // rel tables
            }

            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendNodeDelete("Person", 1L);
                db.GraphLog.AppendNode("Person", 1L, new Dictionary<string, object> { ["name"] = "alicia", ["age"] = 31L });

                var store = new GraphStore(dir, inMemory: false);
                var nodes = new List<KeyValuePair<object, Dictionary<string, object>>>(store.EnumerateNodes("Person"));

                Assert.Single(nodes);
                Assert.Equal(1L, nodes[0].Key);
                Assert.Equal("alicia", nodes[0].Value["name"]);
                Assert.Equal(31L, nodes[0].Value["age"]);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void GraphStore_RelDeleteThenReinsertLogOverlay_UsesLatestVisibleEdge()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var stream = new FileStream(graphDataPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0); // node tables
                writer.Write(1); // rel tables
                writer.Write("KNOWS");
                writer.Write(1); // rows
                GraphDataSerializer.WriteValue(writer, 1L);
                GraphDataSerializer.WriteValue(writer, 2L);
                GraphDataSerializer.WriteProperties(writer, new Dictionary<string, object> { ["since"] = 2020L, ["weight"] = 1L });
            }

            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendRelDelete("KNOWS", 1L, 2L);
                db.GraphLog.AppendRel("KNOWS", 1L, 2L, new Dictionary<string, object> { ["since"] = 2024L, ["weight"] = 2L });

                var store = new GraphStore(dir, inMemory: false);
                var rels = new List<KeyValuePair<EdgeKey, Dictionary<string, object>>>(store.EnumerateRels("KNOWS"));

                Assert.Single(rels);
                Assert.Equal(new EdgeKey(1L, 2L), rels[0].Key);
                Assert.Equal(2024L, rels[0].Value["since"]);
                Assert.Equal(2L, rels[0].Value["weight"]);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void GraphStore_NodeSnapshotAndRepeatedLogUpdates_UsesLatestCommittedValue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var stream = new FileStream(graphDataPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1); // node tables
                writer.Write("Person");
                writer.Write(1); // rows
                GraphDataSerializer.WriteValue(writer, 1L);
                GraphDataSerializer.WriteProperties(writer, new Dictionary<string, object> { ["name"] = "alice" });
                writer.Write(0); // rel tables
            }

            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendNode("Person", 1L, new Dictionary<string, object> { ["name"] = "alicia" });
                db.GraphLog.AppendNode("Person", 1L, new Dictionary<string, object> { ["name"] = "alina" });

                var store = new GraphStore(dir, inMemory: false);
                var nodes = new List<KeyValuePair<object, Dictionary<string, object>>>(store.EnumerateNodes("Person"));

                Assert.Single(nodes);
                Assert.Equal("alina", nodes[0].Value["name"]);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void GraphStore_RelSnapshotAndRepeatedLogUpdates_UsesLatestCommittedValue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var stream = new FileStream(graphDataPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(0); // node tables
                writer.Write(1); // rel tables
                writer.Write("KNOWS");
                writer.Write(1); // rows
                GraphDataSerializer.WriteValue(writer, 1L);
                GraphDataSerializer.WriteValue(writer, 2L);
                GraphDataSerializer.WriteProperties(writer, new Dictionary<string, object> { ["since"] = 2020L });
            }

            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendRel("KNOWS", 1L, 2L, new Dictionary<string, object> { ["since"] = 2024L });
                db.GraphLog.AppendRel("KNOWS", 1L, 2L, new Dictionary<string, object> { ["since"] = 2025L });

                var store = new GraphStore(dir, inMemory: false);
                var rels = new List<KeyValuePair<EdgeKey, Dictionary<string, object>>>(store.EnumerateRels("KNOWS"));

                Assert.Single(rels);
                Assert.Equal(2025L, rels[0].Value["since"]);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }

    [Fact]
    public void ScanNodeProperty_Uses_Disk_Backend()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bogdb-graphstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var graphDataPath = Path.Combine(dir, "graph-data.bin");
            using (var db = BogDatabase.Open(dir))
            {
                db.GraphLog.AppendNode("Person", 1L, new Dictionary<string, object> { ["name"] = "alice" });

                if (File.Exists(graphDataPath))
                {
                    File.Delete(graphDataPath);
                }

                var scan = new BogDb.Core.Processor.Operator.Scan.ScanNodeProperty(db, "Person", "p", 1);
                var ctx = new BogDb.Core.Processor.ExecutionContext(
                    BogDb.Core.Transaction.Transaction.DUMMY_TRANSACTION, db.BufferManager);

                Assert.True(scan.GetNextTuple(ctx));
                Assert.Equal(1L, ctx.CurrentNodeId);
                Assert.Equal("alice", ctx.CurrentNodeProperties?["name"]);
                Assert.False(scan.GetNextTuple(ctx));
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore cleanup failures */ }
        }
    }
}
