using System.Collections.Generic;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class StructLiteralTests
{
    [Fact]
    public void BogConnection_Query_StructLiteral_AllowsFieldExpressions()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var result = connection.Query(
            "RETURN {name: $name, age: 20 + 1, nested: {enabled: $enabled}} AS s",
            new Dictionary<string, object?> { ["name"] = "Ada", ["enabled"] = true });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var row = result.GetNext().GetAsDictionary();
        var s = Assert.IsType<Dictionary<string, object?>>(row["s"]);
        Assert.Equal("Ada", s["name"]);
        Assert.Equal(21L, s["age"]);
        var nested = Assert.IsType<Dictionary<string, object?>>(s["nested"]);
        Assert.Equal(true, nested["enabled"]);
    }
}
