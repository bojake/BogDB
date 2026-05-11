using BogDb.Core.Common;
using BogDb.Core.Extraction;
using BogDb.Core.Main;
using Xunit;
using System.IO;

namespace BogDb.Tests.Extraction;

public sealed class GraphShardExtractorTests
{
    [Fact]
    public void ExtractNeighborhood_SingleSeedDepthOne_GroupsNodesAndEdges()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.Equal(3, shard.Stats.NodeCount);
        Assert.Equal(2, shard.Stats.EdgeCount);
        Assert.True(shard.NodeTables.ContainsKey("Person"));
        Assert.True(shard.NodeTables.ContainsKey("City"));
        Assert.True(shard.RelTables.ContainsKey("KNOWS"));
        Assert.True(shard.RelTables.ContainsKey("LIVES_IN"));
        Assert.Single(shard.RelTables["KNOWS"].Rows);
        Assert.Single(shard.RelTables["LIVES_IN"].Rows);
    }

    [Fact]
    public void ExtractNeighborhood_DepthTwo_ReachesMultiHopNeighbor()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 2,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.Contains(shard.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:carol");
    }

    [Fact]
    public void ExtractNeighborhood_RelTypeFilter_PreservesOnlyAllowedEdges()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                RelTypes = ["KNOWS"]
            });

        Assert.Single(shard.RelTables);
        Assert.True(shard.RelTables.ContainsKey("KNOWS"));
        Assert.DoesNotContain("LIVES_IN", shard.RelTables.Keys);
    }

    [Fact]
    public void ExtractNeighborhood_NodeTableFilter_ExcludesDisallowedTables()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                NodeTables = ["Person"]
            });

        Assert.Single(shard.NodeTables);
        Assert.DoesNotContain("City", shard.NodeTables.Keys);
        Assert.DoesNotContain("LIVES_IN", shard.RelTables.Keys);
    }

    [Fact]
    public void ExtractNeighborhood_NodeLimit_RecordsBoundaryMetadata()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 2,
                MaxNodes = 2,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.Equal(2, shard.Stats.NodeCount);
        Assert.True(shard.Boundary.HasNodeBoundary);
        Assert.NotEmpty(shard.Boundary.BoundaryNodeIds);
    }

    [Fact]
    public void ExtractNodeSet_IncludesOnlySelectedNodesAndConnectingEdges()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNodeSet(
            [
                new GraphNodeSelector { TableName = "Person", NodeId = "alice" },
                new GraphNodeSelector { TableName = "Person", NodeId = "bob" }
            ],
            new GraphShardExtractionOptions
            {
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.Single(shard.NodeTables);
        Assert.Equal(2, shard.NodeTables["Person"].Rows.Count);
        Assert.Single(shard.RelTables["KNOWS"].Rows);
        Assert.DoesNotContain(shard.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:carol");
    }

    [Fact]
    public void ExtractNodeSet_IncludeIncoming_IncludesIncomingEdgesBetweenSelectedNodes()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNodeSet(
            [
                new GraphNodeSelector { TableName = "Person", NodeId = "alice" },
                new GraphNodeSelector { TableName = "Person", NodeId = "bob" }
            ],
            new GraphShardExtractionOptions
            {
                IncludeOutgoing = false,
                IncludeIncoming = true
            });

        Assert.Single(shard.RelTables);
        Assert.True(shard.RelTables.ContainsKey("KNOWS"));
        Assert.Single(shard.RelTables["KNOWS"].Rows);
        var row = shard.RelTables["KNOWS"].Rows[0];
        Assert.Equal("node:Person:alice", row.SourceNodeId);
        Assert.Equal("node:Person:bob", row.TargetNodeId);
    }

    [Fact]
    public void ExtractNodeSet_BothDirections_DoesNotDuplicateConnectingEdge()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNodeSet(
            [
                new GraphNodeSelector { TableName = "Person", NodeId = "alice" },
                new GraphNodeSelector { TableName = "Person", NodeId = "bob" }
            ],
            new GraphShardExtractionOptions
            {
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        Assert.Single(shard.RelTables);
        Assert.True(shard.RelTables.ContainsKey("KNOWS"));
        Assert.Single(shard.RelTables["KNOWS"].Rows);
    }

    [Fact]
    public void ExtractNeighborhood_SameTableIncomingAndOutgoing_PreservesDistinctEndpoints()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood(
            "Person",
            "bob",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = true
            });

        Assert.True(shard.RelTables.ContainsKey("KNOWS"));
        Assert.Contains(shard.RelTables["KNOWS"].Rows, row =>
            row.SourceNodeId == "node:Person:alice" &&
            row.TargetNodeId == "node:Person:bob");
        Assert.Contains(shard.RelTables["KNOWS"].Rows, row =>
            row.SourceNodeId == "node:Person:bob" &&
            row.TargetNodeId == "node:Person:carol");
        Assert.Equal(["node:Person:carol"], shard.Adjacency.Outgoing["node:Person:bob"].Select(edge => edge.NeighborNodeId));
        Assert.Equal(["node:Person:alice"], shard.Adjacency.Incoming["node:Person:bob"].Select(edge => edge.NeighborNodeId));
    }

    [Fact]
    public void ExtractNeighborhood_RelIds_AreStableAcrossRepeatedExtraction()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var first = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });
        var second = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        var firstIds = first.RelTables.Values.SelectMany(t => t.Rows).Select(r => r.RelId).OrderBy(x => x).ToList();
        var secondIds = second.RelTables.Values.SelectMany(t => t.Rows).Select(r => r.RelId).OrderBy(x => x).ToList();

        Assert.Equal(firstIds, secondIds);
        Assert.All(firstIds, id => Assert.StartsWith("rel:", id));
    }

    [Fact]
    public void ExtractNeighborhood_UsesDeclaredRelEndpoints_WhenIdsOverlapAcrossTables()
    {
        using var db = ExtractionTestGraphFactory.CreateAmbiguousEndpointDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood(
            "Person",
            "shared-id",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.True(shard.NodeTables.ContainsKey("Person"));
        Assert.True(shard.NodeTables.ContainsKey("City"));
        Assert.Single(shard.RelTables);
        Assert.Equal("Person", shard.RelTables["LIVES_IN"].FromTable);
        Assert.Equal("City", shard.RelTables["LIVES_IN"].ToTable);
        Assert.Contains(shard.NodeTables["City"].Rows, row => row.ExternalId == "node:City:shared-id");
    }

    [Fact]
    public void ExtractNeighborhood_PreservedEndpointMetadata_AfterDatabaseReopen()
    {
        var path = Path.Combine(Path.GetTempPath(), "bogdb-ng-extraction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        try
        {
            using (var db = BogDatabase.Open(path))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID> { ["name"] = LogicalTypeID.STRING });
                conn.EnsureNodeTable("City", new Dictionary<string, LogicalTypeID> { ["name"] = LogicalTypeID.STRING });
                conn.EnsureRelTable("LIVES_IN", "Person", "City", new Dictionary<string, LogicalTypeID> { ["since"] = LogicalTypeID.INT64 });
                conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["name"] = "Alice" });
                conn.UpsertNode("City", "seattle", new Dictionary<string, object> { ["name"] = "Seattle" });
                conn.UpsertRelationship("LIVES_IN", "alice", "seattle", new Dictionary<string, object> { ["since"] = 2019L });
                conn.Commit();
            }

            using var reopened = BogDatabase.Open(path);
            var extractor = new GraphShardExtractor(reopened);
            var shard = extractor.ExtractNeighborhood(
                "Person",
                "alice",
                new GraphShardExtractionOptions
                {
                    MaxDepth = 1,
                    IncludeOutgoing = true,
                    IncludeIncoming = false
                });

            Assert.Equal("Person", shard.RelTables["LIVES_IN"].FromTable);
            Assert.Equal("City", shard.RelTables["LIVES_IN"].ToTable);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
