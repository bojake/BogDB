using BogDb.Core.Extraction;
using Xunit;
using System;
using System.Globalization;

namespace BogDb.Tests.Extraction;

public sealed class GraphShardMetadataTests
{
    [Fact]
    public void ExtractedShard_AlwaysCarriesFormatVersionAndGraphVersionToken()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood("Person", "alice");

        Assert.Equal(GraphShard.CurrentFormatVersion, shard.FormatVersion);
        Assert.False(string.IsNullOrWhiteSpace(shard.GraphVersionToken));
        Assert.StartsWith("catalog-v", shard.GraphVersionToken);
        Assert.Equal(GraphShard.CurrentExtractorVersion, shard.ExtractorVersion);
        Assert.False(string.IsNullOrWhiteSpace(shard.ExtractedAtUtc));
        Assert.True(DateTime.TryParse(
            shard.ExtractedAtUtc,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var extractedAt));
        Assert.Equal(DateTimeKind.Utc, extractedAt.Kind);
        Assert.False(string.IsNullOrWhiteSpace(shard.ExtractionPolicy));
    }

    [Fact]
    public void RepeatedExtraction_KeepsGraphVersionStable_ButEmitsExtractorMetadata()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var first = extractor.ExtractNeighborhood("Person", "alice");
        System.Threading.Thread.Sleep(5);
        var second = extractor.ExtractNeighborhood("Person", "alice");

        Assert.Equal(first.GraphVersionToken, second.GraphVersionToken);
        Assert.Equal(GraphShard.CurrentExtractorVersion, first.ExtractorVersion);
        Assert.Equal(GraphShard.CurrentExtractorVersion, second.ExtractorVersion);
        Assert.NotEqual(string.Empty, first.ExtractedAtUtc);
        Assert.NotEqual(string.Empty, second.ExtractedAtUtc);
    }

    [Fact]
    public void CompleteExtract_HasNonTruncatedBoundaryMetadata()
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

        Assert.True(shard.IsComplete);
        Assert.False(shard.Boundary.IsTruncated);
        Assert.False(shard.Boundary.TruncatedByDepth);
        Assert.False(shard.Boundary.TruncatedByNodeLimit);
        Assert.False(shard.Boundary.TruncatedByEdgeLimit);
        Assert.Empty(shard.Boundary.TruncationReasons);
        Assert.Empty(shard.Boundary.BoundaryNodeIds);
        Assert.False(shard.Boundary.FetchHints.ShouldFetchMore);
        Assert.False(shard.Boundary.FetchHints.CanResumeFromBoundary);
        Assert.Empty(shard.Boundary.FetchHints.RecommendedSeedNodeIds);
    }

    [Fact]
    public void NodeLimitHit_PopulatesTruncationMetadata()
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

        Assert.False(shard.IsComplete);
        Assert.True(shard.Boundary.IsTruncated);
        Assert.True(shard.Boundary.TruncatedByNodeLimit);
        Assert.Contains("node_limit", shard.Boundary.TruncationReasons);
        Assert.NotEmpty(shard.Boundary.BoundaryNodeIds);
        Assert.True(shard.Boundary.FetchHints.ShouldFetchMore);
        Assert.True(shard.Boundary.FetchHints.CanResumeFromBoundary);
        Assert.Equal(shard.Boundary.BoundaryNodeIds, shard.Boundary.FetchHints.RecommendedSeedNodeIds);
        Assert.True(shard.Boundary.FetchHints.SuggestedMaxNodes >= shard.Stats.NodeCount);
        Assert.Contains("node_limit", shard.Boundary.FetchHints.Reasons);
    }

    [Fact]
    public void SeedProvenance_TracksIncludedAndExcludedSeeds()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood(
            [
                new GraphNodeSelector { TableName = "Person", NodeId = "alice" },
                new GraphNodeSelector { TableName = "City", NodeId = "seattle" }
            ],
            new GraphShardExtractionOptions
            {
                NodeTables = ["Person"],
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.Equal(2, shard.SeedProvenance.RequestedCount);
        Assert.Equal(1, shard.SeedProvenance.IncludedCount);
        Assert.Equal(1, shard.SeedProvenance.ExcludedCount);

        var included = Assert.Single(shard.SeedProvenance.RequestedSeeds, s => s.Status == "included");
        Assert.Equal("Person", included.TableName);
        Assert.Equal("node:Person:alice", included.ExternalId);

        var excluded = Assert.Single(shard.SeedProvenance.RequestedSeeds, s => s.Status == "excluded");
        Assert.Equal("City", excluded.TableName);
        Assert.Equal("filtered", excluded.Reason);
    }

    [Fact]
    public void DepthLimitHit_PopulatesBoundaryFetchHints()
    {
        using var db = ExtractionTestGraphFactory.CreateSampleDatabase();
        var extractor = new GraphShardExtractor(db);

        var shard = extractor.ExtractNeighborhood(
            "Person",
            "alice",
            new GraphShardExtractionOptions
            {
                MaxDepth = 0,
                IncludeOutgoing = true,
                IncludeIncoming = false
            });

        Assert.False(shard.IsComplete);
        Assert.True(shard.Boundary.TruncatedByDepth);
        Assert.True(shard.Boundary.FetchHints.ShouldFetchMore);
        Assert.True(shard.Boundary.FetchHints.CanResumeFromBoundary);
        Assert.Equal(1, shard.Boundary.FetchHints.SuggestedMaxDepth);
        Assert.Equal(shard.Boundary.BoundaryNodeIds, shard.Boundary.FetchHints.RecommendedSeedNodeIds);
        Assert.Contains("depth_limit", shard.Boundary.FetchHints.Reasons);
    }
}
