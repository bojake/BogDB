using System.Linq;
using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Main;
using BogDb.Core.Main.QueryResult;
using BogDb.Core.Processor.Operator.OrderBy;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class ObservabilityMetricsTests
{
    [Fact]
    public void QuerySummary_ExposesTotalMilliseconds_AndOptionalExecutionMetrics()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var defaultResult = connection.Query("RETURN 1 AS v");
        Assert.True(defaultResult.IsSuccess, defaultResult.ErrorMessage);
        Assert.NotNull(defaultResult.TotalMilliseconds);
        Assert.True(defaultResult.TotalMilliseconds >= 0);
        Assert.Null(defaultResult.ExecutionMetrics);
        Assert.Null(defaultResult.Summary.ExecutionMetrics);

        var metricsResult = connection.Query(
            "RETURN 1 AS v",
            new BogDbQueryMetricsOptions
            {
                CaptureExecutionMetrics = true
            });

        Assert.True(metricsResult.IsSuccess, metricsResult.ErrorMessage);
        Assert.NotNull(metricsResult.ExecutionMetrics);
        Assert.Equal(1UL, metricsResult.ExecutionMetrics!.RowsProduced);
        Assert.Equal(0, metricsResult.ExecutionMetrics.SpillCount);
        Assert.NotNull(metricsResult.Summary.TotalMilliseconds);
        Assert.NotNull(metricsResult.Summary.ExecutionMetrics);
    }

    [Fact]
    public void QuerySummary_ExecutionMetrics_TracksOrderBySpillTelemetry()
    {
        var originalChunkLimit = OrderBy.ChunkRowLimit;
        var originalChunkByteLimit = OrderBy.ChunkByteLimitOverride;
        OrderBy.ChunkRowLimit = 2;
        OrderBy.ChunkByteLimitOverride = null;

        try
        {
            using var database = BogDatabase.Open(":memory:");
            using var connection = new BogConnection(database);

            var before = database.GetMetricsSnapshot();
            var result = connection.Query(
                "UNWIND [9, 3, 7, 1, 8, 2, 6, 4, 5] AS x RETURN x ORDER BY x",
                new BogDbQueryMetricsOptions
                {
                    CaptureExecutionMetrics = true
                });

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.NotNull(result.ExecutionMetrics);
            Assert.Equal(9UL, result.ExecutionMetrics!.RowsProduced);
            Assert.True(result.ExecutionMetrics.TempBytesWritten > 0);
            Assert.True(result.ExecutionMetrics.SpillCount > 0);
            Assert.True(result.ExecutionMetrics.SpillRunCount >= result.ExecutionMetrics.SpillCount);
            Assert.True(result.ExecutionMetrics.MaxMergeFanIn >= 2);

            var after = database.GetMetricsSnapshot();
            Assert.True(after.TempBytesWritten >= before.TempBytesWritten + result.ExecutionMetrics.TempBytesWritten);
        }
        finally
        {
            OrderBy.ChunkRowLimit = originalChunkLimit;
            OrderBy.ChunkByteLimitOverride = originalChunkByteLimit;
        }
    }

    [Fact]
    public void DatabaseMetricsSnapshot_TracksQueries_Commits_AndReset()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var before = database.GetMetricsSnapshot();

        connection.Query("RETURN 1 AS v");

        connection.BeginWriteTransaction();
        connection.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.INT64,
            ["name"] = LogicalTypeID.STRING
        });
        connection.Commit();

        var after = database.GetMetricsSnapshot();
        Assert.True(after.QueryCount >= before.QueryCount + 1);
        Assert.True(after.TotalQueryMilliseconds >= before.TotalQueryMilliseconds);
        Assert.True(after.TransactionCommitCount >= before.TransactionCommitCount + 1);
        Assert.True(after.IndexLookupCount >= before.IndexLookupCount);

        database.ResetMetrics();

        var reset = database.GetMetricsSnapshot();
        Assert.Equal(0, reset.QueryCount);
        Assert.Equal(0, reset.FailedQueryCount);
        Assert.Equal(0, reset.TransactionCommitCount);
        Assert.Equal(0, reset.TransactionRollbackCount);
        Assert.Equal(0, reset.ExtractionCount);
        Assert.Equal(0, reset.TotalQueryMilliseconds);
        Assert.Equal(0, reset.TotalExtractionMilliseconds);
    }

    [Fact]
    public void DatabaseMetricsSnapshot_TracksExtractionCalls()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        connection.BeginWriteTransaction();
        connection.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.INT64,
            ["name"] = LogicalTypeID.STRING
        });
        connection.UpsertNode("Person", 1L, new Dictionary<string, object>
        {
            ["id"] = 1L,
            ["name"] = "Alice"
        });
        connection.Commit();

        var before = database.GetMetricsSnapshot();
        var shard = connection.ExtractGraphNeighborhood("Person", 1L);

        Assert.NotNull(shard);

        var after = database.GetMetricsSnapshot();
        Assert.True(after.ExtractionCount >= before.ExtractionCount + 1);
        Assert.True(after.TotalExtractionMilliseconds >= before.TotalExtractionMilliseconds);
    }

    [Fact]
    public void DatabaseMetricsSnapshot_TracksReadWriteActivity()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        connection.BeginWriteTransaction();
        connection.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.INT64,
            ["name"] = LogicalTypeID.STRING
        });
        connection.EnsureNodeTable("Company", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.INT64,
            ["name"] = LogicalTypeID.STRING
        });
        connection.EnsureRelTable("WORKS_AT", "Person", "Company", new Dictionary<string, LogicalTypeID>());
        connection.UpsertNode("Person", 1L, new Dictionary<string, object>
        {
            ["id"] = 1L,
            ["name"] = "Alice"
        });
        connection.UpsertNode("Company", 10L, new Dictionary<string, object>
        {
            ["id"] = 10L,
            ["name"] = "Acme"
        });
        connection.UpsertRelationship("WORKS_AT", 1L, 10L, new Dictionary<string, object>());
        connection.Commit();

        var beforeReads = database.GetMetricsSnapshot();

        connection.ReadNode("Person", 1L);
        connection.GetNodeById("1", out _);
        connection.GetOutgoingEdges("1");
        _ = database.EnumerateNodeRows("Person").ToList();
        connection.CreateIndex("Person", "name");

        var afterReads = database.GetMetricsSnapshot();
        Assert.True(afterReads.NodesWritten >= 2);
        Assert.True(afterReads.RelationshipsWritten >= 1);
        Assert.True(afterReads.NodeReadCount >= beforeReads.NodeReadCount + 2);
        Assert.True(afterReads.RelReadCount >= beforeReads.RelReadCount + 1);
        Assert.True(afterReads.IndexLookupCount >= beforeReads.IndexLookupCount + 1);
    }
}
