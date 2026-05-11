using BogDb.Core.Extraction;
using Xunit;

namespace BogDb.Tests.Extraction;

public sealed class GraphShardDtoTests
{
    [Fact]
    public void GraphShard_JsonRoundTrip_RetainsShapeAndCounts()
    {
        var shard = CreateSampleShard();

        var json = GraphShardJson.Serialize(shard);
        var roundTripped = GraphShardJson.Deserialize(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(GraphShard.CurrentFormatVersion, roundTripped!.FormatVersion);
        Assert.Equal("graph-v1", roundTripped.GraphVersionToken);
        Assert.Single(roundTripped.NodeTables);
        Assert.Single(roundTripped.RelTables);
        Assert.Equal(1, roundTripped.SeedProvenance.RequestedCount);
        Assert.Equal(1, roundTripped.SeedProvenance.IncludedCount);
        Assert.Single(roundTripped.SeedProvenance.RequestedSeeds);
        Assert.Equal(2, roundTripped.NodeTables["Person"].Rows.Count);
        Assert.Single(roundTripped.RelTables["KNOWS"].Rows);
        Assert.True(roundTripped.Adjacency.Outgoing.ContainsKey("node:Person:alice"));
        Assert.Single(roundTripped.Adjacency.Outgoing["node:Person:alice"]);
        Assert.True(roundTripped.Boundary.HasNodeBoundary);
        Assert.True(roundTripped.Boundary.TruncatedByDepth);
        Assert.True(roundTripped.Boundary.FetchHints.ShouldFetchMore);
        Assert.True(roundTripped.Boundary.FetchHints.CanResumeFromBoundary);
        Assert.Single(roundTripped.Boundary.FetchHints.RecommendedSeedNodeIds);
        Assert.Contains("depth_limit", roundTripped.Boundary.FetchHints.Reasons);
        Assert.Equal(2, roundTripped.Stats.NodeCount);
        Assert.Equal(1, roundTripped.Stats.EdgeCount);
    }

    [Fact]
    public void GraphShard_Defaults_AreStableAndDeterministic()
    {
        var shard = new GraphShard();

        Assert.Equal(GraphShard.CurrentFormatVersion, shard.FormatVersion);
        Assert.Empty(shard.NodeTables);
        Assert.Empty(shard.RelTables);
        Assert.Empty(shard.Adjacency.Outgoing);
        Assert.Empty(shard.Adjacency.Incoming);
        Assert.Empty(shard.SeedProvenance.RequestedSeeds);
        Assert.Empty(shard.Boundary.BoundaryNodeIds);
        Assert.Empty(shard.Boundary.FetchHints.RecommendedSeedNodeIds);
        Assert.Empty(shard.Options.RelTypes);
        Assert.Empty(shard.Options.NodeTables);
        Assert.Empty(shard.Metadata);
        Assert.True(shard.Options.IncludeOutgoing);
        Assert.True(shard.Options.IncludeIncoming);
    }

    [Fact]
    public void GraphShard_Metadata_SerializesNullsAndScalarsCorrectly()
    {
        var shard = new GraphShard
        {
            Metadata = new Dictionary<string, object?>
            {
                ["etag"] = "abc123",
                ["priority"] = 7,
                ["complete"] = false,
                ["note"] = null
            }
        };

        var json = GraphShardJson.Serialize(shard);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var metadata = document.RootElement.GetProperty("Metadata");

        Assert.Equal("abc123", metadata.GetProperty("etag").GetString());
        Assert.Equal(7, metadata.GetProperty("priority").GetInt32());
        Assert.False(metadata.GetProperty("complete").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, metadata.GetProperty("note").ValueKind);
    }

    [Fact]
    public void GraphShardJson_Serialize_IsDeterministicForSameObject()
    {
        var shard = CreateSampleShard();

        var first = GraphShardJson.Serialize(shard);
        var second = GraphShardJson.Serialize(shard);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GraphShardJson_Serialize_NormalizesOrderingAndDerivedCounts()
    {
        var shard = new GraphShard
        {
            GraphVersionToken = "graph-v1",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    PropertyColumns = ["age", "name", "age"],
                    Rows =
                    [
                        new NodeShardRow
                        {
                            ExternalId = "node:Person:bob",
                            Properties = new Dictionary<string, object?> { ["name"] = "Bob", ["age"] = 28 }
                        },
                        new NodeShardRow
                        {
                            ExternalId = "node:Person:alice",
                            Properties = new Dictionary<string, object?> { ["name"] = "Alice", ["age"] = 30 }
                        }
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
                    PropertyColumns = ["since", "since"],
                    Rows =
                    [
                        new RelShardRow
                        {
                            RelId = "rel:KNOWS:1",
                            SourceNodeId = "node:Person:alice",
                            TargetNodeId = "node:Person:bob",
                            Properties = new Dictionary<string, object?> { ["since"] = 2020 }
                        }
                    ]
                }
            },
            Adjacency = new GraphShardAdjacency
            {
                Outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:alice"] =
                    [
                        new ShardEdgeRef
                        {
                            RelId = "rel:KNOWS:1",
                            RelType = "KNOWS",
                            NeighborNodeId = "node:Person:bob",
                            Direction = ""
                        }
                    ]
                }
            },
            SeedProvenance = new GraphShardSeedProvenance
            {
                RequestedCount = 999,
                IncludedCount = 999,
                ExcludedCount = 999,
                RequestedSeeds =
                [
                    new GraphShardSeedRecord
                    {
                        TableName = "Person",
                        RequestedNodeId = "alice",
                        ExternalId = "node:Person:alice",
                        Status = "included"
                    }
                ]
            },
            Boundary = new GraphShardBoundary
            {
                IsTruncated = true,
                TruncationReasons = ["depth_limit", "depth_limit"],
                BoundaryNodeIds = ["node:Person:bob", "node:Person:bob"],
                FetchHints = new GraphShardBoundaryFetchHints
                {
                    Reasons = ["depth_limit", "depth_limit"],
                    RecommendedSeedNodeIds = ["node:Person:bob", "node:Person:bob"]
                }
            },
            Stats = new GraphShardStats
            {
                NodeCount = 0,
                EdgeCount = 0,
                BoundaryNodeCount = 0,
                NodeTableCount = 0,
                RelTableCount = 0
            }
        };

        var normalized = GraphShardJson.Deserialize(GraphShardJson.Serialize(shard));

        Assert.Equal(["age", "name"], normalized.NodeTables["Person"].PropertyColumns);
        Assert.Equal("node:Person:alice", normalized.NodeTables["Person"].Rows[0].ExternalId);
        Assert.Equal("out", normalized.Adjacency.Outgoing["node:Person:alice"][0].Direction);
        Assert.Equal(2, normalized.Stats.NodeCount);
        Assert.Equal(1, normalized.Stats.EdgeCount);
        Assert.Equal(1, normalized.Stats.BoundaryNodeCount);
        Assert.Equal(1, normalized.SeedProvenance.RequestedCount);
        Assert.Equal(1, normalized.SeedProvenance.IncludedCount);
        Assert.Equal(0, normalized.SeedProvenance.ExcludedCount);
        Assert.Single(normalized.Boundary.TruncationReasons);
        Assert.Single(normalized.Boundary.BoundaryNodeIds);
        Assert.Single(normalized.Boundary.FetchHints.RecommendedSeedNodeIds);
    }

    [Fact]
    public void GraphShardJson_Serialize_ThrowsForAdjacencyReferencingMissingNode()
    {
        var shard = new GraphShard
        {
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    Rows =
                    [
                        new NodeShardRow { ExternalId = "node:Person:alice" }
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
                        new RelShardRow
                        {
                            RelId = "rel:KNOWS:1",
                            SourceNodeId = "node:Person:alice",
                            TargetNodeId = "node:Person:bob"
                        }
                    ]
                }
            }
        };

        var ex = Assert.Throws<GraphShardValidationException>(() => GraphShardJson.Serialize(shard));
        Assert.Contains("missing target node", ex.Message);
    }

    [Fact]
    public void GraphShardJson_WriteToStream_RoundTripsPayload()
    {
        var shard = CreateSampleShard();
        using var stream = new MemoryStream();

        GraphShardJson.WriteToStream(stream, shard);

        var roundTripped = GraphShardJson.Deserialize(stream.ToArray());
        Assert.Equal(shard.GraphVersionToken, roundTripped.GraphVersionToken);
        Assert.Equal(shard.Stats.NodeCount, roundTripped.Stats.NodeCount);
    }

    [Fact]
    public void GraphShardJson_WriteToFile_WritesCanonicalPayload()
    {
        var shard = CreateSampleShard();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.graphshard.json");

        try
        {
            GraphShardJson.WriteToFile(path, shard, writeIndented: true);

            Assert.True(File.Exists(path));
            var roundTripped = GraphShardJson.Deserialize(File.ReadAllText(path));
            Assert.Equal(shard.GraphVersionToken, roundTripped.GraphVersionToken);
            Assert.Equal(shard.Stats.EdgeCount, roundTripped.Stats.EdgeCount);
            Assert.Contains(Environment.NewLine, File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static GraphShard CreateSampleShard()
    {
        return new GraphShard
        {
            GraphVersionToken = "graph-v1",
            NodeTables = new Dictionary<string, NodeShardTable>(StringComparer.Ordinal)
            {
                ["Person"] = new()
                {
                    TableName = "Person",
                    PropertyColumns = ["name", "age"],
                    Rows =
                    [
                        new NodeShardRow
                        {
                            ExternalId = "node:Person:alice",
                            Properties = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["name"] = "Alice",
                                ["age"] = 30
                            }
                        },
                        new NodeShardRow
                        {
                            ExternalId = "node:Person:bob",
                            Properties = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["name"] = "Bob",
                                ["age"] = 28
                            }
                        }
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
                    PropertyColumns = ["since"],
                    Rows =
                    [
                        new RelShardRow
                        {
                            RelId = "rel:KNOWS:0",
                            SourceNodeId = "node:Person:alice",
                            TargetNodeId = "node:Person:bob",
                            Properties = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["since"] = 2020
                            }
                        }
                    ]
                }
            },
            Adjacency = new GraphShardAdjacency
            {
                Outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:alice"] =
                    [
                        new ShardEdgeRef
                        {
                            RelId = "rel:KNOWS:0",
                            RelType = "KNOWS",
                            NeighborNodeId = "node:Person:bob",
                            Direction = "out"
                        }
                    ]
                },
                Incoming = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
                {
                    ["node:Person:bob"] =
                    [
                        new ShardEdgeRef
                        {
                            RelId = "rel:KNOWS:0",
                            RelType = "KNOWS",
                            NeighborNodeId = "node:Person:alice",
                            Direction = "in"
                        }
                    ]
                }
            },
            SeedProvenance = new GraphShardSeedProvenance
            {
                RequestedCount = 1,
                IncludedCount = 1,
                ExcludedCount = 0,
                RequestedSeeds =
                [
                    new GraphShardSeedRecord
                    {
                        TableName = "Person",
                        RequestedNodeId = "alice",
                        ExternalId = "node:Person:alice",
                        Status = "included"
                    }
                ]
            },
            Boundary = new GraphShardBoundary
            {
                HasNodeBoundary = true,
                HasEdgeBoundary = false,
                IsTruncated = true,
                TruncatedByDepth = true,
                TruncationReasons = ["depth_limit"],
                BoundaryNodeIds = ["node:Person:bob"],
                FetchHints = new GraphShardBoundaryFetchHints
                {
                    ShouldFetchMore = true,
                    CanResumeFromBoundary = true,
                    RecommendedSeedNodeIds = ["node:Person:bob"],
                    SuggestedMaxDepth = 2,
                    SuggestedMaxNodes = 10,
                    SuggestedMaxEdges = 10,
                    Reasons = ["depth_limit"]
                }
            },
            Stats = new GraphShardStats
            {
                NodeCount = 2,
                EdgeCount = 1,
                BoundaryNodeCount = 1,
                NodeTableCount = 1,
                RelTableCount = 1
            },
            Options = new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                MaxNodes = 10,
                MaxEdges = 10,
                IncludeOutgoing = true,
                IncludeIncoming = true,
                IncludeNodeProperties = true,
                IncludeRelProperties = true,
                IncludeAdjacency = true,
                IncludeBoundaryMetadata = true,
                RelTypes = ["KNOWS"],
                NodeTables = ["Person"]
            },
            Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = "unit-test"
            }
        };
    }
}
