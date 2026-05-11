using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Storage.Index;

namespace BogDb.Tests.Storage;

/// <summary>
/// Unit and integration tests for the HashIndex subsystem:
///   INodeIndex / InMemoryNodeIndex, NodePropertyIndex,
///   BogDatabase.CreateIndex/TryIndexLookup/UpdateNodeIndexes,
///   and the UpsertNode/UpsertNodeById population pipelines.
/// </summary>
public class HashIndexTests
{
    // ── INodeIndex (InMemoryNodeIndex) ────────────────────────────────────────

    [Fact]
    public void InMemoryNodeIndex_Put_And_Lookup_Long()
    {
        var idx = new NodePropertyIndex();
        idx.CreateIndex("age");
        idx.Put("age", 42L, 0L);
        Assert.True(idx.TryLookup("age", 42L, out var offset));
        Assert.Equal(0L, offset);
    }

    [Fact]
    public void InMemoryNodeIndex_Put_And_Lookup_String()
    {
        var idx = new NodePropertyIndex();
        idx.CreateIndex("name");
        idx.Put("name", "alice", 7L);
        Assert.True(idx.TryLookup("name", "alice", out var offset));
        Assert.Equal(7L, offset);
    }

    [Fact]
    public void InMemoryNodeIndex_Put_And_Lookup_List_UsesStructuralEquality()
    {
        var idx = new NodePropertyIndex();
        idx.CreateIndex("embedding");
        idx.Put("embedding", new List<object?> { 1.0f, 2.0f, 3.0f }, 7L);

        Assert.True(idx.TryLookup("embedding", new List<object?> { 1.0d, 2.0d, 3.0d }, out var offset));
        Assert.Equal(7L, offset);
    }

    [Fact]
    public void InMemoryNodeIndex_Put_And_Lookup_NestedList_UsesStructuralEquality()
    {
        var idx = new NodePropertyIndex();
        idx.CreateIndex("embedding");
        idx.Put("embedding", new List<object?>
        {
            new List<object?> { 1.0f, 2.0f },
            new List<object?> { 3.0f, 4.0f }
        }, 9L);

        Assert.True(idx.TryLookup("embedding", new List<object?>
        {
            new List<object?> { 1.0d, 2.0d },
            new List<object?> { 3.0d, 4.0d }
        }, out var offset));
        Assert.Equal(9L, offset);
    }

    [Fact]
    public void InMemoryNodeIndex_Miss_ReturnsFalse()
    {
        var idx = new NodePropertyIndex();
        idx.CreateIndex("email");
        Assert.False(idx.TryLookup("email", "nobody@example.com", out _));
    }

    [Fact]
    public void InMemoryNodeIndex_Overwrite_KeepsLatest()
    {
        var idx = new NodePropertyIndex();
        idx.CreateIndex("rank");
        idx.Put("rank", 1L, 0L);
        idx.Put("rank", 1L, 99L);  // overwrite same key
        Assert.True(idx.TryLookup("rank", 1L, out var offset));
        Assert.Equal(99L, offset);
    }

    [Fact]
    public void InMemoryNodeIndex_UnindexedProperty_ReturnsFalse()
    {
        var idx = new NodePropertyIndex();
        // "score" not indexed
        Assert.False(idx.TryLookup("score", 100L, out _));
    }

    [Fact]
    public void NodePropertyIndex_Count_Tracks_Entries()
    {
        var idx = new NodePropertyIndex();
        idx.CreateIndex("id");
        Assert.Equal(0L, idx.Count("id"));
        idx.Put("id", "a", 0L);
        idx.Put("id", "b", 1L);
        Assert.Equal(2L, idx.Count("id"));
    }

    [Fact]
    public void NodePropertyIndex_HasAnyIndex_False_WhenEmpty()
    {
        var idx = new NodePropertyIndex();
        Assert.False(idx.HasAnyIndex);
        idx.CreateIndex("email");
        Assert.True(idx.HasAnyIndex);
    }

    // ── BogDatabase.CreateIndex + TryIndexLookup ─────────────────────────────

    [Fact]
    public void CreateIndex_Rebuilds_From_ExistingNodes()
    {
        using var db = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);

        conn.UpsertNodeById("Person", "alice_id",
            new Dictionary<string, object> { ["name"] = "Alice", ["age"] = 30L });
        conn.UpsertNodeById("Person", "bob_id",
            new Dictionary<string, object> { ["name"] = "Bob",   ["age"] = 25L });

        conn.CreateIndex("Person", "name");

        Assert.True(db.TryIndexLookup("Person", "name", "Alice", out var off0));
        Assert.True(db.TryIndexLookup("Person", "name", "Bob",   out var off1));
        Assert.NotEqual(off0, off1);
    }

    [Fact]
    public void UpsertNodeById_PopulatesIndex_Incrementally()
    {
        using var db = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);

        // Create index FIRST — then subsequent inserts auto-populate it
        conn.CreateIndex("Product", "sku");
        conn.UpsertNodeById("Product", "p1", new Dictionary<string, object> { ["sku"] = "ABC-123" });
        conn.UpsertNodeById("Product", "p2", new Dictionary<string, object> { ["sku"] = "XYZ-999" });

        Assert.True (db.TryIndexLookup("Product", "sku", "ABC-123", out _));
        Assert.True (db.TryIndexLookup("Product", "sku", "XYZ-999", out _));
        Assert.False(db.TryIndexLookup("Product", "sku", "NOT-EXIST", out _));
    }

    [Fact]
    public void TryIndexLookup_DoesNotMatchNodeId_WhenPropertyValueDiffers()
    {
        using var db = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
        {
            ["id"] = BogDb.Core.Common.LogicalTypeID.STRING,
            ["name"] = BogDb.Core.Common.LogicalTypeID.STRING
        });
        conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice",
            ["name"] = "Alicia"
        });
        conn.Commit();

        conn.CreateIndex("Person", "name");

        Assert.True(db.TryIndexLookup("Person", "name", "Alicia", out _));
        Assert.False(db.TryIndexLookup("Person", "name", "alice", out _));
    }

    [Fact]
    public void TryIndexLookup_DropsStalePropertyKey_AfterUpdate()
    {
        using var db = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
        {
            ["id"] = BogDb.Core.Common.LogicalTypeID.STRING,
            ["name"] = BogDb.Core.Common.LogicalTypeID.STRING
        });
        conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice",
            ["name"] = "Alice"
        });
        conn.Commit();

        conn.CreateIndex("Person", "name");
        Assert.True(conn.Query("MATCH (p:Person {name:'Alice'}) SET p.name = 'Alicia'").IsSuccess);

        Assert.True(db.TryIndexLookup("Person", "name", "Alicia", out _));
        Assert.False(db.TryIndexLookup("Person", "name", "Alice", out _));
    }

    // ── UpsertNode path (BogConnection.UpsertNode → UpdateIndexesForNode → UpdateNodeIndexes) ──

    [Fact]
    public void UpsertNode_PopulatesIndex_Via_DirectWrite()
    {
        using var db = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);

        conn.CreateIndex("User", "email");
        conn.BeginWriteTransaction();
        conn.UpsertNode("User", "u1", new Dictionary<string, object> { ["email"] = "alice@test.com" });
        conn.Commit();

        Assert.True(db.TryIndexLookup("User", "email", "alice@test.com", out _));
    }

    [Fact]
    public void UpsertNode_Offsets_Are_Stable_Across_Multiple_Inserts()
    {
        using var db = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);

        conn.CreateIndex("Item", "code");
        conn.BeginWriteTransaction();
        conn.UpsertNode("Item", "i1", new Dictionary<string, object> { ["code"] = "X1" });
        conn.UpsertNode("Item", "i2", new Dictionary<string, object> { ["code"] = "X2" });
        conn.UpsertNode("Item", "i3", new Dictionary<string, object> { ["code"] = "X3" });
        conn.Commit();

        Assert.True(db.TryIndexLookup("Item", "code", "X1", out var off1));
        Assert.True(db.TryIndexLookup("Item", "code", "X2", out var off2));
        Assert.True(db.TryIndexLookup("Item", "code", "X3", out var off3));

        // Offsets should be distinct and monotonically increasing
        Assert.True(off1 < off2);
        Assert.True(off2 < off3);
    }

    // ── BogDatabase.UpdateNodeIndexes (canonical) ────────────────────────────

    [Fact]
    public void UpdateNodeIndexes_NopWhenNoIndexRegistered()
    {
        using var db = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);

        // No index created — should not throw
        conn.UpsertNodeById("Ghost", "g1", new Dictionary<string, object> { ["val"] = 99L });
        Assert.False(db.TryIndexLookup("Ghost", "val", 99L, out _));
    }

    [Fact]
    public void Multiple_Indexes_On_Same_Table_UpdatedTogether()
    {
        using var db = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);

        conn.CreateIndex("Employee", "email");
        conn.CreateIndex("Employee", "dept");

        conn.UpsertNodeById("Employee", "e1",
            new Dictionary<string, object> { ["email"] = "bob@corp.com", ["dept"] = "Eng" });

        Assert.True(db.TryIndexLookup("Employee", "email", "bob@corp.com", out _));
        Assert.True(db.TryIndexLookup("Employee", "dept",  "Eng",          out _));
    }
}
