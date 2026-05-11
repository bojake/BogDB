using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BogDb.Core.Main;
using BogDb.Core.Storage.Index;
using Xunit;

namespace BogDb.Tests.Storage;

public class DiskBackedNodeIndexTests
{
    [Fact]
    public void DiskBackedIndex_PutAndLookup_Works()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kzix_test_{Guid.NewGuid():N}.idx");
        try
        {
            var idx = new DiskBackedNodeIndex(path);
            idx.Put("Alice", 10L);
            idx.Put("Bob", 20L);

            Assert.True(idx.TryLookup("Alice", out var offset));
            Assert.Equal(10L, offset);
            Assert.True(idx.TryLookup("Bob", out offset));
            Assert.Equal(20L, offset);
            Assert.False(idx.TryLookup("Charlie", out _));
            Assert.Equal(2, idx.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DiskBackedIndex_CheckpointAndReload_SurvivesReopen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kzix_test_{Guid.NewGuid():N}.idx");
        try
        {
            // Phase 1: create and checkpoint
            {
                var idx = new DiskBackedNodeIndex(path);
                idx.Put("Alice", 10L);
                idx.Put("Bob", 20L);
                idx.Put("Alice", 30L); // second offset for Alice
                idx.Checkpoint();
            }

            Assert.True(File.Exists(path));

            // Phase 2: reopen from disk
            {
                var idx = new DiskBackedNodeIndex(path);
                Assert.Equal(2, idx.Count);

                Assert.True(idx.TryLookupAll("Alice", out var aliceOffsets));
                Assert.Equal(2, aliceOffsets.Count);
                Assert.Contains(10L, aliceOffsets);
                Assert.Contains(30L, aliceOffsets);

                Assert.True(idx.TryLookup("Bob", out var bobOffset));
                Assert.Equal(20L, bobOffset);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DiskBackedIndex_SupportsIntAndDoubleKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kzix_test_{Guid.NewGuid():N}.idx");
        try
        {
            {
                var idx = new DiskBackedNodeIndex(path);
                idx.Put(42L, 100L);
                idx.Put(3.14, 200L);
                idx.Put(true, 300L);
                idx.Checkpoint();
            }

            {
                var idx = new DiskBackedNodeIndex(path);
                Assert.True(idx.TryLookup(42L, out var v1));
                Assert.Equal(100L, v1);
                Assert.True(idx.TryLookup(3.14, out var v2));
                Assert.Equal(200L, v2);
                Assert.True(idx.TryLookup(true, out var v3));
                Assert.Equal(300L, v3);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DiskBackedIndex_Remove_PersistsOnCheckpoint()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kzix_test_{Guid.NewGuid():N}.idx");
        try
        {
            {
                var idx = new DiskBackedNodeIndex(path);
                idx.Put("A", 1L);
                idx.Put("B", 2L);
                idx.Remove("A", 1L);
                idx.Checkpoint();
            }

            {
                var idx = new DiskBackedNodeIndex(path);
                Assert.Equal(1, idx.Count);
                Assert.False(idx.TryLookup("A", out _));
                Assert.True(idx.TryLookup("B", out var v));
                Assert.Equal(2L, v);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void NodePropertyIndex_CreateDiskBacked_WorksEndToEnd()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kzix_test_{Guid.NewGuid():N}.idx");
        try
        {
            var npi = new NodePropertyIndex();
            npi.CreateDiskBackedIndex("name", path);

            Assert.True(npi.IsDiskBacked("name"));
            Assert.False(npi.IsDiskBacked("age"));

            npi.Put("name", "Alice", 10L);
            npi.Put("name", "Bob", 20L);

            Assert.True(npi.TryLookup("name", "Alice", out var offset));
            Assert.Equal(10L, offset);

            npi.CheckpointDiskIndexes();
            Assert.True(File.Exists(path));

            // Reload
            var npi2 = new NodePropertyIndex();
            npi2.LoadDiskBackedIndex("name", path);

            Assert.True(npi2.IsDiskBacked("name"));
            Assert.True(npi2.TryLookup("name", "Alice", out offset));
            Assert.Equal(10L, offset);
            Assert.True(npi2.TryLookup("name", "Bob", out offset));
            Assert.Equal(20L, offset);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ─── E2E: BogDatabase disk-backed index lifecycle ──────────────────────────

    [Fact]
    public void BogDatabase_CreateIndex_ProducesKzixFile()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kzix_e2e_{Guid.NewGuid():N}");
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogDb.Core.Main.BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
                {
                    { "id", BogDb.Core.Common.LogicalTypeID.STRING },
                    { "name", BogDb.Core.Common.LogicalTypeID.STRING }
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    { "id", "alice" }, { "name", "Alice" }
                });
                conn.Commit();

                conn.CreateIndex("Person", "name");

                // REAL: Verify .kzix file was created — this only happens with disk-backed path
                var expectedPath = Path.Combine(dbPath, "indexes", "Person.name.kzix");
                Assert.True(File.Exists(expectedPath), $"Expected index file at {expectedPath}");

                // REAL: Verify the .kzix file is non-empty (contains actual serialized data)
                Assert.True(new FileInfo(expectedPath).Length > 0, ".kzix file is empty");
            }
        }
        finally
        {
            if (Directory.Exists(dbPath)) Directory.Delete(dbPath, recursive: true);
        }
    }

    [Fact]
    public void BogDatabase_DiskBackedIndex_SurvivesReopen_WithoutMonolithicSnapshot()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kzix_e2e_{Guid.NewGuid():N}");
        try
        {
            // Phase 1: create, index, close
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogDb.Core.Main.BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
                {
                    { "id", BogDb.Core.Common.LogicalTypeID.STRING },
                    { "name", BogDb.Core.Common.LogicalTypeID.STRING }
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    { "id", "alice" }, { "name", "Alice" }
                });
                conn.UpsertNode("Person", "bob", new Dictionary<string, object>
                {
                    { "id", "bob" }, { "name", "Bob" }
                });
                conn.Commit();

                conn.CreateIndex("Person", "name");
            }

            // Phase 2: reopen — verify the loaded index IS disk-backed (not just snapshot-loaded)
            using (var db = BogDatabase.Open(dbPath))
            {
                // REAL: Verify the index is specifically a DiskBackedNodeIndex, not InMemoryNodeIndex
                Assert.True(db.NodeIndexes.TryGetValue("Person", out var idx));
                Assert.True(idx!.IsDiskBacked("name"),
                    "Index should be DiskBackedNodeIndex after reopen, not InMemoryNodeIndex");

                // Verify the data is correct
                Assert.True(db.TryIndexLookup("Person", "name", "Alice", out var offset));
                Assert.True(offset >= 0);
                Assert.True(db.TryIndexLookup("Person", "name", "Bob", out _));
                Assert.False(db.TryIndexLookup("Person", "name", "Nonexistent", out _));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath)) Directory.Delete(dbPath, recursive: true);
        }
    }

    [Fact]
    public void BogDatabase_DiskBackedIndex_CheckpointWritesUpdatedKzixFile()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kzix_e2e_{Guid.NewGuid():N}");
        try
        {
            // Phase 1: create, index
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogDb.Core.Main.BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
                {
                    { "id", BogDb.Core.Common.LogicalTypeID.STRING },
                    { "name", BogDb.Core.Common.LogicalTypeID.STRING }
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    { "id", "alice" }, { "name", "Alice" }
                });
                conn.Commit();

                conn.CreateIndex("Person", "name");
            }

            var kzixPath = Path.Combine(dbPath, "indexes", "Person.name.kzix");
            var firstSize = new FileInfo(kzixPath).Length;

            // Phase 2: reopen, add data, close (triggers PersistState → checkpoint)
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogDb.Core.Main.BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.UpsertNode("Person", "bob", new Dictionary<string, object>
                {
                    { "id", "bob" }, { "name", "Bob" }
                });
                conn.Commit();
            }

            // REAL: The .kzix file should be larger now (has Bob + Alice)
            var secondSize = new FileInfo(kzixPath).Length;
            Assert.True(secondSize > firstSize,
                $".kzix file didn't grow after adding data: first={firstSize}, second={secondSize}");

            // Phase 3: reopen and verify both entries survived via disk-backed index
            using (var db = BogDatabase.Open(dbPath))
            {
                Assert.True(db.NodeIndexes.TryGetValue("Person", out var idx));
                Assert.True(idx!.IsDiskBacked("name"));
                Assert.True(db.TryIndexLookup("Person", "name", "Alice", out _));
                Assert.True(db.TryIndexLookup("Person", "name", "Bob", out _));
            }
        }
        finally
        {
            if (Directory.Exists(dbPath)) Directory.Delete(dbPath, recursive: true);
        }
    }

    [Fact]
    public void BogDatabase_RebuildFromCatalog_UsesDiskBacked_WhenFileBased()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"kzix_e2e_{Guid.NewGuid():N}");
        try
        {
            // Phase 1: create, index
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogDb.Core.Main.BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
                {
                    { "id", BogDb.Core.Common.LogicalTypeID.STRING },
                    { "name", BogDb.Core.Common.LogicalTypeID.STRING }
                });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object>
                {
                    { "id", "alice" }, { "name", "Alice" }
                });
                conn.Commit();

                conn.CreateIndex("Person", "name");
            }

            // Delete BOTH the monolithic snapshot AND the .kzix file
            // This forces RebuildNodeIndexesFromCatalog (the fallback path)
            var indexSnapshotPath = Path.Combine(dbPath, "index-data.bin");
            var kzixPath = Path.Combine(dbPath, "indexes", "Person.name.kzix");
            if (File.Exists(indexSnapshotPath)) File.Delete(indexSnapshotPath);
            if (File.Exists(kzixPath)) File.Delete(kzixPath);

            // Reopen — should rebuild from catalog using DiskBackedNodeIndex
            using (var db = BogDatabase.Open(dbPath))
            {
                // REAL: Verify the rebuilt index IS disk-backed
                Assert.True(db.NodeIndexes.TryGetValue("Person", out var idx));
                Assert.True(idx!.IsDiskBacked("name"),
                    "Rebuilt index should be DiskBackedNodeIndex for file-backed databases");

                // Verify data was rebuilt correctly from node table
                Assert.True(db.TryIndexLookup("Person", "name", "Alice", out _));
            }
            // Dispose triggers PersistState → CheckpointDiskIndexes → writes .kzix

            // REAL: Verify the .kzix file was re-created after dispose
            Assert.True(File.Exists(kzixPath),
                ".kzix file should be recreated after catalog rebuild + dispose");
        }
        finally
        {
            if (Directory.Exists(dbPath)) Directory.Delete(dbPath, recursive: true);
        }
    }
}
