using System;
using System.Collections.Generic;
using System.IO;
using BogDb.Core.Common;
using BogDb.Core.Main;
using BogDb.Core.Storage.Index;
using BogDb.Core.Storage.Table;
using BogDb.Core.Transaction;
using Xunit;

namespace BogDb.Tests.Main;

public class BogDatabasePersistenceTests
{
    [Fact]
    public void Reopen_PreservesNodeData()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    { "id", LogicalTypeID.INT64 },
                    { "name", LogicalTypeID.STRING },
                    { "age", LogicalTypeID.INT64 }
                });
                conn.UpsertNode("Person", 42L, new Dictionary<string, object>
                {
                    { "name", "Ada" },
                    { "age", 37L }
                });
                conn.Commit();
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var node = reopenedConn.ReadNode("Person", 42L);
                Assert.NotNull(node);
                Assert.Equal("Ada", node["name"]);
                Assert.Equal(37L, (long)node["age"]);

                var result = reopenedConn.Query("MATCH (n:Person) RETURN n.name, n.age");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                var row = result.GetNext();
                Assert.Equal("Ada", row.GetString(0));
                Assert.Equal(37L, row.GetInt64(1));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesRelationshipDataForQueryExecution()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    { "id", LogicalTypeID.INT64 }
                });
                conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>());
                conn.UpsertNode("Person", 1L, new Dictionary<string, object>());
                conn.UpsertNode("Person", 2L, new Dictionary<string, object>());
                conn.UpsertRelationship("KNOWS", 1L, 2L, new Dictionary<string, object>());
                conn.Commit();
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var result = reopenedConn.Query("MATCH (a:Person)-[:KNOWS]->(b:Person) RETURN a.id, b.id");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                var row = result.GetNext();
                Assert.Equal(1L, row.GetInt64(0));
                Assert.Equal(2L, row.GetInt64(1));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesDeclaredArrayTypeMetadata()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                var create = conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])");
                Assert.True(create.IsSuccess, create.ErrorMessage);
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            {
                var entry = Assert.IsType<BogDb.Core.Catalog.NodeTableCatalogEntry>(
                    reopenedDb.Catalog.GetTableCatalogEntry(null, "Document", useInternal: false));
                var property = entry.GetProperty("embedding");
                Assert.Equal(LogicalTypeID.LIST, property.Type);
                Assert.Equal("FLOAT[]", property.DeclaredType);
                Assert.Equal(LogicalTypeID.FLOAT, property.LeafType);
                Assert.Equal(1, property.ListDepth);
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }

    [Fact]
    public void Reopen_NormalizesPersistedNodeArrayValuesToDeclaredFloatType()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);

                conn.BeginWriteTransaction();
                var tx = conn.ClientContext.ActiveTransaction!;
                db.NodeTables["Document"].Upsert(tx, "doc-1", new Dictionary<string, object>
                {
                    ["id"] = "doc-1",
                    ["embedding"] = new List<object?> { 1L, 2L, 3L }
                });
                conn.Commit();
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var node = reopenedConn.ReadNode("Document", "doc-1");
                Assert.NotNull(node);
                var embedding = Assert.IsType<List<object?>>(node!["embedding"]);
                Assert.Equal(1.0f, Assert.IsType<float>(embedding[0]));
                Assert.Equal(2.0f, Assert.IsType<float>(embedding[1]));
                Assert.Equal(3.0f, Assert.IsType<float>(embedding[2]));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }

    [Fact]
    public void Reopen_NormalizesPersistedNodeArrayValuesToDeclaredInt64Type()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                Assert.True(conn.Query("CREATE NODE TABLE Metric(id STRING, readings INT64[])").IsSuccess);

                conn.BeginWriteTransaction();
                var tx = conn.ClientContext.ActiveTransaction!;
                db.NodeTables["Metric"].Upsert(tx, "m-1", new Dictionary<string, object>
                {
                    ["id"] = "m-1",
                    ["readings"] = new List<object?> { 1, 2.0d, 3L }
                });
                conn.Commit();
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var node = reopenedConn.ReadNode("Metric", "m-1");
                Assert.NotNull(node);
                var readings = Assert.IsType<List<object?>>(node!["readings"]);
                Assert.Equal(1L, Assert.IsType<long>(readings[0]));
                Assert.Equal(2L, Assert.IsType<long>(readings[1]));
                Assert.Equal(3L, Assert.IsType<long>(readings[2]));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }

    [Fact]
    public void Reopen_NormalizesPersistedNodeArrayValuesToDeclaredNestedFloatType()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                Assert.True(conn.Query("CREATE NODE TABLE Tensor(id STRING, embedding FLOAT[][])").IsSuccess);

                conn.BeginWriteTransaction();
                var tx = conn.ClientContext.ActiveTransaction!;
                db.NodeTables["Tensor"].Upsert(tx, "t-1", new Dictionary<string, object>
                {
                    ["id"] = "t-1",
                    ["embedding"] = new List<object?>
                    {
                        new List<object?> { 1L, 2L },
                        new List<object?> { 3L, 4.5d }
                    }
                });
                conn.Commit();
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var node = reopenedConn.ReadNode("Tensor", "t-1");
                Assert.NotNull(node);
                var embedding = Assert.IsType<List<object?>>(node!["embedding"]);
                var first = Assert.IsType<List<object?>>(embedding[0]);
                var second = Assert.IsType<List<object?>>(embedding[1]);
                Assert.Equal(1.0f, Assert.IsType<float>(first[0]));
                Assert.Equal(2.0f, Assert.IsType<float>(first[1]));
                Assert.Equal(3.0f, Assert.IsType<float>(second[0]));
                Assert.Equal(4.5f, Assert.IsType<float>(second[1]));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }

    [Fact]
    public void Reopen_NormalizesPersistedRelArrayValuesToDeclaredFloatType()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING)").IsSuccess);
                Assert.True(conn.Query("CREATE REL TABLE LINKS(FROM Document TO Document, weights FLOAT[])").IsSuccess);

                conn.BeginWriteTransaction();
                conn.UpsertNode("Document", "doc-1", new Dictionary<string, object> { ["id"] = "doc-1" });
                conn.UpsertNode("Document", "doc-2", new Dictionary<string, object> { ["id"] = "doc-2" });
                var tx = conn.ClientContext.ActiveTransaction!;
                db.RelTables["LINKS"].Upsert(tx, new EdgeKey("doc-1", "doc-2"), new Dictionary<string, object>
                {
                    ["weights"] = new List<object?> { 4L, 5L, 6L }
                });
                conn.Commit();
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var result = reopenedConn.Query(
                    "MATCH (:Document {id:'doc-1'})-[r:LINKS]->(:Document {id:'doc-2'}) RETURN r.weights");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                var weights = Assert.IsType<List<object?>>(result.GetNext().GetValue(0));
                Assert.Equal(4.0f, Assert.IsType<float>(weights[0]));
                Assert.Equal(5.0f, Assert.IsType<float>(weights[1]));
                Assert.Equal(6.0f, Assert.IsType<float>(weights[2]));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }

    [Fact]
    public void Reopen_PreservesCommittedSchemaOnlyTransaction_AfterCrashBeforeDispose()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);

            Assert.NotNull(reopenedDb.Catalog.GetTableEntry("Person"));
            Assert.NotNull(reopenedDb.Catalog.GetTableEntry("KNOWS"));
            Assert.True(reopenedDb.NodeTables.ContainsKey("Person"));
            Assert.True(reopenedDb.RelTables.ContainsKey("KNOWS"));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesCommittedNodeDelete()
    {
        var dbPath = CreateTempDirectory();
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
                conn.UpsertNode("Person", 1L, new Dictionary<string, object> { ["id"] = 1L, ["name"] = "Alice" });
                conn.UpsertNode("Person", 2L, new Dictionary<string, object> { ["id"] = 2L, ["name"] = "Bob" });
                conn.Commit();

                Assert.True(conn.Query("BEGIN TRANSACTION").IsSuccess);
                var writer = conn.ClientContext.ActiveTransaction!;
                Assert.True(db.NodeTables["Person"].Remove(writer, 2L));
                Assert.True(conn.Query("COMMIT").IsSuccess);
                Assert.Single(db.NodeTables["Person"].EnumerateRows());
            }

            var logPath = Path.Combine(dbPath, "graph-log.bin");
            Assert.True(File.Exists(logPath));
            Assert.Equal(0, new FileInfo(logPath).Length);

            var nodeTables = new Dictionary<string, NodeTableData>(StringComparer.OrdinalIgnoreCase);
            var relTables = new Dictionary<string, RelTableData>(StringComparer.OrdinalIgnoreCase);
            Assert.True(ColumnarTableSerializer.TryReadSnapshot(Path.Combine(dbPath, "graph-data.bin"), nodeTables, relTables));
            Assert.Single(nodeTables["Person"].EnumerateRows());

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var result = reopenedConn.Query("MATCH (p:Person) RETURN p.id ORDER BY p.id");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                Assert.Equal(1L, result.GetNext().GetInt64(0));
                Assert.Null(reopenedConn.ReadNode("Person", 2L));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesCommittedSetPropertyUpdate()
    {
        var dbPath = CreateTempDirectory();
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
                conn.UpsertNode("Person", 1L, new Dictionary<string, object> { ["id"] = 1L, ["name"] = "Alice" });
                conn.Commit();

                Assert.True(conn.Query("BEGIN TRANSACTION").IsSuccess);
                var writer = conn.ClientContext.ActiveTransaction!;
                Assert.True(db.NodeTables["Person"].SetProperty(writer, 1L, "name", "Alicia"));
                Assert.True(conn.Query("COMMIT").IsSuccess);
                var committedNode = conn.ReadNode("Person", 1L);
                Assert.NotNull(committedNode);
                Assert.Equal("Alicia", committedNode!["name"]);
            }

            var logPath = Path.Combine(dbPath, "graph-log.bin");
            Assert.True(File.Exists(logPath));
            Assert.Equal(0, new FileInfo(logPath).Length);

            var nodeTables = new Dictionary<string, NodeTableData>(StringComparer.OrdinalIgnoreCase);
            var relTables = new Dictionary<string, RelTableData>(StringComparer.OrdinalIgnoreCase);
            Assert.True(ColumnarTableSerializer.TryReadSnapshot(Path.Combine(dbPath, "graph-data.bin"), nodeTables, relTables));
            var persistedNode = Assert.Single(nodeTables["Person"].EnumerateRows());
            Assert.Equal("Alicia", persistedNode.Value["name"]);

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var node = reopenedConn.ReadNode("Person", 1L);
                Assert.NotNull(node);
                Assert.Equal("Alicia", node!["name"]);

                var result = reopenedConn.Query("MATCH (p:Person) RETURN p.name");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.True(result.HasNext());
                Assert.Equal("Alicia", result.GetNext().GetString(0));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_RebuildsPersistedNodeIndexFromCurrentCommittedValues()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice"
                });
                conn.Commit();

                conn.CreateIndex("Person", "name");
                conn.UpsertNodeById("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alicia"
                });

                Assert.True(db.TryIndexLookup("Person", "name", "Alicia", out _));
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                Assert.True(reopenedDb.TryIndexLookup("Person", "name", "Alicia", out var offset));
                Assert.False(reopenedDb.TryIndexLookup("Person", "name", "Alice", out _));

                var node = reopenedConn.ReadNode("Person", "alice");
                Assert.NotNull(node);
                Assert.Equal("Alicia", node!["name"]);

                var tx = new BogDb.Core.Transaction.Transaction(
                    BogDb.Core.Transaction.TransactionType.READ_ONLY,
                    id: 99,
                    startTS: 100);
                var scan = new BogDb.Core.Processor.Operator.Scan.PhysicalIndexScanNode(
                    reopenedDb,
                    "Person",
                    "name",
                    "Alicia",
                    "p",
                    id: 99);
                var ctx = new BogDb.Core.Processor.ExecutionContext(tx, reopenedDb.BufferManager);
                Assert.True(scan.GetNextTuple(ctx));
                Assert.Equal("alice", ctx.CurrentNodeId);
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void PersistState_WritesIndexOwnedSnapshotEntries()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice"
                });
                conn.UpsertNode("Person", "bob", new Dictionary<string, object>
                {
                    ["id"] = "bob",
                    ["name"] = "Bob"
                });
                conn.Commit();

                conn.CreateIndex("Person", "name");
                conn.Query("MATCH (p:Person {id:'alice'}) SET p.name = 'Alicia'");
            }

            var indexSnapshotPath = Path.Combine(dbPath, "index-data.bin");
            Assert.True(File.Exists(indexSnapshotPath));
            Assert.True(new FileInfo(indexSnapshotPath).Length > 0);

            var snapshot = IndexSnapshotSerializer.TryRead(indexSnapshotPath);
            Assert.NotNull(snapshot);
            Assert.True(snapshot!.Tables.TryGetValue("Person", out var tableSnapshot));
            Assert.True(tableSnapshot.Properties.TryGetValue("name", out var propertySnapshot));
            Assert.Contains(propertySnapshot.Entries, entry => Equals(entry.Key, "Alicia"));
            Assert.DoesNotContain(propertySnapshot.Entries, entry => Equals(entry.Key, "Alice"));

            using var reopenedDb = BogDatabase.Open(dbPath);
            Assert.True(reopenedDb.TryIndexLookup("Person", "name", "Alicia", out _));
            Assert.False(reopenedDb.TryIndexLookup("Person", "name", "Alice", out _));
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }

    [Fact]
    public void Reopen_FallsBackToCatalogRebuild_WhenIndexSnapshotIsMissing()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice"
                });
                conn.Commit();

                conn.CreateIndex("Person", "name");
                conn.Query("MATCH (p:Person {id:'alice'}) SET p.name = 'Alicia'");
            }

            File.Delete(Path.Combine(dbPath, "index-data.bin"));

            using var reopenedDb = BogDatabase.Open(dbPath);
            Assert.True(reopenedDb.TryIndexLookup("Person", "name", "Alicia", out _));
            Assert.False(reopenedDb.TryIndexLookup("Person", "name", "Alice", out _));
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }

    [Fact]
    public void Reopen_UsesSnapshotBackedIndex_AndAcceptsPostReopenWrites()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice"
                });
                conn.Commit();

                conn.CreateIndex("Person", "name");
                Assert.True(db.TryIndexLookup("Person", "name", "Alice", out _));
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                Assert.True(reopenedDb.TryIndexLookup("Person", "name", "Alice", out _));

                var update = reopenedConn.Query("MATCH (p:Person {id:'alice'}) SET p.name = 'Alicia'");
                Assert.True(update.IsSuccess, update.ErrorMessage);

                Assert.True(reopenedDb.TryIndexLookup("Person", "name", "Alicia", out _));
                Assert.False(reopenedDb.TryIndexLookup("Person", "name", "Alice", out _));

                var result = reopenedConn.Query("MATCH (p:Person {name:'Alicia'}) RETURN p.id");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                Assert.Equal("alice", result.GetNext().GetString(0));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }

    [Fact]
    public void Reopen_RebuildsTypedFloatArrayIndex_WithStructuralLookup()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);

                conn.BeginWriteTransaction();
                var tx = conn.ClientContext.ActiveTransaction!;
                db.NodeTables["Document"].Upsert(tx, "doc-1", new Dictionary<string, object>
                {
                    ["id"] = "doc-1",
                    ["embedding"] = new List<object?> { 1L, 2L, 3L }
                });
                conn.Commit();

                conn.CreateIndex("Document", "embedding");
                Assert.True(db.TryIndexLookup("Document", "embedding", new List<object?> { 1.0d, 2.0d, 3.0d }, out _));
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                Assert.True(reopenedDb.TryIndexLookup("Document", "embedding", new List<object?> { 1.0d, 2.0d, 3.0d }, out _));

                var scan = new BogDb.Core.Processor.Operator.Scan.PhysicalIndexScanNode(
                    reopenedDb,
                    "Document",
                    "embedding",
                    new List<object?> { 1.0d, 2.0d, 3.0d },
                    "d",
                    id: 100);
                var ctx = new BogDb.Core.Processor.ExecutionContext(
                    new BogDb.Core.Transaction.Transaction(BogDb.Core.Transaction.TransactionType.READ_ONLY, id: 100, startTS: 100),
                    reopenedDb.BufferManager);
                Assert.True(scan.GetNextTuple(ctx));
                Assert.Equal("doc-1", ctx.CurrentNodeId);
                var embedding = Assert.IsType<List<object?>>(ctx.CurrentNodeProperties!["embedding"]);
                Assert.Equal(1.0f, Assert.IsType<float>(embedding[0]));
                Assert.Equal(2.0f, Assert.IsType<float>(embedding[1]));
                Assert.Equal(3.0f, Assert.IsType<float>(embedding[2]));

                var result = reopenedConn.Query("MATCH (d:Document {embedding:[1.0, 2.0, 3.0]}) RETURN d.id");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                Assert.Equal("doc-1", result.GetNext().GetString(0));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_RebuildsTypedNestedFloatArrayIndex_WithStructuralLookup()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                Assert.True(conn.Query("CREATE NODE TABLE Tensor(id STRING, embedding FLOAT[][])").IsSuccess);

                conn.BeginWriteTransaction();
                var tx = conn.ClientContext.ActiveTransaction!;
                db.NodeTables["Tensor"].Upsert(tx, "t-1", new Dictionary<string, object>
                {
                    ["id"] = "t-1",
                    ["embedding"] = new List<object?>
                    {
                        new List<object?> { 1L, 2L },
                        new List<object?> { 3L, 4L }
                    }
                });
                conn.Commit();

                conn.CreateIndex("Tensor", "embedding");
                Assert.True(reopenedLikeLookup(db));
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                Assert.True(reopenedLikeLookup(reopenedDb));

                var result = reopenedConn.Query("MATCH (t:Tensor {embedding:[[1.0, 2.0], [3.0, 4.0]]}) RETURN t.id");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                Assert.Equal("t-1", result.GetNext().GetString(0));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }

        static bool reopenedLikeLookup(BogDatabase db)
            => db.TryIndexLookup("Tensor", "embedding", new List<object?>
            {
                new List<object?> { 1.0d, 2.0d },
                new List<object?> { 3.0d, 4.0d }
            }, out _);
    }

    [Fact]
    public void Reopen_PreservesCreatedIndex_AfterCrashBeforeDispose()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.UpsertNode("Person", "alice", new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            });
            conn.Commit();

            conn.CreateIndex("Person", "name");
            Assert.True(db.TryIndexLookup("Person", "name", "Alice", out _));

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            Assert.True(reopenedDb.TryIndexLookup("Person", "name", "Alice", out var offset));
            var result = reopenedConn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            Assert.Equal("alice", result.GetNext().GetString(0));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesLatestVisibleNodeAcrossTwoDeleteReinsertCycles()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING,
                    ["score"] = LogicalTypeID.INT64
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice",
                    ["score"] = 10L
                });
                conn.Commit();

                conn.BeginWriteTransaction();
                var delete1 = conn.Query("MATCH (p:Person {id:'alice'}) DETACH DELETE p");
                Assert.True(delete1.IsSuccess, delete1.ErrorMessage);
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alicia",
                    ["score"] = 20L
                });
                conn.Commit();

                conn.BeginWriteTransaction();
                var delete2 = conn.Query("MATCH (p:Person {id:'alice'}) DETACH DELETE p");
                Assert.True(delete2.IsSuccess, delete2.ErrorMessage);
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alina",
                    ["score"] = 30L
                });
                conn.Commit();
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var node = reopenedConn.ReadNode("Person", "alice");
                Assert.NotNull(node);
                Assert.Equal("Alina", node!["name"]);
                Assert.Equal(30L, (long)node["score"]);

                var result = reopenedConn.Query(
                    "MATCH (p:Person {id:'alice'}) RETURN p.name AS n, p.score AS s");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                var row = result.GetNext();
                Assert.Equal("Alina", row.GetString(0));
                Assert.Equal(30L, row.GetInt64(1));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesLatestVisibleRelationshipAcrossTwoDeleteReinsertCycles()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
                {
                    ["weight"] = LogicalTypeID.INT64
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice", ["name"] = "Alice" });
                conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob", ["name"] = "Bob" });
                conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object> { ["weight"] = 1L });
                conn.Commit();

                conn.BeginWriteTransaction();
                var delete1 = conn.Query("MATCH (:Person {id:'alice'})-[r:KNOWS]->(:Person {id:'bob'}) DELETE r");
                Assert.True(delete1.IsSuccess, delete1.ErrorMessage);
                conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object> { ["weight"] = 2L });
                conn.Commit();

                conn.BeginWriteTransaction();
                var delete2 = conn.Query("MATCH (:Person {id:'alice'})-[r:KNOWS]->(:Person {id:'bob'}) DELETE r");
                Assert.True(delete2.IsSuccess, delete2.ErrorMessage);
                conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object> { ["weight"] = 3L });
                conn.Commit();
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var result = reopenedConn.Query(
                    "MATCH (:Person {id:'alice'})-[r:KNOWS]->(:Person {id:'bob'}) RETURN r.weight AS w");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                Assert.Equal(3L, result.GetNext().GetInt64(0));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_RebuildsNodeIndexAgainstLatestVisibleRowAcrossDeleteReinsertCycles()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice"
                });
                conn.Commit();

                conn.CreateIndex("Person", "name");

                conn.BeginWriteTransaction();
                var delete1 = conn.Query("MATCH (p:Person {id:'alice'}) DETACH DELETE p");
                Assert.True(delete1.IsSuccess, delete1.ErrorMessage);
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alicia"
                });
                conn.Commit();

                conn.BeginWriteTransaction();
                var delete2 = conn.Query("MATCH (p:Person {id:'alice'}) DETACH DELETE p");
                Assert.True(delete2.IsSuccess, delete2.ErrorMessage);
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alina"
                });
                conn.Commit();
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                Assert.True(reopenedDb.TryIndexLookup("Person", "name", "Alina", out var offset));
                Assert.False(reopenedDb.TryIndexLookup("Person", "name", "Alice", out _));
                Assert.False(reopenedDb.TryIndexLookup("Person", "name", "Alicia", out _));

                var tx = new BogDb.Core.Transaction.Transaction(
                    BogDb.Core.Transaction.TransactionType.READ_ONLY,
                    id: 100,
                    startTS: 100);
                var scan = new BogDb.Core.Processor.Operator.Scan.PhysicalIndexScanNode(
                    reopenedDb,
                    "Person",
                    "name",
                    "Alina",
                    "p",
                    id: 100);
                var ctx = new BogDb.Core.Processor.ExecutionContext(tx, reopenedDb.BufferManager);
                Assert.True(scan.GetNextTuple(ctx));
                Assert.Equal("alice", ctx.CurrentNodeId);
                Assert.Equal("Alina", ctx.CurrentNodeProperties!["name"]);
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DoesNotReplayRolledBackDirectNodeUpsertFromGraphLog()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        BogConnection? conn = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.Commit();

            conn.BeginWriteTransaction();
            conn.UpsertNode("Person", "alice", new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            });
            var rollback = conn.Query("ROLLBACK");
            Assert.True(rollback.IsSuccess, rollback.ErrorMessage);

            var logPath = Path.Combine(dbPath, "graph-log.bin");
            var walPath = Path.Combine(dbPath, "data.wal");
            Assert.True(File.Exists(logPath));
            Assert.True(File.Exists(walPath));
            Assert.Equal(0, new FileInfo(logPath).Length);
            // Logical WAL: header (17 bytes) + BEGIN_TRANSACTION (1 byte) may persist
            Assert.True(new FileInfo(walPath).Length <= 18);

            SimulateCrashClose(db);
            conn = null;
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);
            Assert.Null(reopenedConn.ReadNode("Person", "alice"));

            var result = reopenedConn.Query("MATCH (p:Person) RETURN p.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(0UL, result.GetNumTuples());
        }
        finally
        {
            conn?.Dispose();
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesCommittedDirectNodeUpsert_AfterLaterRolledBackDirectUpsert()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        BogConnection? conn = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.Commit();

            conn.UpsertNodeById("Person", "alice", new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            });

            conn.BeginWriteTransaction();
            conn.UpsertNode("Person", "bob", new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob"
            });
            var rollback = conn.Query("ROLLBACK");
            Assert.True(rollback.IsSuccess, rollback.ErrorMessage);

            SimulateCrashClose(db);
            conn = null;
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);
            var alice = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(alice);
            Assert.Equal("Alice", alice!["name"]);
            Assert.Null(reopenedConn.ReadNode("Person", "bob"));

            var result = reopenedConn.Query("MATCH (p:Person) RETURN p.id ORDER BY p.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            Assert.Equal("alice", result.GetNext().GetString(0));
        }
        finally
        {
            conn?.Dispose();
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Checkpoint_AfterRolledBackDirectNodeUpsert_DoesNotPersistRolledBackRow()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.Commit();

                conn.UpsertNodeById("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice"
                });

                conn.BeginWriteTransaction();
                conn.UpsertNode("Person", "bob", new Dictionary<string, object>
                {
                    ["id"] = "bob",
                    ["name"] = "Bob"
                });
                var rollback = conn.Query("ROLLBACK");
                Assert.True(rollback.IsSuccess, rollback.ErrorMessage);

                var checkpoint = conn.Query("CHECKPOINT");
                Assert.True(checkpoint.IsSuccess, checkpoint.ErrorMessage);
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var result = reopenedConn.Query("MATCH (p:Person) RETURN p.id ORDER BY p.id");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(1UL, result.GetNumTuples());
                Assert.Equal("alice", result.GetNext().GetString(0));
                Assert.Null(reopenedConn.ReadNode("Person", "bob"));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesPersistedState_WithTrailingCheckpointMarkerAndGarbage()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.Commit();

                conn.UpsertNodeById("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice"
                });
            }

            var walPath = Path.Combine(dbPath, "data.wal");
            using (var wal = new BogDb.Core.Transaction.WAL(dbPath, readOnly: false, inMemory: false))
            {
                wal.LogAndFlushCheckpoint();
            }

            var lengthBeforeGarbage = new FileInfo(walPath).Length;
            using (var fs = new FileStream(walPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                fs.Write(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
            }

            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var reopenedConn = new BogConnection(reopenedDb))
            {
                var alice = reopenedConn.ReadNode("Person", "alice");
                Assert.NotNull(alice);
                Assert.Equal("Alice", alice!["name"]);

                // After checkpoint replay, WAL is truncated to 0
                Assert.Equal(0, new FileInfo(walPath).Length);
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DoesNotLetStaleGraphLogOverrideNewerSnapshotState()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var directTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var initialProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice-v1"
            };
            db.NodeTables["Person"].Upsert(directTx, "alice", initialProps);
            db.GraphLog.AppendNode("Person", "alice", initialProps);
            db.TransactionManager.Commit(conn.ClientContext, directTx);

            var updateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].SetProperty(updateTx, "alice", "name", "Alice-v2"));
            db.TransactionManager.Commit(conn.ClientContext, updateTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            ColumnarTableSerializer.WriteSnapshot(graphDataPath, db.NodeTables, db.RelTables);
            var walPath = Path.Combine(dbPath, "data.wal");
            var walTimestamp = File.GetLastWriteTimeUtc(walPath);
            File.SetLastWriteTimeUtc(graphDataPath, walTimestamp.AddSeconds(1));

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            Assert.True(File.Exists(graphLogPath));
            Assert.True(new FileInfo(graphLogPath).Length > 0);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);
            var alice = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(alice);
            Assert.Equal("Alice-v2", alice!["name"]);
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_QueryPath_IgnoresSnapshotRow_WhenNewerCommittedNodeDeleteExistsInGraphLog()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var insertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var initialProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(insertTx, "alice", initialProps);
            db.GraphLog.AppendNode("Person", "alice", initialProps);
            db.TransactionManager.Commit(conn.ClientContext, insertTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            ColumnarTableSerializer.WriteSnapshot(graphDataPath, db.NodeTables, db.RelTables);

            var deleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].Remove(deleteTx, "alice"));
            db.TransactionManager.Commit(conn.ClientContext, deleteTx);

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            var walPath = Path.Combine(dbPath, "data.wal");
            File.SetLastWriteTimeUtc(graphDataPath, File.GetLastWriteTimeUtc(walPath).AddSeconds(-1));
            Assert.True(new FileInfo(graphLogPath).Length > 0);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            Assert.Null(reopenedConn.ReadNode("Person", "alice"));

            var result = reopenedConn.Query("MATCH (p:Person) RETURN p.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(0UL, result.GetNumTuples());
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_QueryPath_IgnoresSnapshotRel_WhenNewerCommittedRelDeleteExistsInGraphLog()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            foreach (var (id, name) in new[] { ("alice", "Alice"), ("bob", "Bob") })
            {
                var nodeTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
                var props = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name
                };
                db.NodeTables["Person"].Upsert(nodeTx, id, props);
                db.GraphLog.AppendNode("Person", id, props);
                db.TransactionManager.Commit(conn.ClientContext, nodeTx);
            }

            var relInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relProps = new Dictionary<string, object>
            {
                ["since"] = 2024L
            };
            db.RelTables["KNOWS"].Upsert(relInsertTx, new EdgeKey("alice", "bob"), relProps);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relProps);
            db.TransactionManager.Commit(conn.ClientContext, relInsertTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            ColumnarTableSerializer.WriteSnapshot(graphDataPath, db.NodeTables, db.RelTables);

            var relDeleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.RelTables["KNOWS"].Remove(relDeleteTx, new EdgeKey("alice", "bob")));
            db.TransactionManager.Commit(conn.ClientContext, relDeleteTx);

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            var walPath = Path.Combine(dbPath, "data.wal");
            File.SetLastWriteTimeUtc(graphDataPath, File.GetLastWriteTimeUtc(walPath).AddSeconds(-1));
            Assert.True(new FileInfo(graphLogPath).Length > 0);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (:Person {id:'alice'})-[r:KNOWS]->(:Person {id:'bob'}) RETURN r.since");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(0UL, result.GetNumTuples());
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_QueryPath_UsesLatestVisibleNode_WhenSnapshotRowIsDeletedThenReinsertedInGraphLog()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING,
                ["age"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            var insertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var initialProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice",
                ["age"] = 30L
            };
            db.NodeTables["Person"].Upsert(insertTx, "alice", initialProps);
            db.GraphLog.AppendNode("Person", "alice", initialProps);
            db.TransactionManager.Commit(conn.ClientContext, insertTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            ColumnarTableSerializer.WriteSnapshot(graphDataPath, db.NodeTables, db.RelTables);

            var deleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].Remove(deleteTx, "alice"));
            db.TransactionManager.Commit(conn.ClientContext, deleteTx);

            var reinsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var reinsertProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alicia",
                ["age"] = 31L
            };
            db.NodeTables["Person"].Upsert(reinsertTx, "alice", reinsertProps);
            db.GraphLog.AppendNode("Person", "alice", reinsertProps);
            db.TransactionManager.Commit(conn.ClientContext, reinsertTx);

            var walPath = Path.Combine(dbPath, "data.wal");
            File.SetLastWriteTimeUtc(graphDataPath, File.GetLastWriteTimeUtc(walPath).AddSeconds(-1));

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var node = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(node);
            Assert.Equal("Alicia", node!["name"]);
            Assert.Equal(31L, (long)node["age"]);

            var result = reopenedConn.Query("MATCH (p:Person {id:'alice'}) RETURN p.name, p.age");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal("Alicia", row.GetString(0));
            Assert.Equal(31L, row.GetInt64(1));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_QueryPath_UsesLatestVisibleRel_WhenSnapshotEdgeIsDeletedThenReinsertedInGraphLog()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64,
                ["weight"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            foreach (var (id, name) in new[] { ("alice", "Alice"), ("bob", "Bob") })
            {
                var nodeTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
                var props = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name
                };
                db.NodeTables["Person"].Upsert(nodeTx, id, props);
                db.GraphLog.AppendNode("Person", id, props);
                db.TransactionManager.Commit(conn.ClientContext, nodeTx);
            }

            var relInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relProps = new Dictionary<string, object>
            {
                ["since"] = 2020L,
                ["weight"] = 1L
            };
            db.RelTables["KNOWS"].Upsert(relInsertTx, new EdgeKey("alice", "bob"), relProps);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relProps);
            db.TransactionManager.Commit(conn.ClientContext, relInsertTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            ColumnarTableSerializer.WriteSnapshot(graphDataPath, db.NodeTables, db.RelTables);

            var relDeleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.RelTables["KNOWS"].Remove(relDeleteTx, new EdgeKey("alice", "bob")));
            db.TransactionManager.Commit(conn.ClientContext, relDeleteTx);

            var relReinsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relReinsertProps = new Dictionary<string, object>
            {
                ["since"] = 2024L,
                ["weight"] = 2L
            };
            db.RelTables["KNOWS"].Upsert(relReinsertTx, new EdgeKey("alice", "bob"), relReinsertProps);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relReinsertProps);
            db.TransactionManager.Commit(conn.ClientContext, relReinsertTx);

            var walPath = Path.Combine(dbPath, "data.wal");
            File.SetLastWriteTimeUtc(graphDataPath, File.GetLastWriteTimeUtc(walPath).AddSeconds(-1));

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (:Person {id:'alice'})-[r:KNOWS]->(:Person {id:'bob'}) RETURN r.since, r.weight");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal(2024L, row.GetInt64(0));
            Assert.Equal(2L, row.GetInt64(1));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_QueryPath_UsesLatestCommittedNodeValue_AcrossRepeatedGraphLogUpdates()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var insertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var initialProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(insertTx, "alice", initialProps);
            db.GraphLog.AppendNode("Person", "alice", initialProps);
            db.TransactionManager.Commit(conn.ClientContext, insertTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            ColumnarTableSerializer.WriteSnapshot(graphDataPath, db.NodeTables, db.RelTables);

            foreach (var value in new[] { "Alicia", "Alina" })
            {
                var updateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
                Assert.True(db.NodeTables["Person"].SetProperty(updateTx, "alice", "name", value));
                db.TransactionManager.Commit(conn.ClientContext, updateTx);
            }

            var walPath = Path.Combine(dbPath, "data.wal");
            File.SetLastWriteTimeUtc(graphDataPath, File.GetLastWriteTimeUtc(walPath).AddSeconds(-1));

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var node = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(node);
            Assert.Equal("Alina", node!["name"]);
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_QueryPath_UsesLatestCommittedRelValue_AcrossRepeatedGraphLogUpdates()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            foreach (var (id, name) in new[] { ("alice", "Alice"), ("bob", "Bob") })
            {
                var nodeTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
                var props = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name
                };
                db.NodeTables["Person"].Upsert(nodeTx, id, props);
                db.GraphLog.AppendNode("Person", id, props);
                db.TransactionManager.Commit(conn.ClientContext, nodeTx);
            }

            var relInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relProps = new Dictionary<string, object>
            {
                ["since"] = 2020L
            };
            db.RelTables["KNOWS"].Upsert(relInsertTx, new EdgeKey("alice", "bob"), relProps);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relProps);
            db.TransactionManager.Commit(conn.ClientContext, relInsertTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            ColumnarTableSerializer.WriteSnapshot(graphDataPath, db.NodeTables, db.RelTables);

            foreach (var value in new[] { 2024L, 2025L })
            {
                var relUpdateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
                Assert.True(db.RelTables["KNOWS"].SetProperty(relUpdateTx, new EdgeKey("alice", "bob"), "since", value));
                db.TransactionManager.Commit(conn.ClientContext, relUpdateTx);
            }

            var walPath = Path.Combine(dbPath, "data.wal");
            File.SetLastWriteTimeUtc(graphDataPath, File.GetLastWriteTimeUtc(walPath).AddSeconds(-1));

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (:Person {id:'alice'})-[r:KNOWS]->(:Person {id:'bob'}) RETURN r.since");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            Assert.Equal(2025L, result.GetNext().GetInt64(0));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteNodeCommitWithoutPersistState_RemainsQueryVisible()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var directTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var props = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(directTx, "alice", props);
            db.GraphLog.AppendNode("Person", "alice", props);
            db.TransactionManager.Commit(conn.ClientContext, directTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (p:Person {name:'Alice'}) RETURN p.id, p.name");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal("alice", row.GetString(0));
            Assert.Equal("Alice", row.GetString(1));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteRelationshipCommitWithoutPersistState_RemainsTraversalVisible()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            var aliceTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var aliceProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(aliceTx, "alice", aliceProps);
            db.GraphLog.AppendNode("Person", "alice", aliceProps);
            db.TransactionManager.Commit(conn.ClientContext, aliceTx);

            var bobTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var bobProps = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob"
            };
            db.NodeTables["Person"].Upsert(bobTx, "bob", bobProps);
            db.GraphLog.AppendNode("Person", "bob", bobProps);
            db.TransactionManager.Commit(conn.ClientContext, bobTx);

            var relTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relProps = new Dictionary<string, object>
            {
                ["since"] = 2024L
            };
            db.RelTables["KNOWS"].Upsert(relTx, new EdgeKey("alice", "bob"), relProps);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relProps);
            db.TransactionManager.Commit(conn.ClientContext, relTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal("alice", row.GetString(0));
            Assert.Equal("bob", row.GetString(1));
            Assert.Equal(2024L, row.GetInt64(2));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteNodePropertyUpdateWithoutPersistState_RemainsQueryVisible()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var insertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var props = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(insertTx, "alice", props);
            db.GraphLog.AppendNode("Person", "alice", props);
            db.TransactionManager.Commit(conn.ClientContext, insertTx);

            var updateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].SetProperty(updateTx, "alice", "name", "Alicia"));
            db.TransactionManager.Commit(conn.ClientContext, updateTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (p:Person {name:'Alicia'}) RETURN p.id, p.name");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal("alice", row.GetString(0));
            Assert.Equal("Alicia", row.GetString(1));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteNodeDeleteWithoutPersistState_RemainsDeleted()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var insertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var props = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(insertTx, "alice", props);
            db.GraphLog.AppendNode("Person", "alice", props);
            db.TransactionManager.Commit(conn.ClientContext, insertTx);

            var deleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].Remove(deleteTx, "alice"));
            db.TransactionManager.Commit(conn.ClientContext, deleteTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            Assert.Null(reopenedConn.ReadNode("Person", "alice"));
            var result = reopenedConn.Query(
                "MATCH (p:Person {id:'alice'}) RETURN p.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(0UL, result.GetNumTuples());
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteRelationshipPropertyUpdateWithoutPersistState_RemainsTraversalVisible()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            var aliceTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var aliceProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(aliceTx, "alice", aliceProps);
            db.GraphLog.AppendNode("Person", "alice", aliceProps);
            db.TransactionManager.Commit(conn.ClientContext, aliceTx);

            var bobTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var bobProps = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob"
            };
            db.NodeTables["Person"].Upsert(bobTx, "bob", bobProps);
            db.GraphLog.AppendNode("Person", "bob", bobProps);
            db.TransactionManager.Commit(conn.ClientContext, bobTx);

            var relInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relProps = new Dictionary<string, object>
            {
                ["since"] = 2020L
            };
            db.RelTables["KNOWS"].Upsert(relInsertTx, new EdgeKey("alice", "bob"), relProps);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relProps);
            db.TransactionManager.Commit(conn.ClientContext, relInsertTx);

            var relUpdateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.RelTables["KNOWS"].SetProperty(relUpdateTx, new EdgeKey("alice", "bob"), "since", 2024L));
            db.TransactionManager.Commit(conn.ClientContext, relUpdateTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal("alice", row.GetString(0));
            Assert.Equal("bob", row.GetString(1));
            Assert.Equal(2024L, row.GetInt64(2));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteRelationshipDeleteWithoutPersistState_RemainsDeleted()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            var aliceTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var aliceProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(aliceTx, "alice", aliceProps);
            db.GraphLog.AppendNode("Person", "alice", aliceProps);
            db.TransactionManager.Commit(conn.ClientContext, aliceTx);

            var bobTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var bobProps = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob"
            };
            db.NodeTables["Person"].Upsert(bobTx, "bob", bobProps);
            db.GraphLog.AppendNode("Person", "bob", bobProps);
            db.TransactionManager.Commit(conn.ClientContext, bobTx);

            var relInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relProps = new Dictionary<string, object>
            {
                ["since"] = 2024L
            };
            db.RelTables["KNOWS"].Upsert(relInsertTx, new EdgeKey("alice", "bob"), relProps);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relProps);
            db.TransactionManager.Commit(conn.ClientContext, relInsertTx);

            var relDeleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.RelTables["KNOWS"].Remove(relDeleteTx, new EdgeKey("alice", "bob")));
            db.TransactionManager.Commit(conn.ClientContext, relDeleteTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (:Person {id:'alice'})-[r:KNOWS]->(:Person {id:'bob'}) RETURN r.since");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(0UL, result.GetNumTuples());
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteNodeUpdateThenDeleteWithoutPersistState_RemainsDeleted()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var insertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var initialProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(insertTx, "alice", initialProps);
            db.GraphLog.AppendNode("Person", "alice", initialProps);
            db.TransactionManager.Commit(conn.ClientContext, insertTx);

            var updateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].SetProperty(updateTx, "alice", "name", "Alicia"));
            db.TransactionManager.Commit(conn.ClientContext, updateTx);

            var deleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].Remove(deleteTx, "alice"));
            db.TransactionManager.Commit(conn.ClientContext, deleteTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            Assert.Null(reopenedConn.ReadNode("Person", "alice"));
            var result = reopenedConn.Query("MATCH (p:Person) RETURN p.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(0UL, result.GetNumTuples());
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteNodeDeleteThenReinsertWithoutPersistState_ReturnsLatestVisibleRow()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING,
                ["age"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            var insertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var initialProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice",
                ["age"] = 30L
            };
            db.NodeTables["Person"].Upsert(insertTx, "alice", initialProps);
            db.GraphLog.AppendNode("Person", "alice", initialProps);
            db.TransactionManager.Commit(conn.ClientContext, insertTx);

            var deleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].Remove(deleteTx, "alice"));
            db.TransactionManager.Commit(conn.ClientContext, deleteTx);

            var reinsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var reinsertProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alicia",
                ["age"] = 31L
            };
            db.NodeTables["Person"].Upsert(reinsertTx, "alice", reinsertProps);
            db.GraphLog.AppendNode("Person", "alice", reinsertProps);
            db.TransactionManager.Commit(conn.ClientContext, reinsertTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var node = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(node);
            Assert.Equal("Alicia", node!["name"]);
            Assert.Equal(31L, (long)node["age"]);

            var result = reopenedConn.Query(
                "MATCH (p:Person {id:'alice'}) RETURN p.name, p.age");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal("Alicia", row.GetString(0));
            Assert.Equal(31L, row.GetInt64(1));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteRelationshipUpdateThenDeleteWithoutPersistState_RemainsDeleted()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            var aliceTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var aliceProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(aliceTx, "alice", aliceProps);
            db.GraphLog.AppendNode("Person", "alice", aliceProps);
            db.TransactionManager.Commit(conn.ClientContext, aliceTx);

            var bobTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var bobProps = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob"
            };
            db.NodeTables["Person"].Upsert(bobTx, "bob", bobProps);
            db.GraphLog.AppendNode("Person", "bob", bobProps);
            db.TransactionManager.Commit(conn.ClientContext, bobTx);

            var relInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relInitial = new Dictionary<string, object>
            {
                ["since"] = 2020L
            };
            db.RelTables["KNOWS"].Upsert(relInsertTx, new EdgeKey("alice", "bob"), relInitial);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relInitial);
            db.TransactionManager.Commit(conn.ClientContext, relInsertTx);

            var relUpdateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.RelTables["KNOWS"].SetProperty(relUpdateTx, new EdgeKey("alice", "bob"), "since", 2024L));
            db.TransactionManager.Commit(conn.ClientContext, relUpdateTx);

            var relDeleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.RelTables["KNOWS"].Remove(relDeleteTx, new EdgeKey("alice", "bob")));
            db.TransactionManager.Commit(conn.ClientContext, relDeleteTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (:Person {id:'alice'})-[r:KNOWS]->(:Person {id:'bob'}) RETURN r.since");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(0UL, result.GetNumTuples());
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DirectWriteRelationshipDeleteThenReinsertWithoutPersistState_ReturnsLatestVisibleEdge()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64,
                ["weight"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            var aliceTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var aliceProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(aliceTx, "alice", aliceProps);
            db.GraphLog.AppendNode("Person", "alice", aliceProps);
            db.TransactionManager.Commit(conn.ClientContext, aliceTx);

            var bobTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var bobProps = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob"
            };
            db.NodeTables["Person"].Upsert(bobTx, "bob", bobProps);
            db.GraphLog.AppendNode("Person", "bob", bobProps);
            db.TransactionManager.Commit(conn.ClientContext, bobTx);

            var relInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relInitial = new Dictionary<string, object>
            {
                ["since"] = 2020L,
                ["weight"] = 1L
            };
            db.RelTables["KNOWS"].Upsert(relInsertTx, new EdgeKey("alice", "bob"), relInitial);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relInitial);
            db.TransactionManager.Commit(conn.ClientContext, relInsertTx);

            var relDeleteTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.RelTables["KNOWS"].Remove(relDeleteTx, new EdgeKey("alice", "bob")));
            db.TransactionManager.Commit(conn.ClientContext, relDeleteTx);

            var relReinsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relReinserted = new Dictionary<string, object>
            {
                ["since"] = 2024L,
                ["weight"] = 2L
            };
            db.RelTables["KNOWS"].Upsert(relReinsertTx, new EdgeKey("alice", "bob"), relReinserted);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relReinserted);
            db.TransactionManager.Commit(conn.ClientContext, relReinsertTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (:Person {id:'alice'})-[r:KNOWS]->(:Person {id:'bob'}) RETURN r.since, r.weight");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal(2024L, row.GetInt64(0));
            Assert.Equal(2L, row.GetInt64(1));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_IgnoresUncommittedDirectWriteNodeTail_AndKeepsCommittedPrefix()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING,
                ["notes"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var committedTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var committedProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice",
                ["notes"] = "committed"
            };
            db.NodeTables["Person"].Upsert(committedTx, "alice", committedProps);
            db.GraphLog.AppendNode("Person", "alice", committedProps);
            db.TransactionManager.Commit(conn.ClientContext, committedTx);

            var uncommittedTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var uncommittedProps = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob",
                ["notes"] = new string('x', 9000)
            };
            db.NodeTables["Person"].Upsert(uncommittedTx, "bob", uncommittedProps);
            db.GraphLog.AppendNode("Person", "bob", uncommittedProps);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var alice = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(alice);
            Assert.Equal("Alice", alice!["name"]);

            Assert.Null(reopenedConn.ReadNode("Person", "bob"));
            var result = reopenedConn.Query("MATCH (p:Person) RETURN p.id ORDER BY p.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            Assert.Equal("alice", result.GetNext().GetString(0));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_IgnoresUncommittedDirectWriteRelationshipTail_AndKeepsCommittedPrefix()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64,
                ["notes"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            foreach (var (id, name) in new[] { ("alice", "Alice"), ("bob", "Bob"), ("cara", "Cara") })
            {
                var nodeTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
                var props = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name
                };
                db.NodeTables["Person"].Upsert(nodeTx, id, props);
                db.GraphLog.AppendNode("Person", id, props);
                db.TransactionManager.Commit(conn.ClientContext, nodeTx);
            }

            var committedRelTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var committedRel = new Dictionary<string, object>
            {
                ["since"] = 2020L,
                ["notes"] = "committed"
            };
            db.RelTables["KNOWS"].Upsert(committedRelTx, new EdgeKey("alice", "bob"), committedRel);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", committedRel);
            db.TransactionManager.Commit(conn.ClientContext, committedRelTx);

            var uncommittedRelTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var uncommittedRel = new Dictionary<string, object>
            {
                ["since"] = 2025L,
                ["notes"] = new string('y', 9000)
            };
            db.RelTables["KNOWS"].Upsert(uncommittedRelTx, new EdgeKey("bob", "cara"), uncommittedRel);
            db.GraphLog.AppendRel("KNOWS", "bob", "cara", uncommittedRel);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since ORDER BY a.id, b.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal("alice", row.GetString(0));
            Assert.Equal("bob", row.GetString(1));
            Assert.Equal(2020L, row.GetInt64(2));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_IgnoresUncommittedDirectWriteNodeTail_AfterCheckpoint()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING,
                ["notes"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var committedTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var committedProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice",
                ["notes"] = "checkpointed"
            };
            db.NodeTables["Person"].Upsert(committedTx, "alice", committedProps);
            db.GraphLog.AppendNode("Person", "alice", committedProps);
            db.TransactionManager.Commit(conn.ClientContext, committedTx);

            var checkpoint = conn.Query("CHECKPOINT");
            Assert.True(checkpoint.IsSuccess, checkpoint.ErrorMessage);

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            var walPath = Path.Combine(dbPath, "data.wal");
            Assert.Equal(0, new FileInfo(graphLogPath).Length);
            // Logical WAL header (17 bytes) + BEGIN_TRANSACTION (1 byte) may persist
            Assert.True(new FileInfo(walPath).Length <= 18);

            var uncommittedTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var uncommittedProps = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob",
                ["notes"] = new string('x', 9000)
            };
            db.NodeTables["Person"].Upsert(uncommittedTx, "bob", uncommittedProps);
            db.GraphLog.AppendNode("Person", "bob", uncommittedProps);

            Assert.True(new FileInfo(graphLogPath).Length > 0);
            Assert.True(new FileInfo(walPath).Length > 0);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var alice = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(alice);
            Assert.Equal("Alice", alice!["name"]);
            Assert.Equal("checkpointed", alice["notes"]);

            Assert.Null(reopenedConn.ReadNode("Person", "bob"));
            var result = reopenedConn.Query("MATCH (p:Person) RETURN p.id ORDER BY p.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            Assert.Equal("alice", result.GetNext().GetString(0));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_IgnoresUncommittedDirectWriteRelationshipTail_AfterCheckpoint()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64,
                ["notes"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            foreach (var (id, name) in new[] { ("alice", "Alice"), ("bob", "Bob"), ("cara", "Cara") })
            {
                var nodeTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
                var props = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name
                };
                db.NodeTables["Person"].Upsert(nodeTx, id, props);
                db.GraphLog.AppendNode("Person", id, props);
                db.TransactionManager.Commit(conn.ClientContext, nodeTx);
            }

            var committedRelTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var committedRel = new Dictionary<string, object>
            {
                ["since"] = 2020L,
                ["notes"] = "checkpointed"
            };
            db.RelTables["KNOWS"].Upsert(committedRelTx, new EdgeKey("alice", "bob"), committedRel);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", committedRel);
            db.TransactionManager.Commit(conn.ClientContext, committedRelTx);

            var checkpoint = conn.Query("CHECKPOINT");
            Assert.True(checkpoint.IsSuccess, checkpoint.ErrorMessage);

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            var walPath = Path.Combine(dbPath, "data.wal");
            Assert.Equal(0, new FileInfo(graphLogPath).Length);
            // Logical WAL header (17 bytes) + BEGIN_TRANSACTION (1 byte) may persist
            Assert.True(new FileInfo(walPath).Length <= 18);

            var uncommittedRelTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var uncommittedRel = new Dictionary<string, object>
            {
                ["since"] = 2025L,
                ["notes"] = new string('y', 9000)
            };
            db.RelTables["KNOWS"].Upsert(uncommittedRelTx, new EdgeKey("bob", "cara"), uncommittedRel);
            db.GraphLog.AppendRel("KNOWS", "bob", "cara", uncommittedRel);

            Assert.True(new FileInfo(graphLogPath).Length > 0);
            Assert.True(new FileInfo(walPath).Length > 0);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since, r.notes ORDER BY a.id, b.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            var row = result.GetNext();
            Assert.Equal("alice", row.GetString(0));
            Assert.Equal("bob", row.GetString(1));
            Assert.Equal(2020L, row.GetInt64(2));
            Assert.Equal("checkpointed", row.GetString(3));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_DoesNotLetCheckpointOnlyWalReviveStaleGraphLog()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var directTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var initialProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice-v1"
            };
            db.NodeTables["Person"].Upsert(directTx, "alice", initialProps);
            db.GraphLog.AppendNode("Person", "alice", initialProps);
            db.TransactionManager.Commit(conn.ClientContext, directTx);

            var updateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].SetProperty(updateTx, "alice", "name", "Alice-v2"));
            db.TransactionManager.Commit(conn.ClientContext, updateTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            ColumnarTableSerializer.WriteSnapshot(graphDataPath, db.NodeTables, db.RelTables);

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            var graphLogTimestamp = File.GetLastWriteTimeUtc(graphLogPath);
            File.SetLastWriteTimeUtc(graphDataPath, graphLogTimestamp.AddSeconds(1));

            db.StorageManager.GetWAL().LogAndFlushCheckpoint();

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);
            var alice = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(alice);
            Assert.Equal("Alice-v2", alice!["name"]);
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_AppliesNewerGraphLog_WhenSnapshotIsOlder()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var firstTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var firstProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice-v1"
            };
            db.NodeTables["Person"].Upsert(firstTx, "alice", firstProps);
            db.GraphLog.AppendNode("Person", "alice", firstProps);
            db.TransactionManager.Commit(conn.ClientContext, firstTx);

            var catalogPath = Path.Combine(dbPath, "catalog.bin");
            using (var stream = new FileStream(catalogPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                db.Catalog.Serialize(writer);
            }

            var graphDataPath = Path.Combine(dbPath, "graph-data.bin");
            ColumnarTableSerializer.WriteSnapshot(graphDataPath, db.NodeTables, db.RelTables);
            var snapshotTimestamp = File.GetLastWriteTimeUtc(graphDataPath);

            var secondTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var secondProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice-v2"
            };
            db.NodeTables["Person"].Upsert(secondTx, "alice", secondProps);
            db.GraphLog.AppendNode("Person", "alice", secondProps);
            db.TransactionManager.Commit(conn.ClientContext, secondTx);

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            Assert.True(File.Exists(graphLogPath));
            Assert.True(new FileInfo(graphLogPath).Length > 0);

            var walPath = Path.Combine(dbPath, "data.wal");
            Assert.True(File.Exists(walPath));
            Assert.True(new FileInfo(walPath).Length > 0);
            var walTimestamp = File.GetLastWriteTimeUtc(walPath);
            File.SetLastWriteTimeUtc(graphDataPath, walTimestamp.AddSeconds(-1));
            Assert.True(File.GetLastWriteTimeUtc(walPath) > File.GetLastWriteTimeUtc(graphDataPath));

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);
            var alice = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(alice);
            Assert.Equal("Alice-v2", alice!["name"]);
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Checkpoint_PersistsMixedCommittedHistory_AndClearsGraphLog()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var aliceInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var aliceV1 = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice-v1"
            };
            db.NodeTables["Person"].Upsert(aliceInsertTx, "alice", aliceV1);
            db.GraphLog.AppendNode("Person", "alice", aliceV1);
            db.TransactionManager.Commit(conn.ClientContext, aliceInsertTx);

            var aliceUpdateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].SetProperty(aliceUpdateTx, "alice", "name", "Alice-v2"));
            db.TransactionManager.Commit(conn.ClientContext, aliceUpdateTx);

            var bobInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var bobV1 = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob-v1"
            };
            db.NodeTables["Person"].Upsert(bobInsertTx, "bob", bobV1);
            db.GraphLog.AppendNode("Person", "bob", bobV1);
            db.TransactionManager.Commit(conn.ClientContext, bobInsertTx);

            var checkpoint = conn.Query("CHECKPOINT");
            Assert.True(checkpoint.IsSuccess, checkpoint.ErrorMessage);

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            var walPath = Path.Combine(dbPath, "data.wal");
            Assert.Equal(0, new FileInfo(graphLogPath).Length);
            // Logical WAL header (17 bytes) + BEGIN_TRANSACTION (1 byte) may persist
            Assert.True(new FileInfo(walPath).Length <= 18);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);
            var alice = reopenedConn.ReadNode("Person", "alice");
            Assert.NotNull(alice);
            Assert.Equal("Alice-v2", alice!["name"]);

            var bob = reopenedConn.ReadNode("Person", "bob");
            Assert.NotNull(bob);
            Assert.Equal("Bob-v1", bob!["name"]);
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Checkpoint_PersistsDirectWriteData_ForReopenedQueryPaths()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            var aliceInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var aliceV1 = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice-v1"
            };
            db.NodeTables["Person"].Upsert(aliceInsertTx, "alice", aliceV1);
            db.GraphLog.AppendNode("Person", "alice", aliceV1);
            db.TransactionManager.Commit(conn.ClientContext, aliceInsertTx);

            var aliceUpdateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].SetProperty(aliceUpdateTx, "alice", "name", "Alice-v2"));
            db.TransactionManager.Commit(conn.ClientContext, aliceUpdateTx);

            var bobInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var bobV1 = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob-v1"
            };
            db.NodeTables["Person"].Upsert(bobInsertTx, "bob", bobV1);
            db.GraphLog.AppendNode("Person", "bob", bobV1);
            db.TransactionManager.Commit(conn.ClientContext, bobInsertTx);

            var relInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relV1 = new Dictionary<string, object>
            {
                ["since"] = 2024L
            };
            db.RelTables["KNOWS"].Upsert(relInsertTx, new EdgeKey("alice", "bob"), relV1);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relV1);
            db.TransactionManager.Commit(conn.ClientContext, relInsertTx);

            var checkpoint = conn.Query("CHECKPOINT");
            Assert.True(checkpoint.IsSuccess, checkpoint.ErrorMessage);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var nodeResult = reopenedConn.Query(
                "MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id");
            Assert.True(nodeResult.IsSuccess, nodeResult.ErrorMessage);
            Assert.Equal(2UL, nodeResult.GetNumTuples());

            var firstNode = nodeResult.GetNext();
            Assert.Equal("alice", firstNode.GetString(0));
            Assert.Equal("Alice-v2", firstNode.GetString(1));

            var secondNode = nodeResult.GetNext();
            Assert.Equal("bob", secondNode.GetString(0));
            Assert.Equal("Bob-v1", secondNode.GetString(1));

            var relResult = reopenedConn.Query(
                "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since");
            Assert.True(relResult.IsSuccess, relResult.ErrorMessage);
            Assert.Equal(1UL, relResult.GetNumTuples());

            var relRow = relResult.GetNext();
            Assert.Equal("alice", relRow.GetString(0));
            Assert.Equal("bob", relRow.GetString(1));
            Assert.Equal(2024L, relRow.GetInt64(2));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Checkpoint_RebuildsIndexAgainstLatestDirectWriteValue_AfterGraphLogClears()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.CommitSchemaOnly();

            var insertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var initialProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(insertTx, "alice", initialProps);
            db.GraphLog.AppendNode("Person", "alice", initialProps);
            db.TransactionManager.Commit(conn.ClientContext, insertTx);

            conn.CreateIndex("Person", "name");

            var updateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.NodeTables["Person"].SetProperty(updateTx, "alice", "name", "Alicia"));
            db.TransactionManager.Commit(conn.ClientContext, updateTx);

            var checkpoint = conn.Query("CHECKPOINT");
            Assert.True(checkpoint.IsSuccess, checkpoint.ErrorMessage);

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            var walPath = Path.Combine(dbPath, "data.wal");
            Assert.Equal(0, new FileInfo(graphLogPath).Length);
            // Logical WAL header (17 bytes) + BEGIN_TRANSACTION (1 byte) may persist
            Assert.True(new FileInfo(walPath).Length <= 18);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            Assert.True(reopenedDb.TryIndexLookup("Person", "name", "Alicia", out var offset));
            Assert.False(reopenedDb.TryIndexLookup("Person", "name", "Alice", out _));

            var tx = new BogDb.Core.Transaction.Transaction(
                BogDb.Core.Transaction.TransactionType.READ_ONLY,
                id: 101,
                startTS: 100);
            var scan = new BogDb.Core.Processor.Operator.Scan.PhysicalIndexScanNode(
                reopenedDb,
                "Person",
                "name",
                "Alicia",
                "p",
                id: 101);
            var ctx = new BogDb.Core.Processor.ExecutionContext(tx, reopenedDb.BufferManager);
            Assert.True(scan.GetNextTuple(ctx));
            Assert.Equal("alice", ctx.CurrentNodeId);
            Assert.Equal("Alicia", ctx.CurrentNodeProperties!["name"]);

            var result = reopenedConn.Query("MATCH (p:Person {name:'Alicia'}) RETURN p.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());
            Assert.Equal("alice", result.GetNext().GetString(0));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Checkpoint_PersistsLatestDirectWriteRelationshipUpdate_ForReopenedTraversal()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                ["id"] = LogicalTypeID.STRING,
                ["name"] = LogicalTypeID.STRING
            });
            conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
            {
                ["since"] = LogicalTypeID.INT64
            });
            conn.CommitSchemaOnly();

            var aliceTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var aliceProps = new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            };
            db.NodeTables["Person"].Upsert(aliceTx, "alice", aliceProps);
            db.GraphLog.AppendNode("Person", "alice", aliceProps);
            db.TransactionManager.Commit(conn.ClientContext, aliceTx);

            var bobTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var bobProps = new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob"
            };
            db.NodeTables["Person"].Upsert(bobTx, "bob", bobProps);
            db.GraphLog.AppendNode("Person", "bob", bobProps);
            db.TransactionManager.Commit(conn.ClientContext, bobTx);

            var relInsertTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            var relInitial = new Dictionary<string, object>
            {
                ["since"] = 2020L
            };
            db.RelTables["KNOWS"].Upsert(relInsertTx, new EdgeKey("alice", "bob"), relInitial);
            db.GraphLog.AppendRel("KNOWS", "alice", "bob", relInitial);
            db.TransactionManager.Commit(conn.ClientContext, relInsertTx);

            var relUpdateTx = db.TransactionManager.BeginTransaction(conn.ClientContext, TransactionType.WRITE);
            Assert.True(db.RelTables["KNOWS"].SetProperty(relUpdateTx, new EdgeKey("alice", "bob"), "since", 2024L));
            db.TransactionManager.Commit(conn.ClientContext, relUpdateTx);

            var checkpoint = conn.Query("CHECKPOINT");
            Assert.True(checkpoint.IsSuccess, checkpoint.ErrorMessage);

            var graphLogPath = Path.Combine(dbPath, "graph-log.bin");
            var walPath = Path.Combine(dbPath, "data.wal");
            Assert.Equal(0, new FileInfo(graphLogPath).Length);
            // Logical WAL header (17 bytes) + BEGIN_TRANSACTION (1 byte) may persist
            Assert.True(new FileInfo(walPath).Length <= 18);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(1UL, result.GetNumTuples());

            var row = result.GetNext();
            Assert.Equal("alice", row.GetString(0));
            Assert.Equal("bob", row.GetString(1));
            Assert.Equal(2024L, row.GetInt64(2));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesRelGroupAddedConnection_ForMixedEndpointTraversal()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE NODE TABLE Company(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)").IsSuccess);
            Assert.True(conn.Query("ALTER TABLE KNOWS ADD FROM Person TO Company").IsSuccess);

            conn.BeginWriteTransaction();
            conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice" });
            conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob" });
            conn.UpsertNode("Company", "acme", new Dictionary<string, object> { ["id"] = "acme" });
            conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object> { ["since"] = 2020L });
            conn.UpsertRelationship("KNOWS", "bob", "acme", new Dictionary<string, object> { ["since"] = 2024L });
            conn.Commit();

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var relEntry = Assert.IsType<BogDb.Core.Catalog.RelGroupCatalogEntry>(
                reopenedDb.Catalog.GetTableCatalogEntry(null, "KNOWS", useInternal: false));
            Assert.Equal(2, relEntry.GetConnections().Count);
            Assert.True(relEntry.ContainsConnection("Person", "Person"));
            Assert.True(relEntry.ContainsConnection("Person", "Company"));

            var direct = reopenedConn.Query(
                "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since");
            Assert.True(direct.IsSuccess, direct.ErrorMessage);
            Assert.Equal(1UL, direct.GetNumTuples());
            var directRow = direct.GetNext();
            Assert.Equal("alice", directRow.GetString(0));
            Assert.Equal("bob", directRow.GetString(1));
            Assert.Equal(2020L, directRow.GetInt64(2));

            var recursive = reopenedConn.Query(
                "MATCH (p:Person)-[r:KNOWS*1..2]->(c:Company) WHERE p.id = 'alice' RETURN c.id");
            Assert.True(recursive.IsSuccess, recursive.ErrorMessage);
            Assert.Equal(1UL, recursive.GetNumTuples());
            Assert.Equal("acme", recursive.GetNext().GetString(0));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesRelGroupDroppedConnection_RemovesMatchingEdges_AndReleasesNodeReference()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE NODE TABLE Company(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)").IsSuccess);
            Assert.True(conn.Query("ALTER TABLE KNOWS ADD FROM Person TO Company").IsSuccess);

            conn.BeginWriteTransaction();
            conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice" });
            conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob" });
            conn.UpsertNode("Company", "acme", new Dictionary<string, object> { ["id"] = "acme" });
            conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object> { ["since"] = 2020L });
            conn.UpsertRelationship("KNOWS", "alice", "acme", new Dictionary<string, object> { ["since"] = 2024L });
            conn.Commit();

            var drop = conn.Query("ALTER TABLE KNOWS DROP FROM Person TO Company");
            Assert.True(drop.IsSuccess, drop.ErrorMessage);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var relEntry = Assert.IsType<BogDb.Core.Catalog.RelGroupCatalogEntry>(
                reopenedDb.Catalog.GetTableCatalogEntry(null, "KNOWS", useInternal: false));
            Assert.Single(relEntry.GetConnections());
            Assert.True(relEntry.ContainsConnection("Person", "Person"));
            Assert.False(relEntry.ContainsConnection("Person", "Company"));

            var personCompany = reopenedConn.Query(
                "MATCH (p:Person)-[r:KNOWS]->(c:Company) RETURN COUNT(*)");
            Assert.True(personCompany.IsSuccess, personCompany.ErrorMessage);
            Assert.Equal(0L, personCompany.GetNext().GetInt64(0));

            var personPerson = reopenedConn.Query(
                "MATCH (p:Person)-[r:KNOWS]->(q:Person) RETURN COUNT(*)");
            Assert.True(personPerson.IsSuccess, personPerson.ErrorMessage);
            Assert.Equal(1L, personPerson.GetNext().GetInt64(0));

            var dropCompany = reopenedConn.Query("DROP TABLE Company");
            Assert.True(dropCompany.IsSuccess, dropCompany.ErrorMessage);
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesRelGroupMultipleAddedConnections_ForMixedEndpointTraversal()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE NODE TABLE Company(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE NODE TABLE City(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE REL TABLE LINK(FROM Person TO Person, since INT64)").IsSuccess);
            Assert.True(conn.Query("ALTER TABLE LINK ADD FROM Person TO Company").IsSuccess);
            Assert.True(conn.Query("ALTER TABLE LINK ADD FROM Company TO City").IsSuccess);

            conn.BeginWriteTransaction();
            conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice" });
            conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob" });
            conn.UpsertNode("Company", "acme", new Dictionary<string, object> { ["id"] = "acme" });
            conn.UpsertNode("City", "seattle", new Dictionary<string, object> { ["id"] = "seattle" });
            conn.UpsertRelationship("LINK", "alice", "bob", new Dictionary<string, object> { ["since"] = 2020L });
            conn.UpsertRelationship("LINK", "bob", "acme", new Dictionary<string, object> { ["since"] = 2022L });
            conn.UpsertRelationship("LINK", "acme", "seattle", new Dictionary<string, object> { ["since"] = 2024L });
            conn.Commit();

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var relEntry = Assert.IsType<BogDb.Core.Catalog.RelGroupCatalogEntry>(
                reopenedDb.Catalog.GetTableCatalogEntry(null, "LINK", useInternal: false));
            Assert.Equal(3, relEntry.GetConnections().Count);
            Assert.True(relEntry.ContainsConnection("Person", "Person"));
            Assert.True(relEntry.ContainsConnection("Person", "Company"));
            Assert.True(relEntry.ContainsConnection("Company", "City"));

            var personCompany = reopenedConn.Query(
                "MATCH (p:Person)-[r:LINK]->(c:Company) RETURN p.id, c.id, r.since");
            Assert.True(personCompany.IsSuccess, personCompany.ErrorMessage);
            Assert.Equal(1UL, personCompany.GetNumTuples());
            var personCompanyRow = personCompany.GetNext();
            Assert.Equal("bob", personCompanyRow.GetString(0));
            Assert.Equal("acme", personCompanyRow.GetString(1));
            Assert.Equal(2022L, personCompanyRow.GetInt64(2));

            var recursive = reopenedConn.Query(
                "MATCH (p:Person)-[r:LINK*1..3]->(c:City) WHERE p.id = 'alice' RETURN c.id");
            Assert.True(recursive.IsSuccess, recursive.ErrorMessage);
            Assert.Equal(1UL, recursive.GetNumTuples());
            Assert.Equal("seattle", recursive.GetNext().GetString(0));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesRemainingRelGroupConnections_AfterDroppingOneMixedEndpointConnection()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;
        try
        {
            db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE NODE TABLE Company(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE NODE TABLE City(id STRING)").IsSuccess);
            Assert.True(conn.Query("CREATE REL TABLE LINK(FROM Person TO Person, since INT64)").IsSuccess);
            Assert.True(conn.Query("ALTER TABLE LINK ADD FROM Person TO Company").IsSuccess);
            Assert.True(conn.Query("ALTER TABLE LINK ADD FROM Company TO City").IsSuccess);

            conn.BeginWriteTransaction();
            conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice" });
            conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob" });
            conn.UpsertNode("Company", "acme", new Dictionary<string, object> { ["id"] = "acme" });
            conn.UpsertNode("City", "seattle", new Dictionary<string, object> { ["id"] = "seattle" });
            conn.UpsertRelationship("LINK", "alice", "bob", new Dictionary<string, object> { ["since"] = 2020L });
            conn.UpsertRelationship("LINK", "bob", "acme", new Dictionary<string, object> { ["since"] = 2022L });
            conn.UpsertRelationship("LINK", "acme", "seattle", new Dictionary<string, object> { ["since"] = 2024L });
            conn.Commit();

            var drop = conn.Query("ALTER TABLE LINK DROP FROM Person TO Company");
            Assert.True(drop.IsSuccess, drop.ErrorMessage);

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var relEntry = Assert.IsType<BogDb.Core.Catalog.RelGroupCatalogEntry>(
                reopenedDb.Catalog.GetTableCatalogEntry(null, "LINK", useInternal: false));
            Assert.Equal(2, relEntry.GetConnections().Count);
            Assert.True(relEntry.ContainsConnection("Person", "Person"));
            Assert.False(relEntry.ContainsConnection("Person", "Company"));
            Assert.True(relEntry.ContainsConnection("Company", "City"));

            var personCompany = reopenedConn.Query(
                "MATCH (p:Person)-[r:LINK]->(c:Company) RETURN COUNT(*)");
            Assert.True(personCompany.IsSuccess, personCompany.ErrorMessage);
            Assert.Equal(0L, personCompany.GetNext().GetInt64(0));

            var companyCity = reopenedConn.Query(
                "MATCH (c:Company)-[r:LINK]->(d:City) RETURN COUNT(*)");
            Assert.True(companyCity.IsSuccess, companyCity.ErrorMessage);
            Assert.Equal(1L, companyCity.GetNext().GetInt64(0));

            var recursive = reopenedConn.Query(
                "MATCH (p:Person)-[r:LINK*1..3]->(c:City) WHERE p.id = 'alice' RETURN COUNT(*)");
            Assert.True(recursive.IsSuccess, recursive.ErrorMessage);
            Assert.Equal(0L, recursive.GetNext().GetInt64(0));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    [Fact]
    public void Reopen_PreservesMergeCreatedParallelEdges_ForSameEndpoints()
    {
        var dbPath = CreateTempDirectory();
        BogDatabase? db = null;

        try
        {
            db = BogDatabase.Open(dbPath);
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.INT64
                });
                conn.EnsureRelTable("LIKES", "Person", "Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.INT64
                });
                conn.UpsertNode("Person", 0L, new Dictionary<string, object>());
                conn.UpsertNode("Person", 3L, new Dictionary<string, object>());
                conn.Commit();

                Assert.True(conn.Query(
                    "MATCH (p:Person {id:0}), (q:Person {id:3}) MERGE (p)-[:LIKES {id:1}]->(q)").IsSuccess);
                Assert.True(conn.Query(
                    "MATCH (p:Person {id:0}), (q:Person {id:3}) MERGE (p)-[:LIKES {id:2}]->(q)").IsSuccess);
            }

            SimulateCrashClose(db);
            db = null;

            using var reopenedDb = BogDatabase.Open(dbPath);
            using var reopenedConn = new BogConnection(reopenedDb);

            var result = reopenedConn.Query(
                "MATCH (:Person {id:0})-[e:LIKES]->(:Person {id:3}) RETURN e.id ORDER BY e.id");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(2UL, result.GetNumTuples());
            Assert.True(result.HasNext());
            Assert.Equal(1L, result.GetNext().GetInt64(0));
            Assert.True(result.HasNext());
            Assert.Equal(2L, result.GetNext().GetInt64(0));
        }
        finally
        {
            db?.Dispose();
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "bogdb-ng-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SimulateCrashClose(BogDatabase db)
    {
        var databaseType = typeof(BogDatabase);

        var graphLogProp = databaseType.GetProperty("GraphLog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        (graphLogProp?.GetValue(db) as System.IDisposable)?.Dispose();

        var storageManagerProp = databaseType.GetProperty("StorageManager", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        (storageManagerProp?.GetValue(db) as System.IDisposable)?.Dispose();

        db.BufferManager.Dispose();
    }
    [Fact]
    public void ColumnFiles_CreatedOnDisk_WithManifest()
    {
        var dbPath = CreateTempDirectory();
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING,
                    ["age"] = LogicalTypeID.INT64
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    ["id"] = "alice",
                    ["name"] = "Alice",
                    ["age"] = 30L
                });
                conn.UpsertNode("Person", "bob", new Dictionary<string, object>
                {
                    ["id"] = "bob",
                    ["name"] = "Bob",
                    ["age"] = 25L
                });
                conn.Commit();
            }

            // Verify column files exist
            var columnsDir = Path.Combine(dbPath, "columns");
            Assert.True(Directory.Exists(columnsDir), "columns/ directory should exist");

            var kzFiles = Directory.GetFiles(columnsDir, "*.kz");
            Assert.True(kzFiles.Length > 0, "Should have at least one .kz column file");

            var manifestPath = Path.Combine(columnsDir, "manifest.json");
            Assert.True(File.Exists(manifestPath), "manifest.json should exist");
            var manifestJson = File.ReadAllText(manifestPath);
            Assert.Contains("Person", manifestJson);

            // Verify data survives reopen
            using (var reopenedDb = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(reopenedDb))
            {
                var result = conn.Query("MATCH (p:Person) RETURN p.name, p.age ORDER BY p.name");
                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(2UL, result.GetNumTuples());

                var row1 = result.GetNext();
                Assert.Equal("Alice", row1.GetString(0));
                Assert.Equal(30L, row1.GetInt64(1));

                var row2 = result.GetNext();
                Assert.Equal("Bob", row2.GetString(0));
                Assert.Equal(25L, row2.GetInt64(1));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }
}
