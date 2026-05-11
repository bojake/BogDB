using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Common;
using BogDb.Core.GraphDataScience;

namespace BogDb.Tests.GraphDataScience;

/// <summary>
/// Tests for the parallel GDS pipeline:
///   - FrontierMorsel / FrontierMorselDispatcher (morsel dispatch correctness)
///   - GdsScheduler (all nodes covered, no duplicates, correct concurrency)
///   - ParallelPageRank (agreement with sequential, non-negative ranks, convergence)
///   - ParallelWCC     (same component count and labels as sequential)
///   - ParallelSSSP    (same distances as sequential BFS)
///   - GdsRegistry routing (IsParallel / Sequential)
/// </summary>
public class GdsParallelismTests
{
    // ── Shared graph fixture (same as GdsStreamingTests) ─────────────────────

    private static (BogDatabase db, BogConnection conn) BuildSocialGraph()
    {
        var db   = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"]   = LogicalTypeID.INT64,
            ["name"] = LogicalTypeID.STRING,
        });
        conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>());

        conn.UpsertNodeById("Person", "0", new Dictionary<string, object> { ["id"]=0L, ["name"]="Alice" });
        conn.UpsertNodeById("Person", "1", new Dictionary<string, object> { ["id"]=1L, ["name"]="Bob"   });
        conn.UpsertNodeById("Person", "2", new Dictionary<string, object> { ["id"]=2L, ["name"]="Carol" });
        conn.UpsertNodeById("Person", "3", new Dictionary<string, object> { ["id"]=3L, ["name"]="Dave"  });
        conn.UpsertNodeById("Person", "4", new Dictionary<string, object> { ["id"]=4L, ["name"]="Eve"   });

        conn.UpsertRelationshipById("KNOWS", "0", "1", new Dictionary<string, object>());
        conn.UpsertRelationshipById("KNOWS", "1", "2", new Dictionary<string, object>());
        conn.UpsertRelationshipById("KNOWS", "0", "2", new Dictionary<string, object>());
        conn.UpsertRelationshipById("KNOWS", "3", "4", new Dictionary<string, object>());
        conn.Commit();

        return (db, conn);
    }

    // ── FrontierMorsel ────────────────────────────────────────────────────────

    [Fact]
    public void FrontierMorselDispatcher_AllOffsetsClaimedExactlyOnce()
    {
        ulong total    = 10_000;
        var dispatcher = new FrontierMorselDispatcher(total, maxThreads: 4);
        var claimed    = new System.Collections.Concurrent.ConcurrentBag<ulong>();

        var tasks = new Task[4];
        for (int t = 0; t < 4; t++)
            tasks[t] = Task.Run(() =>
            {
                while (dispatcher.TryGetNext(out var morsel))
                    for (ulong i = morsel.BeginOffset; i < morsel.EndOffset; i++)
                        claimed.Add(i);
            });
        Task.WaitAll(tasks);

        var sorted = claimed.OrderBy(x => x).ToArray();
        Assert.Equal((int)total, sorted.Length);
        Assert.Equal(0UL, sorted[0]);
        Assert.Equal(total - 1, sorted[^1]);
        // No gaps or duplicates
        for (int i = 1; i < sorted.Length; i++)
            Assert.Equal(sorted[i - 1] + 1, sorted[i]);
    }

    [Fact]
    public void FrontierMorselDispatcher_Reset_AllowsSecondPass()
    {
        var dispatcher = new FrontierMorselDispatcher(100, maxThreads: 1);
        int pass1 = 0;
        while (dispatcher.TryGetNext(out _)) pass1++;

        dispatcher.Reset();
        int pass2 = 0;
        while (dispatcher.TryGetNext(out _)) pass2++;

        Assert.Equal(pass1, pass2);
    }

    [Fact]
    public void FrontierMorsel_Size_IsCorrect()
    {
        var m = new FrontierMorsel(10, 25);
        Assert.Equal(15UL, m.Size);
        Assert.True(m.IsValid);
    }

    // ── GdsScheduler ──────────────────────────────────────────────────────────

    [Fact]
    public void GdsScheduler_RunParallel_CoversAllNodes()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph   = new GraphAdapter(db.NodeTables, db.RelTables);
            var nodes   = NodeList.From(graph);
            var sched   = new GdsScheduler((ulong)nodes.Count, maxDegreeOfParallelism: 4);
            var visited = new System.Collections.Concurrent.ConcurrentBag<ulong>();

            sched.RunParallel(nodes, (morsel, nl) =>
            {
                for (ulong off = morsel.BeginOffset; off < morsel.EndOffset; off++)
                    visited.Add(off);
            });

            Assert.Equal(nodes.Count, visited.Count);
            Assert.Equal(nodes.Count, visited.Distinct().Count());
        }
    }

    [Fact]
    public void GdsScheduler_SingleThread_SameCoverage()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var nodes = NodeList.From(graph);
            int count1 = 0, count2 = 0;

            new GdsScheduler((ulong)nodes.Count, 1).RunParallel(nodes, (m, _) =>
            {
                for (ulong i = m.BeginOffset; i < m.EndOffset; i++)
                    System.Threading.Interlocked.Increment(ref count1);
            });
            new GdsScheduler((ulong)nodes.Count, 4).RunParallel(nodes, (m, _) =>
            {
                for (ulong i = m.BeginOffset; i < m.EndOffset; i++)
                    System.Threading.Interlocked.Increment(ref count2);
            });

            Assert.Equal(count1, count2);
        }
    }

    // ── Parallel PageRank ─────────────────────────────────────────────────────

    [Fact]
    public void ParallelPageRank_AggregateSumApproxN_Nodes()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new ParallelPageRankAlgorithm(graph, maxThreads: 4);
            algo.Execute(new GdsCallOptions { MaxIterations = 20 });

            var results = algo.GetResults().ToList();
            Assert.Equal(5, results.Count);

            double rankSum = results.Sum(r => Convert.ToDouble(r.Values["rank"]));
            // PageRank scores sum should be ≈ 1.0 (within 1%)
            Assert.InRange(rankSum, 0.99, 1.01);
        }
    }

    [Fact]
    public void ParallelPageRank_MatchesSequential_WithinTolerance()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph  = new GraphAdapter(db.NodeTables, db.RelTables);
            var opts   = new GdsCallOptions { MaxIterations = 30, Tolerance = 1e-8 };

            var seqAlgo = new PageRankAlgorithm(graph);
            seqAlgo.Execute(opts);
            var parAlgo = new ParallelPageRankAlgorithm(graph, maxThreads: 4);
            parAlgo.Execute(opts);

            var seqRanks = seqAlgo.GetResults()
                .ToDictionary(r => r.NodeId, r => Convert.ToDouble(r.Values["rank"]));
            var parRanks = parAlgo.GetResults()
                .ToDictionary(r => r.NodeId, r => Convert.ToDouble(r.Values["rank"]));

            foreach (var (nid, seqR) in seqRanks)
            {
                Assert.True(parRanks.ContainsKey(nid), $"Missing node {nid} in parallel result");
                double diff = Math.Abs(parRanks[nid] - seqR);
                Assert.True(diff < 1e-4,
                    $"PageRank mismatch for {nid}: seq={seqR:F8}, par={parRanks[nid]:F8}");
            }
        }
    }

    // ── Parallel WCC ──────────────────────────────────────────────────────────

    [Fact]
    public void ParallelWcc_TwoComponents_CorrectCount()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new ParallelWccAlgorithm(graph, maxThreads: 4);
            algo.Execute(GdsCallOptions.Defaults);

            var components = algo.GetResults()
                .Select(r => Convert.ToInt64(r.Values["component_id"]))
                .Distinct()
                .Count();
            Assert.Equal(2, components);
        }
    }

    [Fact]
    public void ParallelWcc_MatchesSequential_SameLabels()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph  = new GraphAdapter(db.NodeTables, db.RelTables);

            var seqAlgo = new WccAlgorithm(graph);
            seqAlgo.Execute(GdsCallOptions.Defaults);
            var parAlgo = new ParallelWccAlgorithm(graph, maxThreads: 4);
            parAlgo.Execute(GdsCallOptions.Defaults);

            // Both should produce the same number of distinct components
            var seqComps = seqAlgo.GetResults()
                .Select(r => Convert.ToInt64(r.Values["component_id"]))
                .Distinct().Count();
            var parComps = parAlgo.GetResults()
                .Select(r => Convert.ToInt64(r.Values["component_id"]))
                .Distinct().Count();
            Assert.Equal(seqComps, parComps);

            // Same-component membership: nodes that share a label in sequential
            // must also share a label in parallel
            var seqByNode = seqAlgo.GetResults()
                .ToDictionary(r => r.NodeId, r => Convert.ToInt64(r.Values["component_id"]));
            var parByNode = parAlgo.GetResults()
                .ToDictionary(r => r.NodeId, r => Convert.ToInt64(r.Values["component_id"]));

            // Alice(0:0) and Bob(0:1) should be co-located in both
            var alice = new NodeId(0, 0);
            var bob   = new NodeId(1, 0);
            Assert.Equal(
                seqByNode[alice] == seqByNode[bob],
                parByNode[alice] == parByNode[bob]);
        }
    }

    // ── Parallel SSSP ─────────────────────────────────────────────────────────

    [Fact]
    public void ParallelSssp_FromAlice_CorrectDistances()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var alice = new NodeId(0, 0);
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new ParallelSsspAlgorithm(graph, maxThreads: 4);
            algo.Execute(new GdsCallOptions { SourceNode = alice });

            var dists = algo.GetResults()
                .ToDictionary(r => r.NodeId, r => r.Values["distance"]);

            Assert.Equal(0.0,  Convert.ToDouble(dists[new NodeId(0, 0)]));
            Assert.Equal(1.0,  Convert.ToDouble(dists[new NodeId(1, 0)]));
            Assert.Equal(1.0,  Convert.ToDouble(dists[new NodeId(2, 0)]));
            Assert.Null(dists[new NodeId(3, 0)]);  // Dave unreachable
        }
    }

    [Fact]
    public void ParallelSssp_MatchesSequential()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var alice  = new NodeId(0, 0);
            var graph  = new GraphAdapter(db.NodeTables, db.RelTables);
            var opts   = new GdsCallOptions { SourceNode = alice };

            var seqAlgo = new SsspAlgorithm(graph);
            seqAlgo.Execute(opts);
            var parAlgo = new ParallelSsspAlgorithm(graph, maxThreads: 4);
            parAlgo.Execute(opts);

            var seqDists = seqAlgo.GetResults()
                .ToDictionary(r => r.NodeId, r => r.Values["distance"]);
            var parDists = parAlgo.GetResults()
                .ToDictionary(r => r.NodeId, r => r.Values["distance"]);

            foreach (var (nid, seqD) in seqDists)
            {
                Assert.True(parDists.ContainsKey(nid));
                // Both null or both equal numeric value
                Assert.Equal(seqD == null, parDists[nid] == null);
                if (seqD != null)
                    Assert.Equal(Convert.ToDouble(seqD), Convert.ToDouble(parDists[nid]));
            }
        }
    }

    // ── GdsRegistry routing ────────────────────────────────────────────────────

    [Fact]
    public void GdsRegistry_DefaultOptions_CreatesParallelAlgorithm()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = GdsRegistry.Create("pagerank", graph, GdsCallOptions.Defaults);

            Assert.NotNull(algo);
            Assert.IsType<ParallelPageRankAlgorithm>(algo);
        }
    }

    [Fact]
    public void GdsRegistry_SequentialOptions_CreatesSequentialAlgorithm()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = GdsRegistry.Create("pagerank", graph, GdsCallOptions.Sequential);

            Assert.NotNull(algo);
            Assert.IsType<PageRankAlgorithm>(algo);
        }
    }

    [Fact]
    public void GdsRegistry_ParallelWcc_ProducesResults()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = GdsRegistry.Create("wcc", graph, GdsCallOptions.Defaults)!;
            algo.Execute(GdsCallOptions.Defaults);
            Assert.Equal(5, algo.GetResults().Count());
        }
    }

    // ── End-to-end CALL via BogConnection ────────────────────────────────────

    [Fact]
    public void CallPageRankParallel_ViaQuery_Returns5Rows()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            // Default options = parallel
            var r = conn.Query("CALL pagerank() YIELD *");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void PhysicalGdsCall_Parallel_StreamsCorrectRowCount()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var op   = new PhysicalGdsCall(db, "pagerank");
            var rows = new List<Dictionary<string, object?>>();
            while (op.Next(out var row)) rows.Add(row);
            Assert.Equal(5, rows.Count);
        }
    }
}
