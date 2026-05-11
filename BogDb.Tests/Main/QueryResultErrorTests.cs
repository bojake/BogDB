using Xunit;
using BogDb.Core.Main;

namespace BogDb.Tests.Main;

public class QueryResultErrorTests
{
    [Fact]
    public void Query_InvalidSyntax_ReturnsFailedQueryResult()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var result = connection.Query("THIS IS NOT CYPHER");

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Contains("Syntax error", result.ErrorMessage);
        Assert.DoesNotContain("Statement type not yet implemented", result.ErrorMessage);
        Assert.Equal(0UL, result.GetNumTuples());
        Assert.False(result.HasNext());
        Assert.Throws<InvalidOperationException>(() => result.GetNext());
    }

    [Fact]
    public void Query_BeginTransactionTwice_ReturnsFailedQueryResult()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var first = connection.Query("BEGIN TRANSACTION");
        Assert.True(first.IsSuccess);

        var second = connection.Query("BEGIN TRANSACTION");
        Assert.False(second.IsSuccess);
        Assert.Contains("already active", second.ErrorMessage);
    }
}
