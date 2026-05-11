using System;
using Xunit;
using BogDb.Core.Function;

namespace BogDb.Tests.Function;

public sealed class DateFunctionTests
{
    [Fact] public void Now_ReturnsNonNullString()
    {
        var result = FunctionDispatcher.Invoke("now", []) as string;
        Assert.NotNull(result);
        Assert.True(DateTime.TryParse(result, out _));
    }

    [Fact] public void CurrentDate_ReturnsDateString()
    {
        var result = FunctionDispatcher.Invoke("current_date", []) as string;
        Assert.NotNull(result);
        Assert.True(DateOnly.TryParse(result, out var d));
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).Year, d.Year);
    }

    [Fact] public void Date_ParsesIsoString()
        => Assert.Equal("2024-03-19", FunctionDispatcher.Invoke("date", ["2024-03-19"]));

    [Fact] public void Timestamp_ParsesIsoString()
    {
        var result = FunctionDispatcher.Invoke("timestamp", ["2024-03-19T10:30:00Z"]) as string;
        Assert.NotNull(result);
        Assert.True(DateTime.TryParse(result, out _));
    }

    [Fact] public void Year_Extract()
        => Assert.Equal(2024L, FunctionDispatcher.Invoke("year", ["2024-03-19"]));

    [Fact] public void Month_Extract()
        => Assert.Equal(3L, FunctionDispatcher.Invoke("month", ["2024-03-19"]));

    [Fact] public void Day_Extract()
        => Assert.Equal(19L, FunctionDispatcher.Invoke("day", ["2024-03-19"]));

    [Fact] public void Hour_Extract()
        => Assert.Equal(10L, FunctionDispatcher.Invoke("hour", ["2024-03-19T10:30:00Z"]));

    [Fact] public void Minute_Extract()
        => Assert.Equal(30L, FunctionDispatcher.Invoke("minute", ["2024-03-19T10:30:00Z"]));

    [Fact] public void DatePart_Quarter()
        => Assert.Equal(1L, FunctionDispatcher.Invoke("date_part", ["quarter", "2024-01-15"]));

    [Fact] public void DateAdd_Day()
    {
        var result = FunctionDispatcher.Invoke("date_add", ["day", "2024-03-01", 5L]) as string;
        Assert.NotNull(result);
        var dt = DateTime.Parse(result);
        Assert.Equal(6, dt.Day);
    }

    [Fact] public void DateDiff_Days()
        => Assert.Equal(5L, FunctionDispatcher.Invoke("date_diff", ["day", "2024-03-10", "2024-03-15"]));

    [Fact] public void EpochMs_IsPositive()
        => Assert.True((long)FunctionDispatcher.Invoke("epoch_ms", ["2024-01-01T00:00:00Z"])! > 0);

    [Fact] public void DateTrunc_Month()
    {
        var result = FunctionDispatcher.Invoke("date_trunc", ["month", "2024-03-19"]) as string;
        Assert.NotNull(result);
        // Use DateTimeOffset to avoid local-timezone conversion of the UTC timestamp
        var dto = DateTimeOffset.Parse(result, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.Equal(1, dto.Day);
        Assert.Equal(3, dto.Month);
    }
}
