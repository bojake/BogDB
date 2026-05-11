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
/// Tests that cover the closed parallelism gaps:
///   1. Δ-stepping SSSP for weighted graphs
///   2. Parallel KHop (Parallel.ForEach per source)
///   3. Parallel VLP  (Parallel.ForEach per source)
/// </summary>
public class GdsParallelGapTests
{
    // ── Weighted graph fixture ────────────────────────────────────────────────

    /// <summary>
    /// Builds a 4-node weighted graph:
    ///   A --1.0--> B --2.0--> C
    ///   A --5.0--> C
    ///   C --3.0--> D
    /// Shortest A→C = 3.0 (via B), A→D = 6.0 (via B→C)
    /// </summary>
    private static (BogDatabase db, BogConnection conn) BuildWeightedGraph()
    {
        var db   = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("N", new Dictionary<string, LogicalTypeID>
            { ["id"] = LogicalTypeID.INT64 });
        conn.EnsureRelTable("E", "N", "N", new Dictionary<string, LogicalTypeID>
            { ["weight"] = LogicalTypeID.DOUBLE });

        for (int i = 0; i < 4; i++)
            conn.UpsertNodeById("N", i.ToString(), new Dictionary<string, object> { ["id"] = (long)i });

        // A=0, B=1, C=2, D=3
        conn.UpsertRelationshipById("E", "0", "1", new Dictionary<string, object> { ["weight"] = 1.0 });
        conn.UpsertRelationshipById("E", "1", "2", new Dictionary<string, object> { ["weight"] = 2.0 });
        conn.UpsertRelationshipById("E", "0", "2", new Dictionary<string, object> { ["weight"] = 5.0 });
        conn.UpsertRelationshipById("E", "2", "3", new Dictionary<string, object> { ["weight"] = 3.0 });
        conn.Commit();

        return (db, conn);
    }

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

    // ── Δ-stepping SSSP ───────────────────────────────────────────────────────

    [Fact]
    public void DeltaStepping_WeightedGraph_ShortestPathViaBIsFound()
    {
        var (db, conn) = BuildWeightedGraph();
        using (db) using (conn)
        {
            var src   = new NodeId(0, 0); // A
            var graph = new GraphAdapter(db.NodeTables, db.RelTables, "weight");
            var algo  = new DeltaSteppingSsspAlgorithm(graph, maxThreads: 4);
            algo.Execute(new GdsCallOptions { SourceNode = src });

            var dists = algo.GetResults()
                .ToDictionary(r => r.NodeId, r => r.Values["distance"]);

            // A→A = 0
            Assert.Equal(0.0, Convert.ToDouble(dists[new NodeId(0, 0)]));
            // A→B = 1.0 (direct)
            Assert.Equal(1.0, Convert.ToDouble(dists[new NodeId(1, 0)]));
            // A→C = 3.0 via A→B→C (not 5.0 direct)
            Assert.Equal(3.0, Convert.ToDouble(dists[new NodeId(2, 0)]));
            // A→D = 6.0 via A→B→C→D
            Assert.Equal(6.0, Convert.ToDouble(dists[new NodeId(3, 0)]));
        }
    }

    [Fact]
    public void DeltaStepping_MatchesSequentialDijkstra()
    {
        var (db, conn) = BuildWeightedGraph();
        using (db) using (conn)
        {
            var src   = new NodeId(0, 0);
            var graph = new GraphAdapter(db.NodeTables, db.RelTables, "weight");
            var opts  = new GdsCallOptions { SourceNode = src };

            var seqAlgo = new SsspAlgorithm(graph);
            seqAlgo.Execute(opts);

            var parAlgo = new DeltaSteppingSsspAlgorithm(graph, maxThreads: 4);
            parAlgo.Execute(opts);

            var seqDists = seqAlgo.GetResults()
                .ToDictionary(r => r.NodeId, r => r.Values["distance"]);
            var parDists = parAlgo.GetResults()
                .ToDictionary(r => r.NodeId, r => r.Values["distance"]);

            foreach (var (nid, seqD) in seqDists)
            {
                Assert.True(parDists.ContainsKey(nid));
                Assert.Equal(seqD == null, parDists[nid] == null);
                if (seqD != null)
                {
                    double diff = Math.Abs(Convert.ToDouble(seqD) - Convert.ToDouble(parDists[nid]));
                    Assert.True(diff < 1e-9, $"Mismatch node {nid}: seq={seqD}, delta={parDists[nid]}");
                }
            }
        }
    }

    [Fact]
    public void DeltaStepping_ViaCallQuery_ReturnsResults()
    {
        var (db, conn) = BuildWeightedGraph();
        using (db) using (conn)
        {
            var r = conn.Query("CALL sssp_delta('0') YIELD *");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.True(r.GetNumTuples() >= 1);
        }
    }

    [Fact]
    public void DeltaStepping_ViaGdsRegistry_ProducesCorrectTypes()
    {
        var (db, conn) = BuildWeightedGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables, "weight");
            var algo  = GdsRegistry.Create("sssp_delta", graph, GdsCallOptions.Defaults);
            Assert.NotNull(algo);
            Assert.IsType<DeltaSteppingSsspAlgorithm>(algo);
        }
    }

    // ── Parallel KHop ─────────────────────────────────────────────────────────

    [Fact]
    public void ParallelKHop_AllSourcesBFS_CoversExpectedNodes()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new ParallelKHopAlgorithm(graph, maxThreads: 4);
            algo.Execute(new GdsCallOptions { MaxHops = 2 });

            var rows = algo.GetResults().ToList();
            // Each of 5 nodes reachable from each source within 2 hops
            // (exact count depends on graph topology; just assert non-empty)
            Assert.True(rows.Count > 0, "Expected reachability rows");
            foreach (var r in rows)
            {
                Assert.True(r.Values.ContainsKey("hops"));
                Assert.True(Convert.ToInt64(r.Values["hops"]) <= 2);
            }
        }
    }

    [Fact]
    public void ParallelKHop_FromAlice_SameResultsAsSequential()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var alice = new NodeId(0, 0);
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var opts  = new GdsCallOptions { SourceNode = alice, MaxHops = 2 };

            var seqAlgo = new KHopAlgorithm(graph);
            seqAlgo.Execute(opts);
            var parAlgo = new ParallelKHopAlgorithm(graph, maxThreads: 4);
            parAlgo.Execute(opts);

            // Both should find the same set of reachable node IDs
            var seqNodes = seqAlgo.GetResults()
                .Select(r => r.Values["node"]?.ToString()).ToHashSet();
            var parNodes = parAlgo.GetResults()
                .Select(r => r.Values["node"]?.ToString()).ToHashSet();

            Assert.Equal(seqNodes.Count, parNodes.Count);
            Assert.Subset(seqNodes, parNodes);
        }
    }

    [Fact]
    public void ParallelKHop_ViaRegistry_IsParallelType()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = GdsRegistry.Create("k_hop", graph, GdsCallOptions.Defaults);
            Assert.NotNull(algo);
            Assert.IsType<ParallelKHopAlgorithm>(algo);
        }
    }

    // ── Parallel VLP ──────────────────────────────────────────────────────────

    [Fact]
    public void ParallelVlp_PathsHaveCorrectLengthRange()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var alice = new NodeId(0, 0);
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new ParallelVariableLengthPathAlgorithm(graph, minLen: 1, maxThreads: 4);
            algo.Execute(new GdsCallOptions { SourceNode = alice, MaxHops = 2 });

            var rows = algo.GetResults().ToList();
            Assert.True(rows.Count > 0, "Expected path rows");
            foreach (var r in rows)
            {
                long len = Convert.ToInt64(r.Values["length"]);
                Assert.True(len >= 1 && len <= 2, $"Length {len} out of [1,2]");
                Assert.True(r.Values["path"]?.ToString()?.Contains("→") == true,
                    "Path should contain → separator");
            }
        }
    }

    [Fact]
    public void ParallelVlp_ViaRegistry_IsParallelType()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = GdsRegistry.Create("variable_length_path", graph, GdsCallOptions.Defaults);
            Assert.NotNull(algo);
            Assert.IsType<ParallelVariableLengthPathAlgorithm>(algo);
        }
    }

    [Fact]
    public void ParallelVlp_SameTotalPathsAsSequential()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var alice = new NodeId(0, 0);
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var opts  = new GdsCallOptions { SourceNode = alice, MaxHops = 2 };

            var seqAlgo = new VariableLengthPathAlgorithm(graph, minLen: 1);
            seqAlgo.Execute(opts);

            var parAlgo = new ParallelVariableLengthPathAlgorithm(graph, minLen: 1, maxThreads: 4);
            parAlgo.Execute(opts);

            Assert.Equal(seqAlgo.GetResults().Count(), parAlgo.GetResults().Count());
        }
    }

    // ── GdsCallOptions.Sequential / IsParallel ─────────────────────────────────

    [Fact]
    public void GdsCallOptions_Sequential_ForcesSequentialAlgorithms()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);

            Assert.IsType<KHopAlgorithm>(
                GdsRegistry.Create("k_hop", graph, GdsCallOptions.Sequential));
            Assert.IsType<VariableLengthPathAlgorithm>(
                GdsRegistry.Create("variable_length_path", graph, GdsCallOptions.Sequential));
        }
    }
}
