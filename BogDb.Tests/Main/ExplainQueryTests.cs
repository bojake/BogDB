using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class ExplainQueryTests
{
    [Fact]
    public void BogConnection_Query_Explain_ReturnsPhysicalPlanText()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var result = connection.Query("EXPLAIN RETURN 1 AS v");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(new[] { "explain result" }, result.ColumnNames.ToArray());
        Assert.Equal(1UL, result.GetNumTuples());

        var planText = result.GetNext().GetString(0);
        Assert.Contains("PROJECTION", planText);
        Assert.Contains("EXPRESSIONS_SCAN", planText);
        Assert.DoesNotContain("LOGICAL_", planText);
    }

    [Fact]
    public void BogConnection_Query_ExplainLogical_ReturnsLogicalPlanText()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var result = connection.Query("EXPLAIN LOGICAL RETURN 1 AS v");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(new[] { "explain result" }, result.ColumnNames.ToArray());
        Assert.Equal(1UL, result.GetNumTuples());

        var planText = result.GetNext().GetString(0);
        Assert.Contains("PROJECTION", planText);
        Assert.Contains("EXPRESSIONS_SCAN", planText);
    }

    [Fact]
    public void BogConnection_Query_Explain_DistinctAndLimitPipeline_ShowsDistinctAndTopK()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var result = connection.Query(
            "EXPLAIN UNWIND [1, 2, 1] AS v RETURN DISTINCT v ORDER BY v LIMIT 2");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var planText = result.GetNext().GetString(0);
        Assert.Contains("DISTINCT", planText);
        Assert.Contains("TOP_K", planText);
        Assert.DoesNotContain("ORDER_BY", planText);
    }
}
