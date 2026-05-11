using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Common;
using BogDb.Core.GraphDataScience;

namespace BogDb.Tests.GraphDataScience;

/// <summary>
/// Integration tests for the GDS streaming pipeline.
/// Covers: IGraph/GraphAdapter, all 5 algorithms, PhysicalGdsCall streaming,
/// CALL query intercept, and GdsRegistry.
/// </summary>
public class GdsStreamingTests
{
    // ── Shared graph fixture ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a 5-node social graph:
    ///   Alice → Bob → Carol
    ///   Alice → Carol
    ///   Dave → Eve
    ///   (Dave and Eve are disconnected from Alice's component)
    /// </summary>
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

        conn.UpsertRelationshipById("KNOWS", "0", "1", new Dictionary<string, object>());  // Alice→Bob
        conn.UpsertRelationshipById("KNOWS", "1", "2", new Dictionary<string, object>());  // Bob→Carol
        conn.UpsertRelationshipById("KNOWS", "0", "2", new Dictionary<string, object>());  // Alice→Carol
        conn.UpsertRelationshipById("KNOWS", "3", "4", new Dictionary<string, object>());  // Dave→Eve
        conn.Commit();

        return (db, conn);
    }

    // ── IGraph / GraphAdapter ────────────────────────────────────────────────

    [Fact]
    public void GraphAdapter_NodeCount_ReportsAllNodes()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            Assert.Equal(5, graph.NodeCount);
        }
    }

    [Fact]
    public void GraphAdapter_AllNodes_EnumeratesCorrectCount()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            Assert.Equal(5, graph.AllNodes().Count());
        }
    }

    [Fact]
    public void GraphAdapter_OutEdges_AliceHasExactlyTwoOutEdges()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            // Alice is offset 0 in table 0, has edges to Bob and Carol
            var alice  = new NodeId(0, 0);
            var outDeg = graph.OutDegree(alice);
            Assert.Equal(2, outDeg);
        }
    }

    [Fact]
    public void GraphAdapter_GetNodeProperties_ReturnsCorrectValues()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var alice = new NodeId(0, 0);
            var props = graph.GetNodeProperties(alice);
            Assert.NotNull(props);
            Assert.True(props!.ContainsKey("name"), "Properties should contain 'name'");
            Assert.Equal("Alice", props["name"]);
        }
    }

    // ── PageRank ─────────────────────────────────────────────────────────────

    [Fact]
    public void PageRank_ProducesNonNegativeScoresForAllNodes()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new PageRankAlgorithm(graph);
            algo.Execute(new GdsCallOptions { MaxIterations = 10 });

            var results = algo.GetResults().ToList();
            Assert.Equal(5, results.Count);
            foreach (var row in results)
            {
                var rank = Convert.ToDouble(row.Values["rank"]);
                Assert.True(rank >= 0, $"PageRank should be non-negative, got {rank}");
            }
        }
    }

    [Fact]
    public void PageRank_HighlyLinkedNode_HasHigherRank()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new PageRankAlgorithm(graph);
            algo.Execute(GdsCallOptions.Defaults);

            var results = algo.GetResults()
                .ToDictionary(r => r.NodeId, r => Convert.ToDouble(r.Values["rank"]));

            // Carol (offset 2) is pointed to by both Alice and Bob — should rank highest
            var carolRank = results[new NodeId(2, 0)];
            var daveRank  = results[new NodeId(3, 0)];
            var eveRank   = results[new NodeId(4, 0)];
            // Carol (2 inbound edges in dense component) MUST outrank
            // Dave (0 inbound edges, only outbound to Eve)
            Assert.True(carolRank > daveRank,
                $"Carol (rank={carolRank:F4}) should outrank Dave (rank={daveRank:F4})");
            // Eve (1 inbound from Dave) should outrank Dave (0 inbound)
            Assert.True(eveRank > daveRank,
                $"Eve (rank={eveRank:F4}) should outrank Dave (rank={daveRank:F4})");
        }
    }

    // ── WCC ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Wcc_TwoComponents_ProducesTwoDistinctLabels()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new WccAlgorithm(graph);
            algo.Execute(GdsCallOptions.Defaults);

            var results  = algo.GetResults().ToList();
            Assert.Equal(5, results.Count);

            var components = results
                .Select(r => Convert.ToInt64(r.Values["component_id"]))
                .Distinct()
                .ToList();

            // Should find exactly 2 components: {Alice,Bob,Carol} and {Dave,Eve}
            Assert.Equal(2, components.Count);
        }
    }

    [Fact]
    public void Wcc_NodesInSameComponent_HaveSameLabel()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new WccAlgorithm(graph);
            algo.Execute(GdsCallOptions.Defaults);

            var compById = algo.GetResults()
                .ToDictionary(r => r.NodeId, r => Convert.ToInt64(r.Values["component_id"]));

            // Alice(0), Bob(1), Carol(2) should be in the same component
            var cAlice = compById[new NodeId(0, 0)];
            var cBob   = compById[new NodeId(1, 0)];
            var cCarol = compById[new NodeId(2, 0)];
            Assert.Equal(cAlice, cBob);
            Assert.Equal(cAlice, cCarol);

            // Dave(3), Eve(4) should be in the same but different component
            var cDave = compById[new NodeId(3, 0)];
            var cEve  = compById[new NodeId(4, 0)];
            Assert.Equal(cDave, cEve);
            Assert.NotEqual(cAlice, cDave);
        }
    }

    // ── SSSP ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Sssp_FromAlice_BobAndCarolReachableIn1And1Hops()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var alice = new NodeId(0, 0);
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new SsspAlgorithm(graph);
            algo.Execute(new GdsCallOptions { SourceNode = alice });

            var dists = algo.GetResults()
                .ToDictionary(r => r.NodeId, r => r.Values["distance"]);

            // Alice distance 0
            Assert.Equal(0.0, Convert.ToDouble(dists[alice]));
            // Bob distance 1 (direct Alice→Bob)
            Assert.Equal(1.0, Convert.ToDouble(dists[new NodeId(1, 0)]));
            // Carol distance 1 (direct Alice→Carol)
            Assert.Equal(1.0, Convert.ToDouble(dists[new NodeId(2, 0)]));
        }
    }

    [Fact]
    public void Sssp_DaveUnreachableFromAlice_DistanceIsNull()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var alice = new NodeId(0, 0);
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new SsspAlgorithm(graph);
            algo.Execute(new GdsCallOptions { SourceNode = alice });

            var daveDist = algo.GetResults()
                .First(r => r.NodeId.Equals(new NodeId(3, 0)))
                .Values["distance"];

            // Dave is in a disconnected component → null distance
            Assert.Null(daveDist);
        }
    }

    // ── KHop ──────────────────────────────────────────────────────────────────

    [Fact]
    public void KHop_FromAliceMaxHops2_FindsBobAndCarol()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var alice = new NodeId(0, 0);
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new KHopAlgorithm(graph);
            algo.Execute(new GdsCallOptions { SourceNode = alice, MaxHops = 2 });

            var results = algo.GetResults().ToList();
            // KHop stores target node IDs in the "node" value (not the result row key)
            var reachedNodes = results.Select(r => r.Values["node"]?.ToString()).ToHashSet();
            // Bob and Carol are within 1 hop from Alice
            Assert.Contains(new NodeId(1, 0).ToString(), reachedNodes); // Bob
            Assert.Contains(new NodeId(2, 0).ToString(), reachedNodes); // Carol
            // Dave and Eve are disconnected — should NOT be reachable
            Assert.DoesNotContain(new NodeId(3, 0).ToString(), reachedNodes); // Dave
            Assert.DoesNotContain(new NodeId(4, 0).ToString(), reachedNodes); // Eve
            Assert.Equal(2, results.Count); // Exactly Bob and Carol
            foreach (var r in results)
                Assert.True(Convert.ToInt64(r.Values["hops"]) <= 2);
        }
    }

    // ── VariableLengthPath ────────────────────────────────────────────────────

    [Fact]
    public void VLP_FromAliceMaxLen2_ProducesMultiplePaths()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var alice = new NodeId(0, 0);
            var graph = new GraphAdapter(db.NodeTables, db.RelTables);
            var algo  = new VariableLengthPathAlgorithm(graph, minLen: 1);
            algo.Execute(new GdsCallOptions { SourceNode = alice, MaxHops = 2 });

            var results = algo.GetResults().ToList();
            Assert.True(results.Count >= 2, $"Expected ≥2 paths, got {results.Count}");
            foreach (var r in results)
            {
                Assert.True(Convert.ToInt64(r.Values["length"]) >= 1);
                Assert.NotEmpty(r.Values["path"]?.ToString() ?? "");
            }
        }
    }

    // ── PhysicalGdsCall streaming ─────────────────────────────────────────────

    [Fact]
    public void PhysicalGdsCall_PageRank_StreamsAllRows()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var op   = new PhysicalGdsCall(db, "pagerank");
            var rows = new List<Dictionary<string, object?>>();
            while (op.Next(out var row)) rows.Add(row);

            Assert.Equal(5, rows.Count);
            foreach (var r in rows)
            {
                Assert.True(r.ContainsKey("rank"), "Row should have 'rank' key");
                Assert.NotNull(r["rank"]);
            }
        }
    }

    [Fact]
    public void PhysicalGdsCall_Wcc_CachesScalarResult()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var op = new PhysicalGdsCall(db, "wcc");
            while (op.Next(out _)) { }  // exhaust stream

            // After running, scalar cache should have a value for node 0:0
            var v = PhysicalGdsCall.GetLastScalar("wcc", new NodeId(0, 0));
            Assert.NotNull(v);
        }
    }

    [Fact]
    public void PhysicalGdsCall_UnknownAlgorithm_ThrowsOrReturnsError()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var op = new PhysicalGdsCall(db, "nonexistent_algo_xyz");
            Assert.Throws<InvalidOperationException>(() => op.Next(out _));
        }
    }

    // ── DbConnection extensions ───────────────────────────────────────────────

    [Fact]
    public void RunGds_PageRank_ReturnsFiveRows()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var rows = conn.RunGds("pagerank");
            Assert.Equal(5, rows.Count);
        }
    }

    [Fact]
    public void RunGds_Wcc_AllRowsHaveComponentId()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var rows = conn.RunGds("wcc");
            Assert.Equal(5, rows.Count);
            foreach (var row in rows)
                Assert.True(row.ContainsKey("component_id"),
                    "Each WCC row should contain 'component_id'");
        }
    }

    // ── CALL query intercept ─────────────────────────────────────────────────

    [Fact]
    public void CallQuery_PageRank_ReturnsSuccessResult()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var r = conn.Query("CALL pagerank() YIELD *");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.True(r.GetNumTuples() >= 1,
                $"Expected ≥1 tuples from CALL pagerank(), got {r.GetNumTuples()}");
        }
    }

    [Fact]
    public void CallQuery_Wcc_ReturnsCorrectTupleCount()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var r = conn.Query("CALL wcc() YIELD *");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void CallQuery_Sssp_WithSourceNode_ReturnsAllNodes()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            // Source node = offset 0, table 0 → Alice
            var r = conn.Query("CALL sssp('0') YIELD *");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            // SSSP returns a distance row for every node in the graph
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void CallQuery_KHop_ReturnsReachableNodes()
    {
        var (db, conn) = BuildSocialGraph();
        using (db) using (conn)
        {
            var r = conn.Query("CALL k_hop('0', 2) YIELD *");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            // Alice can reach Bob and Carol within 2 hops
            Assert.Equal(2UL, r.GetNumTuples());
        }
    }

    // ── GdsRegistry ──────────────────────────────────────────────────────────

    [Fact]
    public void GdsRegistry_IsGdsFunction_KnownAlgorithms_ReturnTrue()
    {
        var known = new[] { "pagerank", "wcc", "sssp", "k_hop", "variable_length_path" };
        foreach (var name in known)
            Assert.True(GdsRegistry.IsGdsFunction(name), $"'{name}' should be a GDS function");
    }

    [Fact]
    public void GdsRegistry_IsGdsFunction_Unknown_ReturnsFalse()
    {
        Assert.False(GdsRegistry.IsGdsFunction("not_a_gds_function_xyz"));
    }

    [Fact]
    public void GdsRegistry_Aliases_Resolve()
    {
        var graph = new GraphAdapter(
            new Dictionary<string, NodeTableData>(),
            new Dictionary<string, RelTableData>());
        // both "wcc" and "weakly_connected_components" should create WccAlgorithm
        Assert.NotNull(GdsRegistry.Create("wcc", graph));
        Assert.NotNull(GdsRegistry.Create("weakly_connected_components", graph));
        Assert.NotNull(GdsRegistry.Create("sssp", graph));
        Assert.NotNull(GdsRegistry.Create("single_source_shortest_paths", graph));
    }
}
