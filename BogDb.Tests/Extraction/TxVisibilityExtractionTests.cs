using BogDb.Core.Common;
using BogDb.Core.Extraction;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Extraction;

/// <summary>
/// End-to-end transaction visibility tests for the Extraction and Runtime paths.
///
/// Complements <see cref="GraphExtractionApiTests"/> which covers the basic
/// "extract in Tx / extract after commit / extract after rollback" cases via
/// <c>ExtractGraphNeighborhood</c>. This file adds:
///
///   - Snapshot extract before commit (uncommitted data visible to same connection)
///   - Adjacency expansion after rel property mutation
///   - Adjacency expansion after rel DELETE
///   - Mixed node+rel mutations — extraction consistency guarantee
///   - Repeated commits — no stale extraction data
/// </summary>
public sealed class TxVisibilityExtractionTests
{
    // ── Graph factory ─────────────────────────────────────────────────────────

    private static BogDatabase BuildGraph()
    {
        var db   = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);
        conn.BeginWriteTransaction();

        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["name"]  = LogicalTypeID.STRING,
            ["score"] = LogicalTypeID.INT64,
        });
        conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
        {
            ["weight"] = LogicalTypeID.INT64,
        });

        conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["name"] = "Alice", ["score"] = 10L });
        conn.UpsertNode("Person", "bob",   new Dictionary<string, object> { ["name"] = "Bob",   ["score"] = 20L });
        conn.UpsertNode("Person", "carol", new Dictionary<string, object> { ["name"] = "Carol", ["score"] = 30L });
        conn.UpsertRelationship("KNOWS", "alice", "bob",   new Dictionary<string, object> { ["weight"] = 5L });
        conn.UpsertRelationship("KNOWS", "bob",   "carol", new Dictionary<string, object> { ["weight"] = 7L });
        conn.Commit();
        conn.Dispose();
        return db;
    }

    private static GraphShardExtractionOptions Opts(int depth = 1) => new()
    {
        MaxDepth        = depth,
        IncludeOutgoing = true,
        IncludeIncoming = false,
    };

    // ── 1. Extract before commit — uncommitted write visible to same connection

    [Fact]
    public void Extract_BeforeCommit_SeesUncommitted_Node()
    {
        using var db   = BuildGraph();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", "dave", new Dictionary<string, object> { ["name"] = "Dave", ["score"] = 40L });
        conn.UpsertRelationship("KNOWS", "carol", "dave", new Dictionary<string, object> { ["weight"] = 9L });

        // Snapshot from inside the open write Tx must include the uncommitted dave
        var shard = conn.ExtractGraphNeighborhood("Person", "carol", Opts());
        Assert.Contains(shard.NodeTables["Person"].Rows,
            r => r.ExternalId == "node:Person:dave");

        conn.ClientContext.Rollback();
    }

    [Fact]
    public void Extract_BeforeCommit_SeesUncommitted_RelProperty()
    {
        using var db   = BuildGraph();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        // Mutate alice→bob weight from 5 to 99
        conn.Query("MATCH (:Person {name:'Alice'})-[r:KNOWS]->(:Person {name:'Bob'}) SET r.weight = 99");

        var shard = conn.ExtractGraphNeighborhood("Person", "alice", Opts());
        var rel   = shard.RelTables["KNOWS"].Rows.FirstOrDefault(
            r => r.Properties.ContainsKey("weight"));
        Assert.NotNull(rel);
        Assert.Equal(99L, Convert.ToInt64(rel.Properties["weight"]));

        conn.ClientContext.Rollback();
    }

    // ── 2. Extract after commit — committed data visible to reader ────────────

    [Fact]
    public void Extract_AfterCommit_SeesCommitted()
    {
        using var db    = BuildGraph();
        using var write = new BogConnection(db);
        using var read  = new BogConnection(db);

        write.BeginWriteTransaction();
        write.UpsertNode("Person", "eve", new Dictionary<string, object> { ["name"] = "Eve", ["score"] = 50L });
        write.UpsertRelationship("KNOWS", "carol", "eve", new Dictionary<string, object> { ["weight"] = 11L });
        write.Commit();

        var shard = read.ExtractGraphNeighborhood("Person", "carol", Opts());
        Assert.Contains(shard.NodeTables["Person"].Rows,
            r => r.ExternalId == "node:Person:eve");
    }

    // ── 3. Extract after rollback — rolled-back write not visible ─────────────

    [Fact]
    public void Extract_AfterRollback_HidesWrite()
    {
        using var db    = BuildGraph();
        using var write = new BogConnection(db);
        using var read  = new BogConnection(db);

        write.BeginWriteTransaction();
        write.UpsertNode("Person", "frank", new Dictionary<string, object> { ["name"] = "Frank", ["score"] = 60L });
        write.UpsertRelationship("KNOWS", "carol", "frank", new Dictionary<string, object> { ["weight"] = 13L });
        write.ClientContext.Rollback();

        var shard = read.ExtractGraphNeighborhood("Person", "carol", Opts());
        Assert.DoesNotContain(shard.NodeTables["Person"].Rows,
            r => r.ExternalId == "node:Person:frank");
    }

    // ── 4. Adjacency expansion after rel property update ─────────────────────

    [Fact]
    public void AdjacencyExpansion_AfterRelPropertyUpdate_ReflectsNewValue()
    {
        using var db   = BuildGraph();
        using var conn = new BogConnection(db);

        // Commit a rel property update
        conn.BeginWriteTransaction();
        conn.Query("MATCH (:Person {name:'Alice'})-[r:KNOWS]->(:Person {name:'Bob'}) SET r.weight = 777");
        conn.Commit();

        // Re-extract alice's neighbourhood — rel weight must be updated
        var shard = conn.ExtractGraphNeighborhood("Person", "alice", Opts());
        var rel   = shard.RelTables["KNOWS"].Rows.FirstOrDefault(
            r => r.Properties.ContainsKey("weight"));
        Assert.NotNull(rel);
        Assert.Equal(777L, Convert.ToInt64(rel.Properties["weight"]));
    }

    // ── 5. Adjacency expansion after rel DELETE ────────────────────────────────

    [Fact]
    public void AdjacencyExpansion_AfterRelDelete_RelNotVisible()
    {
        using var db   = BuildGraph();
        using var conn = new BogConnection(db);

        // Delete alice→bob KNOWS rel and commit
        conn.BeginWriteTransaction();
        conn.Query("MATCH (:Person {name:'Alice'})-[r:KNOWS]->(:Person {name:'Bob'}) DELETE r");
        conn.Commit();

        var shard = conn.ExtractGraphNeighborhood("Person", "alice", Opts());

        // KNOWS table may be absent or empty — either is correct
        bool hasKnowsRel = shard.RelTables.TryGetValue("KNOWS", out var knowsTable) &&
                           knowsTable.Rows.Count > 0;
        Assert.False(hasKnowsRel,
            "Deleted rel should not appear in extraction after commit");
    }

    // ── 6. Mixed node+rel mutations — extraction consistency ──────────────────

    [Fact]
    public void MixedNodeAndRel_MutationsInOneTx_ExtractionConsistent()
    {
        using var db   = BuildGraph();
        using var conn = new BogConnection(db);

        // Inside one TX: add grace, add grace→carol rel, mutate carol.score
        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", "grace", new Dictionary<string, object> { ["name"] = "Grace", ["score"] = 70L });
        conn.UpsertRelationship("KNOWS", "grace", "carol", new Dictionary<string, object> { ["weight"] = 55L });
        conn.Query("MATCH (p:Person {name:'Carol'}) SET p.score = 999");
        conn.Commit();

        // Extract grace's neighbourhood — must see carol with updated score
        var shard = conn.ExtractGraphNeighborhood("Person", "grace", new GraphShardExtractionOptions
        {
            MaxDepth        = 1,
            IncludeOutgoing = true,
            IncludeIncoming = false,
        });

        var carolRow = shard.NodeTables["Person"].Rows
            .FirstOrDefault(r => r.ExternalId == "node:Person:carol");
        Assert.NotNull(carolRow);
        Assert.Equal(999L, Convert.ToInt64(carolRow.Properties["score"]));
    }

    // ── 7. Repeated commits — no stale extraction data ────────────────────────

    [Fact]
    public void RepeatedCommits_ExtractionNoStaleness()
    {
        using var db   = BuildGraph();
        using var conn = new BogConnection(db);

        // 4 commit cycles — each adds a new node hanging off alice
        for (int i = 0; i < 4; i++)
        {
            conn.BeginWriteTransaction();
            conn.UpsertNode("Person", $"iter-{i}",
                new Dictionary<string, object> { ["name"] = $"Iter{i}", ["score"] = (long)i });
            conn.UpsertRelationship("KNOWS", "alice", $"iter-{i}",
                new Dictionary<string, object> { ["weight"] = (long)(i * 10) });
            conn.Commit();
        }

        // Extract alice at depth=1 — must include all 4 iteration nodes, no duplication
        var shard = conn.ExtractGraphNeighborhood("Person", "alice", Opts(depth: 1));
        for (int i = 0; i < 4; i++)
        {
            var matches = shard.NodeTables["Person"].Rows
                .Where(r => r.ExternalId == $"node:Person:iter-{i}").ToList();
            Assert.True(matches.Count == 1,
                $"iter-{i} should appear exactly once in extraction, but found {matches.Count} copies — potential stale overlay bleed after repeated commits");
        }
    }

    [Fact]
    public void Extract_RelDeleteReinsert_RespectsOldAndNewSnapshots()
    {
        using var db = BuildGraph();
        using var oldConn = new BogConnection(db);
        using var writeConn = new BogConnection(db);
        using var newConn = new BogConnection(db);

        oldConn.ClientContext.StartTransaction(BogDb.Core.Transaction.TransactionType.READ_ONLY);
        var oldReader = oldConn.ClientContext.ActiveTransaction;

        writeConn.BeginWriteTransaction();
        var writer = writeConn.ClientContext.ActiveTransaction;

        var relTable = db.RelTables["KNOWS"];
        var edge = new EdgeKey("alice", "bob");
        Assert.True(relTable.Remove(writer, edge));
        relTable.Upsert(writer, edge, new Dictionary<string, object> { ["weight"] = 99L });
        writeConn.Commit();

        var extractor = new GraphShardExtractor(db);

        var oldShard = extractor.ExtractNeighborhood("Person", "alice", Opts(), oldReader);
        var oldRel = Assert.Single(oldShard.RelTables["KNOWS"].Rows);
        Assert.Equal(5L, Convert.ToInt64(oldRel.Properties["weight"]));

        newConn.ClientContext.StartTransaction(BogDb.Core.Transaction.TransactionType.READ_ONLY);
        var newReader = newConn.ClientContext.ActiveTransaction;
        var newShard = extractor.ExtractNeighborhood("Person", "alice", Opts(), newReader);
        var newRel = Assert.Single(newShard.RelTables["KNOWS"].Rows);
        Assert.Equal(99L, Convert.ToInt64(newRel.Properties["weight"]));
    }
}
