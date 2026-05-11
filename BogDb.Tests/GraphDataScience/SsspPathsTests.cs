using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.Main;
using BogDb.Core.GraphDataScience;

namespace BogDb.Tests.GraphDataScience
{
    public class SsspPathsTests
    {
        // ── Helper: builds a graph using the same proven pattern as GdsStreamingTests ──

        /// <summary>
        /// Linear graph: A → B → C → D
        /// </summary>
        private static (BogDatabase db, BogConnection conn) BuildLinearGraph()
        {
            var db   = BogDatabase.Open(":memory:");
            var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Node", new Dictionary<string, LogicalTypeID>
            {
                ["id"]   = LogicalTypeID.INT64,
                ["name"] = LogicalTypeID.STRING,
            });
            conn.EnsureRelTable("Edge", "Node", "Node", new Dictionary<string, LogicalTypeID>());

            conn.UpsertNodeById("Node", "0", new Dictionary<string, object> { ["id"]=0L, ["name"]="A" });
            conn.UpsertNodeById("Node", "1", new Dictionary<string, object> { ["id"]=1L, ["name"]="B" });
            conn.UpsertNodeById("Node", "2", new Dictionary<string, object> { ["id"]=2L, ["name"]="C" });
            conn.UpsertNodeById("Node", "3", new Dictionary<string, object> { ["id"]=3L, ["name"]="D" });

            conn.UpsertRelationshipById("Edge", "0", "1", new Dictionary<string, object>()); // A→B
            conn.UpsertRelationshipById("Edge", "1", "2", new Dictionary<string, object>()); // B→C
            conn.UpsertRelationshipById("Edge", "2", "3", new Dictionary<string, object>()); // C→D
            conn.Commit();

            return (db, conn);
        }

        /// <summary>
        /// Diamond graph: A → B → D, A → C → D
        /// </summary>
        private static (BogDatabase db, BogConnection conn) BuildDiamondGraph()
        {
            var db   = BogDatabase.Open(":memory:");
            var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Node", new Dictionary<string, LogicalTypeID>
            {
                ["id"]   = LogicalTypeID.INT64,
                ["name"] = LogicalTypeID.STRING,
            });
            conn.EnsureRelTable("Edge", "Node", "Node", new Dictionary<string, LogicalTypeID>());

            conn.UpsertNodeById("Node", "0", new Dictionary<string, object> { ["id"]=0L, ["name"]="A" });
            conn.UpsertNodeById("Node", "1", new Dictionary<string, object> { ["id"]=1L, ["name"]="B" });
            conn.UpsertNodeById("Node", "2", new Dictionary<string, object> { ["id"]=2L, ["name"]="C" });
            conn.UpsertNodeById("Node", "3", new Dictionary<string, object> { ["id"]=3L, ["name"]="D" });

            conn.UpsertRelationshipById("Edge", "0", "1", new Dictionary<string, object>()); // A→B
            conn.UpsertRelationshipById("Edge", "0", "2", new Dictionary<string, object>()); // A→C
            conn.UpsertRelationshipById("Edge", "1", "3", new Dictionary<string, object>()); // B→D
            conn.UpsertRelationshipById("Edge", "2", "3", new Dictionary<string, object>()); // C→D
            conn.Commit();

            return (db, conn);
        }

        // ── Tests ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Linear graph: A → B → C → D
        /// Path from A to D should be [0:0, 0:1, 0:2, 0:3] with distance 3.
        /// </summary>
        [Fact]
        public void SsspPaths_LinearGraph_ReturnsFullPath()
        {
            var (db, conn) = BuildLinearGraph();
            using (db) using (conn)
            {
                var algo = GdsRegistry.CreateFromDb("sssp_paths", db,
                    new GdsCallOptions { MaxDegreeOfParallelism = 1, SourceNode = new NodeId(0, 0) });

                Assert.NotNull(algo);
                algo!.Execute(new GdsCallOptions { MaxDegreeOfParallelism = 1, SourceNode = new NodeId(0, 0) });

                var rows = algo.GetResults().ToDictionary(r => r.NodeId);
                Assert.Equal(4, rows.Count);

                // Source: distance 0, path = [0:0]
                Assert.Equal(0.0, rows[new NodeId(0, 0)].Values["distance"]);
                var srcPath = AsList(rows[new NodeId(0, 0)].Values["path"]);
                Assert.Single(srcPath);

                // D (offset 3): distance 3, path = [0:0, 0:1, 0:2, 0:3]
                Assert.Equal(3.0, rows[new NodeId(3, 0)].Values["distance"]);
                var dPath = AsList(rows[new NodeId(3, 0)].Values["path"]);
                Assert.Equal(4, dPath.Count);
                Assert.Equal(3L, rows[new NodeId(3, 0)].Values["length"]);

                // B (offset 1): distance 1
                Assert.Equal(1.0, rows[new NodeId(1, 0)].Values["distance"]);
            }
        }

        /// <summary>
        /// Diamond: A → B → D, A → C → D. D has distance 2 either way.
        /// </summary>
        [Fact]
        public void SsspPaths_DiamondGraph_ReturnsShortestPath()
        {
            var (db, conn) = BuildDiamondGraph();
            using (db) using (conn)
            {
                var algo = GdsRegistry.CreateFromDb("sssp_paths", db,
                    new GdsCallOptions { MaxDegreeOfParallelism = 1, SourceNode = new NodeId(0, 0) });

                Assert.NotNull(algo);
                algo!.Execute(new GdsCallOptions { MaxDegreeOfParallelism = 1, SourceNode = new NodeId(0, 0) });

                var rows = algo.GetResults().ToDictionary(r => r.NodeId);

                // D (offset 3): distance 2, path has 3 nodes (A → ? → D)
                var dId = new NodeId(3, 0);
                Assert.Equal(2.0, rows[dId].Values["distance"]);
                var dPath = AsList(rows[dId].Values["path"]);
                Assert.Equal(3, dPath.Count);
                Assert.Equal(2L, rows[dId].Values["length"]);

                // Path starts from source
                Assert.Equal("0:0", dPath[0]?.ToString());
                // Path ends at destination
                Assert.Equal("0:3", dPath[2]?.ToString());
            }
        }

        /// <summary>Unreachable nodes produce null path/distance.</summary>
        [Fact]
        public void SsspPaths_UnreachableNode_ReturnsNullPath()
        {
            var db   = BogDatabase.Open(":memory:");
            var conn = new BogConnection(db);
            using (db) using (conn)
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Node", new Dictionary<string, LogicalTypeID>
                {
                    ["id"]   = LogicalTypeID.INT64,
                    ["name"] = LogicalTypeID.STRING,
                });
                conn.EnsureRelTable("Edge", "Node", "Node", new Dictionary<string, LogicalTypeID>());
                conn.UpsertNodeById("Node", "0", new Dictionary<string, object> { ["id"]=0L, ["name"]="A" });
                conn.UpsertNodeById("Node", "1", new Dictionary<string, object> { ["id"]=1L, ["name"]="B" });
                // No edges
                conn.Commit();

                var algo = GdsRegistry.CreateFromDb("sssp_paths", db,
                    new GdsCallOptions { MaxDegreeOfParallelism = 1, SourceNode = new NodeId(0, 0) });

                Assert.NotNull(algo);
                algo!.Execute(new GdsCallOptions { MaxDegreeOfParallelism = 1, SourceNode = new NodeId(0, 0) });

                var rows = algo.GetResults().ToDictionary(r => r.NodeId);
                var bId = new NodeId(1, 0);
                Assert.Null(rows[bId].Values["distance"]);
                Assert.Null(rows[bId].Values["path"]);
                Assert.Null(rows[bId].Values["length"]);
            }
        }

        /// <summary>All aliases resolve correctly.</summary>
        [Fact]
        public void SsspPaths_AllAliases_AreRegistered()
        {
            Assert.True(GdsRegistry.IsGdsFunction("sssp_paths"));
            Assert.True(GdsRegistry.IsGdsFunction("shortest_paths"));
            Assert.True(GdsRegistry.IsGdsFunction("single_sp_paths"));
        }

        /// <summary>
        /// Weighted graph: X→Z direct weight 10, X→Y→Z weight 3+4=7.
        /// Dijkstra should pick the cheaper path through Y.
        /// </summary>
        [Fact]
        public void SsspPaths_WeightedGraph_ReturnsWeightedShortestPath()
        {
            var db   = BogDatabase.Open(":memory:");
            var conn = new BogConnection(db);
            using (db) using (conn)
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("City", new Dictionary<string, LogicalTypeID>
                {
                    ["id"]   = LogicalTypeID.INT64,
                    ["name"] = LogicalTypeID.STRING,
                });
                conn.EnsureRelTable("Road", "City", "City", new Dictionary<string, LogicalTypeID>
                {
                    ["distance"] = LogicalTypeID.DOUBLE,
                });

                conn.UpsertNodeById("City", "0", new Dictionary<string, object> { ["id"]=0L, ["name"]="X" });
                conn.UpsertNodeById("City", "1", new Dictionary<string, object> { ["id"]=1L, ["name"]="Y" });
                conn.UpsertNodeById("City", "2", new Dictionary<string, object> { ["id"]=2L, ["name"]="Z" });

                conn.UpsertRelationshipById("Road", "0", "2", new Dictionary<string, object> { ["distance"]=10.0 }); // X→Z direct
                conn.UpsertRelationshipById("Road", "0", "1", new Dictionary<string, object> { ["distance"]=3.0 });  // X→Y
                conn.UpsertRelationshipById("Road", "1", "2", new Dictionary<string, object> { ["distance"]=4.0 });  // Y→Z
                conn.Commit();

                var algo = GdsRegistry.CreateFromDb("sssp_paths", db,
                    new GdsCallOptions { MaxDegreeOfParallelism = 1, SourceNode = new NodeId(0, 0) },
                    weightProperty: "distance");

                Assert.NotNull(algo);
                algo!.Execute(new GdsCallOptions { MaxDegreeOfParallelism = 1, SourceNode = new NodeId(0, 0) });

                var rows = algo.GetResults().ToDictionary(r => r.NodeId);

                // Z should have distance 7.0 (via Y), not 10.0 (direct)
                var zId = new NodeId(2, 0);
                Assert.Equal(7.0, rows[zId].Values["distance"]);
                var zPath = AsList(rows[zId].Values["path"]);
                Assert.Equal(3, zPath.Count); // X → Y → Z
                Assert.Equal("0:0", zPath[0]?.ToString()); // starts at X
                Assert.Equal("0:1", zPath[1]?.ToString()); // through Y
                Assert.Equal("0:2", zPath[2]?.ToString()); // ends at Z
                Assert.Equal(2L, rows[zId].Values["length"]);
            }
        }

        // ── Utility ────────────────────────────────────────────────────────────────

        private static List<object?> AsList(object? value)
        {
            Assert.NotNull(value);
            return Assert.IsAssignableFrom<IEnumerable>(value).Cast<object?>().ToList();
        }
    }
}
