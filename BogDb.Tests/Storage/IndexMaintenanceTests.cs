using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Storage;

/// <summary>
/// Verifies that the index lifecycle — insert, update, delete, re-insert —
/// correctly maintains NodePropertyIndex entries, especially that DELETE operations
/// remove stale entries and don't leave phantom results.
/// </summary>
public class IndexMaintenanceTests
{
    // ── Delete removes index entry ─────────────────────────────────────────────

    [Fact]
    public void Delete_RemovesIndexEntry_LookupReturnsNoResults()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY, name STRING)");

        conn.Query("CREATE (:Person {id:'a1', name:'Alice'})");

        // Verify Alice is reachable via indexed lookup
        var before = conn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id");
        Assert.True(before.IsSuccess, before.ErrorMessage);
        Assert.Equal(1UL, before.GetNumTuples());

        // Delete Alice
        var del = conn.Query("MATCH (p:Person {name:'Alice'}) DELETE p");
        Assert.True(del.IsSuccess, del.ErrorMessage);

        // Alice should no longer appear in any lookup
        var after = conn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id");
        Assert.True(after.IsSuccess, after.ErrorMessage);
        Assert.Equal(0UL, after.GetNumTuples());
    }

    [Fact]
    public void Delete_RemovesIndexEntry_IndexCountDecreases()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY, name STRING)");
        // Explicitly create an index on 'name' (PRIMARY KEY auto-indexes 'id')
        db.CreateIndex("Person", "name");

        conn.Query("CREATE (:Person {id:'a1', name:'Alice'})");
        conn.Query("CREATE (:Person {id:'b1', name:'Bob'})");

        var nodeIdx = db.NodeIndexes["Person"];
        Assert.True(nodeIdx.HasIndex("name"));
        var countBefore = nodeIdx.Count("name");
        Assert.Equal(2, countBefore);

        conn.Query("MATCH (p:Person {name:'Alice'}) DELETE p");

        var countAfter = nodeIdx.Count("name");
        Assert.Equal(1, countAfter);
    }

    // ── Delete then re-create ─────────────────────────────────────────────────

    [Fact]
    public void Delete_ThenReinsert_SameKey_Works()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY, name STRING)");

        conn.Query("CREATE (:Person {id:'a1', name:'Alice'})");

        // Delete
        conn.Query("MATCH (p:Person {name:'Alice'}) DELETE p");

        // Re-insert with same key value
        conn.Query("CREATE (:Person {id:'a2', name:'Alice'})");

        var result = conn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        Assert.Equal("a2", result.GetNext().GetString(0));
    }

    // ── Update indexed property then delete ────────────────────────────────────

    [Fact]
    public void Update_IndexedProperty_ThenDelete_CleansUp()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY, name STRING)");

        conn.Query("CREATE (:Person {id:'a1', name:'Alice'})");

        // Update: Alice -> Alicia
        conn.Query("MATCH (p:Person {name:'Alice'}) SET p.name = 'Alicia'");

        // Old key should be gone from index after refresh
        var oldResult = conn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id");
        Assert.True(oldResult.IsSuccess, oldResult.ErrorMessage);
        Assert.Equal(0UL, oldResult.GetNumTuples());

        // New key should work
        var newResult = conn.Query("MATCH (p:Person {name:'Alicia'}) RETURN p.id");
        Assert.True(newResult.IsSuccess, newResult.ErrorMessage);
        Assert.Equal(1UL, newResult.GetNumTuples());

        // Now delete with the updated key
        conn.Query("MATCH (p:Person {name:'Alicia'}) DELETE p");

        // Neither key should return results
        var afterDelete = conn.Query("MATCH (p:Person {name:'Alicia'}) RETURN p.id");
        Assert.True(afterDelete.IsSuccess, afterDelete.ErrorMessage);
        Assert.Equal(0UL, afterDelete.GetNumTuples());
    }

    // ── Delete one of multiple indexed nodes ───────────────────────────────────

    [Fact]
    public void Delete_OneNode_OtherIndexedNodesSurvive()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY, name STRING)");

        conn.Query("CREATE (:Person {id:'a1', name:'Alice'})");
        conn.Query("CREATE (:Person {id:'b1', name:'Bob'})");
        conn.Query("CREATE (:Person {id:'c1', name:'Charlie'})");

        // Delete Bob
        conn.Query("MATCH (p:Person {name:'Bob'}) DELETE p");

        // Alice and Charlie should still resolve
        var alice = conn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id");
        Assert.True(alice.IsSuccess, alice.ErrorMessage);
        Assert.Equal(1UL, alice.GetNumTuples());

        var charlie = conn.Query("MATCH (p:Person {name:'Charlie'}) RETURN p.id");
        Assert.True(charlie.IsSuccess, charlie.ErrorMessage);
        Assert.Equal(1UL, charlie.GetNumTuples());

        // Bob should not
        var bob = conn.Query("MATCH (p:Person {name:'Bob'}) RETURN p.id");
        Assert.True(bob.IsSuccess, bob.ErrorMessage);
        Assert.Equal(0UL, bob.GetNumTuples());
    }

    // ── Full lifecycle via Cypher ──────────────────────────────────────────────

    [Fact]
    public void FullLifecycle_Create_Lookup_Update_Delete_ReInsert()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY, name STRING)");

        // 1. Create
        conn.Query("CREATE (:Person {id:'a1', name:'Alice'})");

        // 2. Lookup succeeds
        var lookup1 = conn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id");
        Assert.Equal(1UL, lookup1.GetNumTuples());

        // 3. Update indexed property
        conn.Query("MATCH (p:Person {name:'Alice'}) SET p.name = 'Alicia'");

        // Old name gone, new name works
        Assert.Equal(0UL, conn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id").GetNumTuples());
        Assert.Equal(1UL, conn.Query("MATCH (p:Person {name:'Alicia'}) RETURN p.id").GetNumTuples());

        // 4. Delete
        conn.Query("MATCH (p:Person {name:'Alicia'}) DELETE p");
        Assert.Equal(0UL, conn.Query("MATCH (p:Person {name:'Alicia'}) RETURN p.id").GetNumTuples());

        // 5. Re-insert with original name
        conn.Query("CREATE (:Person {id:'a3', name:'Alice'})");
        var reinsert = conn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id");
        Assert.Equal(1UL, reinsert.GetNumTuples());
        Assert.Equal("a3", reinsert.GetNext().GetString(0));
    }

    // ── WHERE-backed index lookup after delete ────────────────────────────────

    [Fact]
    public void WhereBackedIndexLookup_AfterDelete_ReturnsEmpty()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY, name STRING)");

        conn.Query("CREATE (:Person {id:'a1', name:'Alice'})");

        conn.Query("MATCH (p:Person) WHERE p.name = 'Alice' DELETE p");

        var result = conn.Query("MATCH (p:Person) WHERE p.name = 'Alice' RETURN p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(0UL, result.GetNumTuples());
    }

    // ── DETACH DELETE around relationships ────────────────────────────────────

    [Fact]
    public void DetachDelete_CleansUpIndexEntries()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY, name STRING)");
        conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)");

        conn.Query("CREATE (:Person {id:'a1', name:'Alice'})");
        conn.Query("CREATE (:Person {id:'b1', name:'Bob'})");
        conn.Query("MATCH (a:Person {name:'Alice'}), (b:Person {name:'Bob'}) CREATE (a)-[:KNOWS]->(b)");

        // DETACH DELETE Alice (removes node + connected edges)
        conn.Query("MATCH (p:Person {name:'Alice'}) DETACH DELETE p");

        // Alice should be gone from index
        var alice = conn.Query("MATCH (p:Person {name:'Alice'}) RETURN p.id");
        Assert.True(alice.IsSuccess, alice.ErrorMessage);
        Assert.Equal(0UL, alice.GetNumTuples());

        // Bob should still be reachable
        var bob = conn.Query("MATCH (p:Person {name:'Bob'}) RETURN p.id");
        Assert.True(bob.IsSuccess, bob.ErrorMessage);
        Assert.Equal(1UL, bob.GetNumTuples());
    }
}
