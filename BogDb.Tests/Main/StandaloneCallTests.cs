using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class StandaloneCallTests
{
    [Fact]
    public void BogConnection_Query_StandaloneCall_SetsExtensionOption_ThroughPipeline()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);
        database.AddExtensionOption("http_timeout", LogicalTypeID.INT64, 30L);

        var result = connection.Query("CALL http_timeout = 45");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(45L, database.GetExtensionOptionValue("http_timeout"));
    }

    [Fact]
    public void BogConnection_Query_StandaloneCall_BindsParameterValue_ThroughPipeline()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);
        database.AddExtensionOption("http_cache_file", LogicalTypeID.BOOL, false);

        var result = connection.Query(
            "CALL http_cache_file = $enabled",
            new Dictionary<string, object?> { ["enabled"] = true });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(true, database.GetExtensionOptionValue("http_cache_file"));
    }
}
