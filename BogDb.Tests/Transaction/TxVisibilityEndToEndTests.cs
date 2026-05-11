using BogDb.Core.Common;
using BogDb.Core.Main;
using System.Linq;
using Xunit;

namespace BogDb.Tests.Transaction;

/// <summary>
/// End-to-end transaction visibility tests that use the full BogDatabase/BogConnection
/// API stack — not storage-unit stubs.
///
/// Each test group proves one isolation rule through the graph query path:
///   1. Uncommitted writes visible inside writer Tx (node + rel)
///   2. Rollback removes writes from reader
///   3. Commit makes writes visible to reader
///   4. Property mutations (node + rel) visible in Tx, hidden after rollback
///   5. Delete/reinsert same external-ID survive commit
///   6. Traversal operators respect snapshot visibility across rel version changes
///   7. Mixed node+rel mutations are atomic (all-or-nothing on rollback)
///   8. Repeated commit cycles show no stale overlay bleed
/// </summary>
public sealed class TxVisibilityEndToEndTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BogDatabase BuildFreshGraph()
    {
        var db   = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);
        conn.BeginWriteTransaction();

        conn.EnsureNodeTable("Person", new()
        {
            ["name"]  = LogicalTypeID.STRING,
            ["score"] = LogicalTypeID.INT64,
        });
        conn.EnsureRelTable("KNOWS", "Person", "Person", new()
        {
            ["weight"] = LogicalTypeID.INT64,
        });

        conn.UpsertNode("Person", "alice", new() { ["name"] = "Alice", ["score"] = 10L });
        conn.UpsertNode("Person", "bob",   new() { ["name"] = "Bob",   ["score"] = 20L });
        conn.UpsertRelationship("KNOWS", "alice", "bob", new() { ["weight"] = 5L });
        conn.Commit();
        conn.Dispose();
        return db;
    }

    private static List<Dictionary<string, object?>> Query(BogConnection conn, string cypher)
    {
        var r = conn.Query(cypher);
        if (!r.IsSuccess)
            throw new Exception($"Query failed: {r.ErrorMessage}");
        var rows = new List<Dictionary<string, object?>>();
        while (r.HasNext())
            rows.Add(r.GetNext().GetAsDictionary().ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
        return rows;
    }

    private static BogDatabase BuildTraversalGraph()
    {
        var db = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);
        conn.BeginWriteTransaction();

        conn.EnsureNodeTable("Person", new()
        {
            ["name"] = LogicalTypeID.STRING,
            ["score"] = LogicalTypeID.INT64,
        });
        conn.EnsureRelTable("KNOWS", "Person", "Person", new()
        {
            ["since"] = LogicalTypeID.INT64,
        });
        conn.EnsureRelTable("LIKES", "Person", "Person", new()
        {
            ["weight"] = LogicalTypeID.INT64,
        });

        conn.UpsertNode("Person", "alice", new() { ["name"] = "Alice", ["score"] = 10L });
        conn.UpsertNode("Person", "bob", new() { ["name"] = "Bob", ["score"] = 20L });
        conn.UpsertNode("Person", "carol", new() { ["name"] = "Carol", ["score"] = 30L });

        conn.UpsertRelationship("KNOWS", "alice", "bob", new() { ["since"] = 2020L });
        conn.UpsertRelationship("KNOWS", "bob", "carol", new() { ["since"] = 2021L });
        conn.UpsertRelationship("LIKES", "alice", "carol", new() { ["weight"] = 1L });

        conn.Commit();
        conn.Dispose();
        return db;
    }

    // ── 1. Uncommitted writes visible inside writer Tx ─────────────────────

    [Fact]
    public void UncommittedWrite_VisibleInsideWriterTx_Node()
    {
        using var db   = BuildFreshGraph();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", "carol", new() { ["name"] = "Carol", ["score"] = 30L });

        // Same connection inside the open write TX must see the uncommitted node
        var rows = Query(conn, "MATCH (p:Person {name:'Carol'}) RETURN p.score AS s");
        Assert.Single(rows);
        Assert.Equal(30L, rows[0]["s"]);
    }

    [Fact]
    public void UncommittedWrite_VisibleInsideWriterTx_Rel()
    {
        using var db   = BuildFreshGraph();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", "carol",  new() { ["name"] = "Carol",  ["score"] = 30L });
        conn.UpsertNode("Person", "danielle", new() { ["name"] = "Danielle", ["score"] = 40L });
        conn.UpsertRelationship("KNOWS", "carol", "danielle", new() { ["weight"] = 99L });

        var rows = Query(conn,
            "MATCH (a:Person {name:'Carol'})-[r:KNOWS]->(b:Person {name:'Danielle'}) RETURN r.weight AS w");
        Assert.Single(rows);
        Assert.Equal(99L, rows[0]["w"]);
    }

    // ── 2. Rollback removes writes from reader ─────────────────────────────

    [Fact]
    public void Rollback_RemovesNodeWrite_FromReader()
    {
        using var db    = BuildFreshGraph();
        using var write = new BogConnection(db);
        using var read  = new BogConnection(db);

        write.BeginWriteTransaction();
        write.UpsertNode("Person", "ephemeral", new() { ["name"] = "Ephemeral", ["score"] = 99L });

        // Reader must not see the uncommitted node (serialisable read isolation)
        var rowsBeforeRollback = Query(read,
            "MATCH (p:Person {name:'Ephemeral'}) RETURN count(p) AS cnt");
        // actual result may be 0 rows or single row with cnt=0
        var countBefore = rowsBeforeRollback.Count > 0
            ? Convert.ToInt64(rowsBeforeRollback[0]["cnt"] ?? 0L) : 0L;
        Assert.Equal(0L, countBefore);

        write.ClientContext.Rollback();

        var rowsAfter = Query(read,
            "MATCH (p:Person {name:'Ephemeral'}) RETURN count(p) AS cnt");
        var countAfter = rowsAfter.Count > 0
            ? Convert.ToInt64(rowsAfter[0]["cnt"] ?? 0L) : 0L;
        Assert.Equal(0L, countAfter);
    }

    [Fact]
    public void Rollback_RemovesRelWrite_FromReader()
    {
        using var db    = BuildFreshGraph();
        using var write = new BogConnection(db);
        using var read  = new BogConnection(db);

        // Add carol first in a committed TX
        write.BeginWriteTransaction();
        write.UpsertNode("Person", "carol", new() { ["name"] = "Carol", ["score"] = 30L });
        write.Commit();

        // Now open a write TX, add a rel, then rollback
        write.BeginWriteTransaction();
        write.UpsertRelationship("KNOWS", "carol", "alice", new() { ["weight"] = 77L });

        write.ClientContext.Rollback();

        var rows = Query(read,
            "MATCH (:Person {name:'Carol'})-[r:KNOWS]->(:Person {name:'Alice'}) RETURN r.weight AS w");
        Assert.Empty(rows);
    }

    // ── 3. Commit makes writes visible to reader ───────────────────────────

    [Fact]
    public void Commit_MakesNodeWrite_VisibleToReader()
    {
        using var db    = BuildFreshGraph();
        using var write = new BogConnection(db);
        using var read  = new BogConnection(db);

        write.BeginWriteTransaction();
        write.UpsertNode("Person", "frank", new() { ["name"] = "Frank", ["score"] = 55L });
        write.Commit();

        var rows = Query(read, "MATCH (p:Person {name:'Frank'}) RETURN p.score AS s");
        Assert.Single(rows);
        Assert.Equal(55L, rows[0]["s"]);
    }

    [Fact]
    public void Commit_MakesRelWrite_VisibleToReader()
    {
        using var db    = BuildFreshGraph();
        using var write = new BogConnection(db);
        using var read  = new BogConnection(db);

        write.BeginWriteTransaction();
        write.UpsertNode("Person", "grace", new() { ["name"] = "Grace", ["score"] = 60L });
        write.UpsertRelationship("KNOWS", "alice", "grace", new() { ["weight"] = 42L });
        write.Commit();

        var rows = Query(read,
            "MATCH (:Person)-[r:KNOWS]->(:Person {name:'Grace'}) RETURN r.weight AS w");
        // The committed alice→grace rel must be visible to the reader
        Assert.Contains(rows, r => Convert.ToInt64(r["w"]) == 42L);
    }

    // ── 4. Property mutations visible in Tx, hidden after rollback ─────────

    [Fact]
    public void NodeProperty_Mutation_VisibleInTx_HiddenAfterRollback()
    {
        using var db   = BuildFreshGraph();
        using var conn = new BogConnection(db);

        // Mutate alice.score inside a TX
        conn.BeginWriteTransaction();
        conn.Query("MATCH (p:Person {name:'Alice'}) SET p.score = 999");

        var inTx = Query(conn, "MATCH (p:Person {name:'Alice'}) RETURN p.score AS s");
        Assert.Single(inTx);
        Assert.Equal(999L, Convert.ToInt64(inTx[0]["s"]));

        conn.ClientContext.Rollback();

        var afterRollback = Query(conn, "MATCH (p:Person {name:'Alice'}) RETURN p.score AS s");
        Assert.Single(afterRollback);
        Assert.Equal(10L, Convert.ToInt64(afterRollback[0]["s"]));
    }

    [Fact]
    public void RelProperty_Mutation_VisibleInTx_HiddenAfterRollback()
    {
        using var db   = BuildFreshGraph();
        using var conn = new BogConnection(db);

        // Mutate alice-KNOWS->bob weight inside a TX
        conn.BeginWriteTransaction();
        conn.Query("MATCH (:Person {name:'Alice'})-[r:KNOWS]->(:Person {name:'Bob'}) SET r.weight = 888");

        var inTx = Query(conn,
            "MATCH (:Person {name:'Alice'})-[r:KNOWS]->(:Person {name:'Bob'}) RETURN r.weight AS w");
        Assert.Single(inTx);
        Assert.Equal(888L, Convert.ToInt64(inTx[0]["w"]));

        conn.ClientContext.Rollback();

        var afterRollback = Query(conn,
            "MATCH (:Person {name:'Alice'})-[r:KNOWS]->(:Person {name:'Bob'}) RETURN r.weight AS w");
        Assert.Single(afterRollback);
        Assert.Equal(5L, Convert.ToInt64(afterRollback[0]["w"]));
    }

    // ── 5. Delete/reinsert same external-ID survives commit ────────────────

    [Fact]
    public void DeleteReinsert_SameKey_SurvivesCommit()
    {
        using var db   = BuildFreshGraph();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.Query("MATCH (p:Person {name:'Alice'}) DETACH DELETE p");
        conn.UpsertNode("Person", "alice", new() { ["name"] = "Alice-New", ["score"] = 777L });
        conn.Commit();

        var rows = Query(conn, "MATCH (p:Person) WHERE p.score = 777 RETURN p.name AS n");
        Assert.Single(rows);
        Assert.Equal("Alice-New", rows[0]["n"]?.ToString());
    }

    // ── 6. Traversal operators respect snapshot visibility ──────────────────

    [Fact]
    public void MultiRelTraversal_OldReaderSeesOldVisibleRel_AfterDeleteReinsertCommit()
    {
        using var db = BuildTraversalGraph();
        using var oldRead = new BogConnection(db);
        using var write = new BogConnection(db);
        using var newRead = new BogConnection(db);

        oldRead.ClientContext.StartTransaction(BogDb.Core.Transaction.TransactionType.READ_ONLY);

        write.BeginWriteTransaction();
        var deleteResult = write.Query(
            "MATCH (:Person {name:'Alice'})-[r:KNOWS]->(:Person {name:'Bob'}) DELETE r");
        Assert.True(deleteResult.IsSuccess, deleteResult.ErrorMessage);
        write.UpsertRelationship("KNOWS", "alice", "bob", new() { ["since"] = 2024L });
        write.Commit();

        var oldRows = Query(oldRead,
            "MATCH (a:Person)-[r:KNOWS|LIKES]->(b:Person) " +
            "WHERE a.name = 'Alice' AND b.name = 'Bob' " +
            "RETURN type(r) AS t, r.since AS since");
        Assert.Single(oldRows);
        Assert.Equal("KNOWS", oldRows[0]["t"]);
        Assert.Equal(2020L, Convert.ToInt64(oldRows[0]["since"]));

        var newRows = Query(newRead,
            "MATCH (a:Person)-[r:KNOWS|LIKES]->(b:Person) " +
            "WHERE a.name = 'Alice' AND b.name = 'Bob' " +
            "RETURN type(r) AS t, r.since AS since");
        Assert.Single(newRows);
        Assert.Equal("KNOWS", newRows[0]["t"]);
        Assert.Equal(2024L, Convert.ToInt64(newRows[0]["since"]));
    }

    [Fact]
    public void DeleteReinsertThenUpdate_OldAndIntermediateReadersKeepCorrectSnapshots()
    {
        using var db = BuildFreshGraph();
        using var oldRead = new BogConnection(db);
        using var midRead = new BogConnection(db);
        using var write = new BogConnection(db);
        using var newRead = new BogConnection(db);

        oldRead.ClientContext.StartTransaction(BogDb.Core.Transaction.TransactionType.READ_ONLY);

        write.BeginWriteTransaction();
        var deleteResult = write.Query("MATCH (p:Person {name:'Alice'}) DETACH DELETE p");
        Assert.True(deleteResult.IsSuccess, deleteResult.ErrorMessage);
        write.UpsertNode("Person", "alice", new() { ["name"] = "Alice-New", ["score"] = 777L });
        write.Commit();

        midRead.ClientContext.StartTransaction(BogDb.Core.Transaction.TransactionType.READ_ONLY);

        write.BeginWriteTransaction();
        var updateResult = write.Query("MATCH (p:Person {name:'Alice-New'}) SET p.score = 888");
        Assert.True(updateResult.IsSuccess, updateResult.ErrorMessage);
        write.Commit();

        var oldRows = Query(oldRead, "MATCH (p:Person {name:'Alice'}) RETURN p.score AS s");
        Assert.Single(oldRows);
        Assert.Equal(10L, Convert.ToInt64(oldRows[0]["s"]));

        var midRows = Query(midRead, "MATCH (p:Person {name:'Alice-New'}) RETURN p.score AS s");
        Assert.Single(midRows);
        Assert.Equal(777L, Convert.ToInt64(midRows[0]["s"]));

        var newRows = Query(newRead, "MATCH (p:Person {name:'Alice-New'}) RETURN p.score AS s");
        Assert.Single(newRows);
        Assert.Equal(888L, Convert.ToInt64(newRows[0]["s"]));
    }

    [Fact]
    public void RecursiveTraversal_OldReaderSeesOldVisiblePath_AfterCommittedRelDelete()
    {
        using var db = BuildTraversalGraph();
        using var oldRead = new BogConnection(db);
        using var write = new BogConnection(db);
        using var newRead = new BogConnection(db);

        oldRead.ClientContext.StartTransaction(BogDb.Core.Transaction.TransactionType.READ_ONLY);

        write.BeginWriteTransaction();
        var deleteResult = write.Query(
            "MATCH (:Person {name:'Bob'})-[r:KNOWS]->(:Person {name:'Carol'}) DELETE r");
        Assert.True(deleteResult.IsSuccess, deleteResult.ErrorMessage);
        write.Commit();

        var oldRows = Query(oldRead,
            "MATCH (a:Person)-[p:KNOWS*1..2]->(b:Person) " +
            "WHERE a.name = 'Alice' RETURN b.name AS n ORDER BY n");
        Assert.Equal(["Bob", "Carol"], oldRows.Select(r => r["n"]?.ToString() ?? string.Empty).ToArray());

        var newRows = Query(newRead,
            "MATCH (a:Person)-[p:KNOWS*1..2]->(b:Person) " +
            "WHERE a.name = 'Alice' RETURN b.name AS n ORDER BY n");
        Assert.Equal(["Bob"], newRows.Select(r => r["n"]?.ToString() ?? string.Empty).ToArray());
    }

    // ── 7. Mixed mutations are atomic (all-or-nothing on rollback) ─────────

    [Fact]
    public void MixedNodeRel_Mutations_InOneTx_AllOrNothing()
    {
        using var db   = BuildFreshGraph();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", "henry",  new() { ["name"] = "Henry", ["score"] = 11L });
        conn.UpsertNode("Person", "irene", new() { ["name"] = "Irene", ["score"] = 22L });
        conn.UpsertRelationship("KNOWS", "henry", "irene", new() { ["weight"] = 33L });
        conn.Query("MATCH (p:Person {name:'Bob'}) SET p.score = 99");

        // All 4 mutations visible before rollback
        Assert.Single(Query(conn, "MATCH (p:Person {name:'Henry'}) RETURN p"));
        Assert.Single(Query(conn, "MATCH (p:Person {name:'Irene'}) RETURN p"));
        Assert.Single(Query(conn,
            "MATCH (:Person {name:'Henry'})-[r:KNOWS]->(:Person {name:'Irene'}) RETURN r"));
        var bobInTx = Query(conn, "MATCH (p:Person {name:'Bob'}) RETURN p.score AS s");
        Assert.Equal(99L, Convert.ToInt64(bobInTx[0]["s"]));

        conn.ClientContext.Rollback();

        // After rollback: all 4 gone / reverted
        Assert.Empty(Query(conn, "MATCH (p:Person {name:'Henry'}) RETURN p"));
        Assert.Empty(Query(conn, "MATCH (p:Person {name:'Irene'}) RETURN p"));
        var bobAfter = Query(conn, "MATCH (p:Person {name:'Bob'}) RETURN p.score AS s");
        Assert.Equal(20L, Convert.ToInt64(bobAfter[0]["s"]));
    }

    // ── 8. Repeated commit cycles — no stale overlay bleed ─────────────────

    [Fact]
    public void RepeatedCommitCycles_NoStaleOverlayBleed()
    {
        using var db   = BuildFreshGraph();
        using var conn = new BogConnection(db);

        for (int i = 0; i < 5; i++)
        {
            conn.BeginWriteTransaction();
            conn.UpsertNode("Person", $"cycle-{i}",
                new() { ["name"] = $"Cycle{i}", ["score"] = (long)i });
            conn.Commit();
        }

        // After 5 commit cycles, all 5 + original 2 nodes exist, no duplication
        var rows = Query(conn, "MATCH (p:Person) RETURN count(p) AS cnt");
        var total = Convert.ToInt64(rows[0]["cnt"]);
        Assert.Equal(7L, total); // 2 original + 5 cycled

        // Each cycle node has exactly one entry
        for (int i = 0; i < 5; i++)
        {
            var r = Query(conn, $"MATCH (p:Person {{name:'Cycle{i}'}}) RETURN count(p) AS cnt");
            Assert.Equal(1L, Convert.ToInt64(r[0]["cnt"]));
        }
    }
}
