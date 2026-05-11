using System;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class ConnectionControlTests
{
    [Fact]
    public void QueryTimeout_LongRunningQueryReturnsTimeoutError()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.SetQueryTimeout(TimeSpan.FromMilliseconds(10));

        var result = conn.Query("RETURN sleep(30) AS slept");

        Assert.False(result.IsSuccess);
        Assert.Contains("timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interrupt_BeforeQuery_ReturnsInterruptedError()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.Interrupt();

        var result = conn.Query("RETURN 1 AS value");

        Assert.False(result.IsSuccess);
        Assert.Contains("interrupted", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
