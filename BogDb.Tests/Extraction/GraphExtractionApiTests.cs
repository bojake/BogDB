using BogDb.Core.Extraction;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Extraction;

public sealed class GraphExtractionApiTests
{
    [Fact]
    public void DatabaseApi_ExtractGraphNeighborhood_MatchesDirectExtractor()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var options = new GraphShardExtractionOptions
        {
            MaxDepth = 1,
            IncludeOutgoing = true,
            IncludeIncoming = false
        };

        var viaDatabase = db.ExtractGraphNeighborhood("Person", "alice", options);
        var viaExtractor = new GraphShardExtractor(db).ExtractNeighborhood("Person", "alice", options);

        Assert.Equal(viaExtractor.Stats.NodeCount, viaDatabase.Stats.NodeCount);
        Assert.Equal(viaExtractor.Stats.EdgeCount, viaDatabase.Stats.EdgeCount);
        Assert.Equal(viaExtractor.ExtractionPolicy, viaDatabase.ExtractionPolicy);
        Assert.Equal(
            viaExtractor.NodeTables["Person"].Rows.Select(r => r.ExternalId).OrderBy(x => x),
            viaDatabase.NodeTables["Person"].Rows.Select(r => r.ExternalId).OrderBy(x => x));
    }

    [Fact]
    public void ConnectionApi_ExtractGraphNeighborhood_UsesActiveTransactionVisibility()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        using var conn = new BogConnection(db);
        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", "dave", new Dictionary<string, object>
        {
            ["name"] = "Dave",
            ["age"] = 40L
        });
        conn.UpsertRelationship("KNOWS", "carol", "dave", new Dictionary<string, object>
        {
            ["since"] = 2022L
        });

        var shardInTx = conn.ExtractGraphNeighborhood(
            "Person",
            "carol",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.Contains(shardInTx.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:dave");

        conn.Commit();

        var shardAfterCommit = conn.ExtractGraphNeighborhood(
            "Person",
            "carol",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.Contains(shardAfterCommit.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:dave");
    }

    [Fact]
    public void ConnectionApi_ExtractGraphNeighborhood_HidesRolledBackWrite()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        using var conn = new BogConnection(db);
        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", "frank", new Dictionary<string, object>
        {
            ["name"] = "Frank",
            ["age"] = 29L
        });
        conn.UpsertRelationship("KNOWS", "carol", "frank", new Dictionary<string, object>
        {
            ["since"] = 2024L
        });

        var shardInTx = conn.ExtractGraphNeighborhood(
            "Person",
            "carol",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.Contains(shardInTx.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:frank");

        conn.ClientContext.Rollback();

        var shardAfterRollback = conn.ExtractGraphNeighborhood(
            "Person",
            "carol",
            new GraphShardExtractionOptions
            {
                MaxDepth = 1,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.DoesNotContain(
            shardAfterRollback.NodeTables["Person"].Rows,
            row => row.ExternalId == "node:Person:frank");
    }

    [Fact]
    public void ConnectionApi_ExtractGraphNodeSet_ReturnsRequestedSubset()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        using var conn = new BogConnection(db);

        var shard = conn.ExtractGraphNodeSet(
            [
                new GraphNodeSelector { TableName = "Person", NodeId = "alice" },
                new GraphNodeSelector { TableName = "City", NodeId = "seattle" }
            ],
            new GraphShardExtractionOptions
            {
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.True(shard.NodeTables.ContainsKey("Person"));
        Assert.True(shard.NodeTables.ContainsKey("City"));
        Assert.Single(shard.NodeTables["Person"].Rows);
        Assert.Single(shard.NodeTables["City"].Rows);
        Assert.Single(shard.RelTables["LIVES_IN"].Rows);
    }

    [Fact]
    public void DatabaseApi_ExportGraphNeighborhoodAsJson_MatchesCanonicalSerializer()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var options = new GraphShardExtractionOptions
        {
            MaxDepth = 1,
            IncludeOutgoing = true,
            IncludeIncoming = false
        };

        var shard = db.ExtractGraphNeighborhood("Person", "alice", options);
        var viaApi = db.ExportGraphNeighborhoodAsJson("Person", "alice", options);
        var deserialized = GraphShardJson.Deserialize(viaApi);

        Assert.Equal(GraphShard.CurrentFormatVersion, deserialized.FormatVersion);
        Assert.Equal(GraphShard.CurrentExtractorVersion, deserialized.ExtractorVersion);
        Assert.Equal(shard.GraphVersionToken, deserialized.GraphVersionToken);
        Assert.Equal(shard.Stats.NodeCount, deserialized.Stats.NodeCount);
        Assert.Equal(shard.Stats.EdgeCount, deserialized.Stats.EdgeCount);
        Assert.Equal(shard.ExtractionPolicy, deserialized.ExtractionPolicy);
        Assert.Equal(
            shard.NodeTables["Person"].Rows.Select(r => r.ExternalId).OrderBy(x => x),
            deserialized.NodeTables["Person"].Rows.Select(r => r.ExternalId).OrderBy(x => x));
    }

    [Fact]
    public void ConnectionApi_ExportGraphNodeSetAsJson_UsesActiveTransactionVisibility()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        using var conn = new BogConnection(db);
        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", "erin", new Dictionary<string, object>
        {
            ["name"] = "Erin",
            ["age"] = 34L
        });

        var json = conn.ExportGraphNodeSetAsJson(
            [new GraphNodeSelector { TableName = "Person", NodeId = "erin" }]);

        var shard = GraphShardJson.Deserialize(json);
        Assert.Contains(shard.NodeTables["Person"].Rows, row => row.ExternalId == "node:Person:erin");
    }
}
