using BogDb.Core.Extraction;
using Xunit;

namespace BogDb.Tests.Extraction;

public sealed class GraphShardMergerTests
{
    [Fact]
    public void Merge_CombinesNodeRelAdjacencyAndBoundaryState()
    {
        var left = new GraphShard
        {
            GraphVersionToken = "graph-v1",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows = [new NodeShardRow { ExternalId = "node:Person:alice", Properties = new Dictionary<string, object?> { ["name"] = "Alice" } }]
                }
            },
            Boundary = new GraphShardBoundary
            {
                IsTruncated = true,
                TruncationReasons = ["node_limit"],
                BoundaryNodeIds = ["node:Person:bob"],
                FetchHints = new GraphShardBoundaryFetchHints
                {
                    ShouldFetchMore = true,
                    CanResumeFromBoundary = true,
                    RecommendedSeedNodeIds = ["node:Person:bob"],
                    SuggestedMaxNodes = 10,
                    Reasons = ["node_limit"]
                }
            },
            SeedProvenance = new GraphShardSeedProvenance
            {
                RequestedCount = 1,
                IncludedCount = 1,
                RequestedSeeds = [new GraphShardSeedRecord { TableName = "Person", RequestedNodeId = "alice", ExternalId = "node:Person:alice", Status = "included" }]
            },
            Stats = new GraphShardStats { NodeCount = 1, EdgeCount = 0, NodeTableCount = 1, RelTableCount = 0, BoundaryNodeCount = 1 }
        };

        var right = new GraphShard
        {
            GraphVersionToken = "graph-v2",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows = [new NodeShardRow { ExternalId = "node:Person:bob", Properties = new Dictionary<string, object?> { ["name"] = "Bob" } }]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows = [new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }]
                }
            },
            Adjacency = new GraphShardAdjacency
            {
                Outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:alice"] = [new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:bob", Direction = "out" }]
                },
                Incoming = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:bob"] = [new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:alice", Direction = "in" }]
                }
            },
            SeedProvenance = new GraphShardSeedProvenance
            {
                RequestedCount = 1,
                IncludedCount = 1,
                RequestedSeeds = [new GraphShardSeedRecord { TableName = "Person", RequestedNodeId = "bob", ExternalId = "node:Person:bob", Status = "included" }]
            },
            Stats = new GraphShardStats { NodeCount = 1, EdgeCount = 1, NodeTableCount = 1, RelTableCount = 1 }
        };

        var merged = GraphShardMerger.Merge(left, right);
        var view = GraphShardRuntimeView.Load(merged);

        Assert.Equal("merged:graph-v1|graph-v2", merged.GraphVersionToken);
        Assert.Equal(2, merged.Stats.NodeCount);
        Assert.Equal(1, merged.Stats.EdgeCount);
        Assert.True(merged.Boundary.IsTruncated);
        Assert.Single(merged.Boundary.BoundaryNodeIds);
        Assert.Equal(2, merged.SeedProvenance.RequestedCount);
        Assert.True(view.HasNode("node:Person:alice"));
        Assert.True(view.HasNode("node:Person:bob"));
        Assert.Equal(["node:Person:bob"], view.Expand("node:Person:alice", includeOutgoing: true));
    }

    [Fact]
    public void Merge_DuplicateNode_OverlaysPropertiesAndDeduplicatesAdjacency()
    {
        var first = new GraphShard
        {
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows = [new NodeShardRow { ExternalId = "node:Person:alice", Properties = new Dictionary<string, object?> { ["name"] = "Alice" } }]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows = [new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }]
                }
            },
            Adjacency = new GraphShardAdjacency
            {
                Outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:alice"] = [new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:bob", Direction = "out" }]
                }
            },
            Stats = new GraphShardStats { NodeCount = 1, EdgeCount = 1, NodeTableCount = 1, RelTableCount = 1 }
        };

        var second = new GraphShard
        {
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice", Properties = new Dictionary<string, object?> { ["age"] = 30 } },
                        new NodeShardRow { ExternalId = "node:Person:bob", Properties = new Dictionary<string, object?> { ["name"] = "Bob" } }
                    ]
                }
            },
            RelTables = new Dictionary<string, RelShardTable>(StringComparer.Ordinal)
            {
                ["KNOWS"] = new()
                {
                    RelType = "KNOWS",
                    FromTable = "Person",
                    ToTable = "Person",
                    Rows = [new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }]
                }
            },
            Adjacency = new GraphShardAdjacency
            {
                Outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:alice"] = [new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:bob", Direction = "out" }]
                }
            },
            Stats = new GraphShardStats { NodeCount = 2, EdgeCount = 1, NodeTableCount = 1, RelTableCount = 1 }
        };

        var merged = GraphShardMerger.Merge(first, second);

        var alice = Assert.Single(merged.NodeTables["Person"].Rows.Where(row => row.ExternalId == "node:Person:alice"));
        Assert.Equal("Alice", alice.Properties["name"]);
        Assert.Equal(30, alice.Properties["age"]);
        Assert.Single(merged.Adjacency.Outgoing["node:Person:alice"]);
    }
}
