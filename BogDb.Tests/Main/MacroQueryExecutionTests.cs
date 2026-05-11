using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class MacroQueryExecutionTests
{
    [Fact]
    public void CreateMacro_AllowsInvocationInLaterQuery()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var createResult = conn.Query("CREATE MACRO Add10(x) AS x + 10");
        Assert.True(createResult.IsSuccess, createResult.ErrorMessage);

        var queryResult = conn.Query("RETURN Add10(5)");
        Assert.True(queryResult.IsSuccess, queryResult.ErrorMessage);
        Assert.True(queryResult.HasNext());
        Assert.Equal(15L, queryResult.GetNext().GetInt64(0));
    }

    [Fact]
    public void CreateMacro_DefaultParameters_AreAppliedWhenArgumentOmitted()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var createResult = conn.Query("CREATE MACRO AddDefault(x, y := 40) AS x + y");
        Assert.True(createResult.IsSuccess, createResult.ErrorMessage);

        var queryResult = conn.Query("RETURN AddDefault(2), AddDefault(2, 3)");
        Assert.True(queryResult.IsSuccess, queryResult.ErrorMessage);
        Assert.True(queryResult.HasNext());
        var row = queryResult.GetNext();
        Assert.Equal(42L, row.GetInt64(0));
        Assert.Equal(5L, row.GetInt64(1));
    }

    [Fact]
    public void CreateMacro_WithoutParameters_ReturnsConstant()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var createResult = conn.Query("CREATE MACRO ReturnConst() AS 42");
        Assert.True(createResult.IsSuccess, createResult.ErrorMessage);

        var queryResult = conn.Query("RETURN ReturnConst()");
        Assert.True(queryResult.IsSuccess, queryResult.ErrorMessage);
        Assert.True(queryResult.HasNext());
        Assert.Equal(42L, queryResult.GetNext().GetInt64(0));
    }
}
