using BogDb.Core.Extraction;
using Xunit;

namespace BogDb.Tests.Extraction;

public sealed class GraphShardRuntimeViewTests
{
    [Fact]
    public void RuntimeView_Load_ProvidesNodeAndRelLookup()
    {
        var shard = GraphShardJson.Deserialize(GraphShardJson.Serialize(CreateSampleShard()));

        var view = GraphShardRuntimeView.Load(shard);

        Assert.True(view.HasNode("node:Person:alice"));
        Assert.True(view.TryGetNode("node:Person:alice", out var tableName, out var nodeRow));
        Assert.Equal("Person", tableName);
        Assert.Equal("Alice", nodeRow.Properties["name"]?.ToString());

        Assert.True(view.HasRelationship("rel:KNOWS:1"));
        Assert.True(view.TryGetRelationship("rel:KNOWS:1", out var relType, out var relRow));
        Assert.Equal("KNOWS", relType);
        Assert.Equal("node:Person:bob", relRow.TargetNodeId);
    }

    [Fact]
    public void RuntimeView_Expand_UsesAdjacencyAndRelTypeFilter()
    {
        var view = GraphShardRuntimeView.Load(CreateSampleShard());

        var outgoing = view.Expand("node:Person:alice", includeOutgoing: true, includeIncoming: false);
        var knowsOnly = view.Expand("node:Person:alice", includeOutgoing: true, includeIncoming: false, relType: "KNOWS");

        Assert.Equal(["node:City:seattle", "node:Person:bob"], outgoing);
        Assert.Equal(["node:Person:bob"], knowsOnly);
    }

    private static GraphShard CreateSampleShard()
        => new()
        {
            GraphVersionToken = "graph-v1",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice", Properties = new Dictionary<string, object?> { ["name"] = "Alice" } },
                        new NodeShardRow { ExternalId = "node:Person:bob", Properties = new Dictionary<string, object?> { ["name"] = "Bob" } }
                    ]
                },
                ["City"] = new()
                {
                    TableName = "City",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:City:seattle", Properties = new Dictionary<string, object?> { ["name"] = "Seattle" } }
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
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:KNOWS:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:Person:bob" }
                    ]
                },
                ["LIVES_IN"] = new()
                {
                    RelType = "LIVES_IN",
                    FromTable = "Person",
                    ToTable = "City",
                    Rows =
                    [
                        new RelShardRow { RelId = "rel:LIVES_IN:1", SourceNodeId = "node:Person:alice", TargetNodeId = "node:City:seattle" }
                    ]
                }
            },
            Adjacency = new GraphShardAdjacency
            {
                Outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:alice"] =
                    [
                        new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:bob", Direction = "out" },
                        new ShardEdgeRef { RelId = "rel:LIVES_IN:1", RelType = "LIVES_IN", NeighborNodeId = "node:City:seattle", Direction = "out" }
                    ]
                },
                Incoming = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:bob"] =
                    [
                        new ShardEdgeRef { RelId = "rel:KNOWS:1", RelType = "KNOWS", NeighborNodeId = "node:Person:alice", Direction = "in" }
                    ],
                    ["node:City:seattle"] =
                    [
                        new ShardEdgeRef { RelId = "rel:LIVES_IN:1", RelType = "LIVES_IN", NeighborNodeId = "node:Person:alice", Direction = "in" }
                    ]
                }
            },
            Stats = new GraphShardStats
            {
                NodeCount = 3,
                EdgeCount = 2,
                NodeTableCount = 2,
                RelTableCount = 2
            }
        };
}
