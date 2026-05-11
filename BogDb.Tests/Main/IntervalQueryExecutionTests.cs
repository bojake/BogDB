using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class IntervalQueryExecutionTests
{
    [Fact]
    public void ReturnIntervalConstructor_StringifiesIsoDuration()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN interval('3 days') AS delta");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("P3D", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnDatePlusInterval_ShiftsDate()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN date('2024-01-01') + interval('3 days') AS shifted");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("2024-01-04", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnDateDifference_AsInterval()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN date('2024-01-10') - date('2024-01-01') AS delta");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("P9D", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnTimestampDifference_CanonicalizesWholeDays()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN timestamp('2024-01-03T00:00:00Z') - timestamp('2024-01-01T00:00:00Z') AS delta");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("P2D", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnIntervalQuarterAndWeekParts()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN date_part('quarter', to_months(14)) AS q, date_part('week', to_days(15)) AS w");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal(4L, row.GetInt64(0));
        Assert.Equal(2L, row.GetInt64(1));
    }

    [Fact]
    public void ReturnIntervalExtendedCalendarParts()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN " +
            "date_part('year', interval('1234 years 5 months')) AS y, " +
            "date_part('decade', interval('1234 years 5 months')) AS dec, " +
            "date_part('century', interval('1234 years 5 months')) AS c, " +
            "date_part('millennium', interval('1234 years 5 months')) AS mil, " +
            "date_part('month', interval('1234 years 5 months')) AS m");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal(1234L, row.GetInt64(0));
        Assert.Equal(123L, row.GetInt64(1));
        Assert.Equal(12L, row.GetInt64(2));
        Assert.Equal(1L, row.GetInt64(3));
        Assert.Equal(5L, row.GetInt64(4));
    }

    [Fact]
    public void ReturnIntervalOrderedComparisons_UseNativeNormalization()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN " +
            "interval('1 month') <= interval('30 days') AS month_le_30_days, " +
            "interval('1 month') >= interval('30 days') AS month_ge_30_days, " +
            "interval('1 month') < interval('31 days') AS month_lt_31_days, " +
            "interval('30 days 1 hour') > interval('1 month') AS thirty_days_hour_gt_month");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.True(row.GetBoolean(0));
        Assert.True(row.GetBoolean(1));
        Assert.True(row.GetBoolean(2));
        Assert.True(row.GetBoolean(3));
    }

    [Fact]
    public void ReturnMixedDayTimeInterval_CanonicalizesAcrossBoundary()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN to_days(1) - to_hours(1) AS delta, date_part('day', to_days(1) - to_hours(1)) AS d, date_part('hour', to_days(1) - to_hours(1)) AS h");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("PT23H", row.GetString(0));
        Assert.Equal(0L, row.GetInt64(1));
        Assert.Equal(23L, row.GetInt64(2));
    }

    [Fact]
    public void ReturnMixedMonthAndDaySigns_AsSignedComponents()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN to_months(1) - to_days(1) AS delta, date_part('month', to_months(1) - to_days(1)) AS m, date_part('day', to_months(1) - to_days(1)) AS d");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("1 month -1 day", row.GetString(0));
        Assert.Equal(1L, row.GetInt64(1));
        Assert.Equal(-1L, row.GetInt64(2));
    }

    [Fact]
    public void ReturnUnaryNegatedInterval()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN -to_days(2) AS delta, date_part('day', -to_days(2)) AS d");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("-P2D", row.GetString(0));
        Assert.Equal(-2L, row.GetInt64(1));
    }

    [Fact]
    public void ReturnNegativeTimeOnlyInterval_PreservesSignedHourPart()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN -to_hours(2) AS delta, date_part('hour', -to_hours(2)) AS h");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("-PT2H", row.GetString(0));
        Assert.Equal(-2L, row.GetInt64(1));
    }

    [Fact]
    public void ReturnNegativeCrossDayInterval_PreservesSignedRemainderParts()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN interval('-25 hours -30 minutes') AS delta, " +
            "date_part('day', interval('-25 hours -30 minutes')) AS d, " +
            "date_part('hour', interval('-25 hours -30 minutes')) AS h, " +
            "date_part('minute', interval('-25 hours -30 minutes')) AS m");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("-P1DT1H30M", row.GetString(0));
        Assert.Equal(-1L, row.GetInt64(1));
        Assert.Equal(-1L, row.GetInt64(2));
        Assert.Equal(-30L, row.GetInt64(3));
    }

    [Fact]
    public void ReturnUnaryNegatedComposedInterval_PreservesSignedRemainderParts()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN -(to_hours(25) + to_minutes(30)) AS delta, " +
            "date_part('day', -(to_hours(25) + to_minutes(30))) AS d, " +
            "date_part('hour', -(to_hours(25) + to_minutes(30))) AS h, " +
            "date_part('minute', -(to_hours(25) + to_minutes(30))) AS m");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("-P1DT1H30M", row.GetString(0));
        Assert.Equal(-1L, row.GetInt64(1));
        Assert.Equal(-1L, row.GetInt64(2));
        Assert.Equal(-30L, row.GetInt64(3));
    }

    // ── G-010: interval + interval query-path ──────────────────────────────────

    [Fact]
    public void ReturnIntervalPlusInterval_CombinesSameComponent()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN to_days(2) + to_days(3) AS delta, " +
            "date_part('day', to_days(2) + to_days(3)) AS d");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("P5D", row.GetString(0));
        Assert.Equal(5L,    row.GetInt64(1));
    }

    [Fact]
    public void ReturnIntervalPlusInterval_CombinesMixedComponents()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN to_months(1) + to_days(15) AS delta, " +
            "date_part('month', to_months(1) + to_days(15)) AS m, " +
            "date_part('day',   to_months(1) + to_days(15)) AS d");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("P1M15D", row.GetString(0));
        Assert.Equal(1L,       row.GetInt64(1));
        Assert.Equal(15L,      row.GetInt64(2));
    }

    [Fact]
    public void ReturnIntervalPlusInterval_TimeComponentsNormalizeToDays()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        // 23h + 2h = 25h = P1DT1H
        var result = conn.Query(
            "RETURN to_hours(23) + to_hours(2) AS delta, " +
            "date_part('day',  to_hours(23) + to_hours(2)) AS d, " +
            "date_part('hour', to_hours(23) + to_hours(2)) AS h");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("P1DT1H", row.GetString(0));
        Assert.Equal(1L,       row.GetInt64(1));
        Assert.Equal(1L,       row.GetInt64(2));
    }

    // ── G-010: date - interval query-path ─────────────────────────────────────

    [Fact]
    public void ReturnDateMinusInterval_ShiftsDateBackward()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN date('2024-01-10') - interval('3 days') AS result");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("2024-01-07", result.GetNext().GetString(0));
    }

    // ── G-010: sub-hour normalization query-path ───────────────────────────────

    [Fact]
    public void ReturnToMinutes_NormalizesAcrossHourBoundary()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN to_minutes(70) AS delta, " +
            "date_part('hour',   to_minutes(70)) AS h, " +
            "date_part('minute', to_minutes(70)) AS m");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("PT1H10M", row.GetString(0));
        Assert.Equal(1L,        row.GetInt64(1));
        Assert.Equal(10L,       row.GetInt64(2));
    }

    [Fact]
    public void ReturnToMilliseconds_NormalizesAcrossSecondBoundary()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN to_milliseconds(1500) AS delta, " +
            "date_part('second',      to_milliseconds(1500)) AS s, " +
            "date_part('millisecond', to_milliseconds(1500)) AS ms");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("PT1.5S", row.GetString(0));
        Assert.Equal(1L,       row.GetInt64(1));
        Assert.Equal(500L,     row.GetInt64(2));
    }

    [Fact]
    public void ReturnToSeconds_NormalizesAcrossMinuteBoundary()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN to_seconds(90) AS delta, " +
            "date_part('minute', to_seconds(90)) AS m, " +
            "date_part('second', to_seconds(90)) AS s");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("PT1M30S", row.GetString(0));
        Assert.Equal(1L,        row.GetInt64(1));
        Assert.Equal(30L,       row.GetInt64(2));
    }

    // ── G-010: reverse mixed-sign query-path ──────────────────────────────────

    [Fact]
    public void ReturnReverseMixedSign_NegativeMonthPositiveDay()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN to_days(5) - to_months(1) AS delta, " +
            "date_part('month', to_days(5) - to_months(1)) AS m, " +
            "date_part('day',   to_days(5) - to_months(1)) AS d");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("-1 month 5 days", row.GetString(0));
        Assert.Equal(-1L,               row.GetInt64(1));
        Assert.Equal(5L,                row.GetInt64(2));
    }

    // ── G-010: zero interval query-path ───────────────────────────────────────

    [Fact]
    public void ReturnZeroInterval_StringifiesCorrectly()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN to_seconds(0) AS delta");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("PT0S", result.GetNext().GetString(0));
    }

    // ── G-010: ISO-8601 string parsing + CAST query-path ──────────────────────

    [Fact]
    public void ReturnIntervalFromIsoString_RoundTrips()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN interval('P1Y2M3DT4H5M6S') AS delta");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("P1Y2M3DT4H5M6S", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnCastAsInterval_FromStringLiteral()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN CAST('3 days' AS INTERVAL) AS delta");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("P3D", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnTimestampPlusInterval_PreservesTimeComponent()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN timestamp('2024-01-01T12:00:00') + interval('2 days') AS result");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("2024-01-03T12:00:00.0000000+00:00", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnTimestampMinusInterval_PreservesTimeComponent()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN timestamp('2024-01-03T12:00:00') - interval('2 days') AS result");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("2024-01-01T12:00:00.0000000+00:00", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnIntervalPlusTimestamp_PreservesTimeComponent()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN interval('2 days') + timestamp('2024-01-01T12:00:00') AS result");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("2024-01-03T12:00:00.0000000+00:00", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnTimestampDifference_PreservesMixedDayTimeRemainder()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN timestamp('2024-01-03T12:00:00') - timestamp('2024-01-01T11:30:00') AS delta, " +
            "date_part('day', timestamp('2024-01-03T12:00:00') - timestamp('2024-01-01T11:30:00')) AS d, " +
            "date_part('minute', timestamp('2024-01-03T12:00:00') - timestamp('2024-01-01T11:30:00')) AS m");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("P2DT30M", row.GetString(0));
        Assert.Equal(2L, row.GetInt64(1));
        Assert.Equal(30L, row.GetInt64(2));
    }

    [Fact]
    public void ReturnNegativeFractionalMinutes_PreservesSignedMinuteAndSecondParts()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN interval('-1.5 minutes') AS delta, " +
            "date_part('minute', interval('-1.5 minutes')) AS m, " +
            "date_part('second', interval('-1.5 minutes')) AS s");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("-PT1M30S", row.GetString(0));
        Assert.Equal(-1L, row.GetInt64(1));
        Assert.Equal(-30L, row.GetInt64(2));
    }

    [Fact]
    public void ReturnMixedSignMonthDayTime_NormalizesAcrossAllBuckets()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN to_months(1) - to_days(1) - to_hours(25) AS delta, " +
            "date_part('month', to_months(1) - to_days(1) - to_hours(25)) AS m, " +
            "date_part('day', to_months(1) - to_days(1) - to_hours(25)) AS d, " +
            "date_part('hour', to_months(1) - to_days(1) - to_hours(25)) AS h");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("1 month -2 days -1 hour", row.GetString(0));
        Assert.Equal(1L, row.GetInt64(1));
        Assert.Equal(-2L, row.GetInt64(2));
        Assert.Equal(-1L, row.GetInt64(3));
    }

    [Fact]
    public void ReturnIntervalList_StringifiesNestedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN [to_days(1), to_hours(25), 'tag'] AS values");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("[P1D,P1DT1H,\"tag\"]", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnIntervalMap_StringifiesNestedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN map(['day', 'hour'], [to_days(1), to_hours(25)]) AS parts");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("{\"day\":P1D,\"hour\":P1DT1H}", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnIntervalListEquality_ComparesStructurally()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN [to_days(1), to_hours(25)] = [to_days(1), to_hours(25)] AS eq");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.True(result.GetNext().GetBoolean(0));
    }

    [Fact]
    public void ReturnIntervalMapEquality_ComparesStructurally()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN map(['day', 'hour'], [to_days(1), to_hours(25)]) = map(['day', 'hour'], [to_days(1), to_hours(25)]) AS eq");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.True(result.GetNext().GetBoolean(0));
    }

    [Fact]
    public void ReturnIntervalStructEquality_ComparesStructurally()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN struct_pack('day', to_days(1), 'hour', to_hours(25)) = struct_pack('day', to_days(1), 'hour', to_hours(25)) AS eq");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.True(result.GetNext().GetBoolean(0));
    }

    [Fact]
    public void ReturnIntervalListContains_UsesStructuralMembership()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN list_contains([[to_days(1)], [to_days(2)]], [to_days(1)]) AS ok");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.True(result.GetNext().GetBoolean(0));
    }

    [Fact]
    public void ReturnIntervalListUnique_DeduplicatesNestedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN list_unique([[to_days(1)], [to_days(1)], [to_days(2)]]) AS vals");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("[[P1D],[P2D]]", result.GetNext().GetString(0));
    }

    [Fact]
    public void ReturnDistinctIntervalLists_DeduplicatesByValue()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "UNWIND [[to_days(1)], [to_days(1)], [to_days(2)]] AS vals " +
            "WITH DISTINCT vals " +
            "RETURN vals ORDER BY tostring(vals)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2UL, result.GetNumTuples());
        Assert.Equal("[P1D]", result.GetNext().GetString(0));
        Assert.Equal("[P2D]", result.GetNext().GetString(0));
    }

    [Fact]
    public void CountDistinctIntervalLists_CountsByValue()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "UNWIND [[to_days(1)], [to_days(1)], [to_days(2)]] AS vals " +
            "RETURN count(DISTINCT vals) AS n");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(2L, result.GetNext().GetInt64(0));
    }

    [Fact]
    public void GroupByIntervalLists_GroupsByValue()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "UNWIND [[to_days(1)], [to_days(1)], [to_days(2)]] AS vals " +
            "RETURN vals, count(*) AS n ORDER BY tostring(vals)");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2UL, result.GetNumTuples());

        var first = result.GetNext();
        Assert.Equal("[P1D]", first.GetString(0));
        Assert.Equal(2L, first.GetInt64(1));

        var second = result.GetNext();
        Assert.Equal("[P2D]", second.GetString(0));
        Assert.Equal(1L, second.GetInt64(1));
    }

    [Fact]
    public void OrderByIntervalLists_SortsByNormalizedValue()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "UNWIND [[to_days(2)], [to_days(1)]] AS vals " +
            "RETURN vals ORDER BY vals");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(2UL, result.GetNumTuples());
        Assert.Equal("[P1D]", result.GetNext().GetString(0));
        Assert.Equal("[P2D]", result.GetNext().GetString(0));
    }

    [Fact]
    public void OrderByIntervalListsWithLimit_UsesTopKByNormalizedValue()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "UNWIND [[to_days(2)], [to_days(1)]] AS vals " +
            "RETURN vals ORDER BY vals LIMIT 1");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("[P1D]", result.GetNext().GetString(0));
    }

    [Fact]
    public void OrderByNestedIntervalLists_SortsLexicographicallyByElements()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "UNWIND [[to_days(1), to_days(3)], [to_days(1), to_days(2)], [to_days(2)]] AS vals " +
            "RETURN vals ORDER BY vals");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(3UL, result.GetNumTuples());
        Assert.Equal("[P1D,P2D]", result.GetNext().GetString(0));
        Assert.Equal("[P1D,P3D]", result.GetNext().GetString(0));
        Assert.Equal("[P2D]", result.GetNext().GetString(0));
    }

    [Fact]
    public void OrderByIntervalLists_ExternalChunkedSort_SortsByNormalizedValue()
    {
        var originalChunkLimit = BogDb.Core.Processor.Operator.OrderBy.OrderBy.ChunkRowLimit;
        var originalChunkByteLimit = BogDb.Core.Processor.Operator.OrderBy.OrderBy.ChunkByteLimitOverride;
        BogDb.Core.Processor.Operator.OrderBy.OrderBy.ChunkRowLimit = 2;
        BogDb.Core.Processor.Operator.OrderBy.OrderBy.ChunkByteLimitOverride = null;

        try
        {
            using var db = BogDatabase.Open(":memory:");
            using var conn = new BogConnection(db);

            var result = conn.Query(
                "UNWIND [[to_days(3)], [to_days(1)], [to_days(4)], [to_days(2)]] AS vals " +
                "RETURN vals ORDER BY vals");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(4UL, result.GetNumTuples());
            Assert.Equal("[P1D]", result.GetNext().GetString(0));
            Assert.Equal("[P2D]", result.GetNext().GetString(0));
            Assert.Equal("[P3D]", result.GetNext().GetString(0));
            Assert.Equal("[P4D]", result.GetNext().GetString(0));
        }
        finally
        {
            BogDb.Core.Processor.Operator.OrderBy.OrderBy.ChunkRowLimit = originalChunkLimit;
            BogDb.Core.Processor.Operator.OrderBy.OrderBy.ChunkByteLimitOverride = originalChunkByteLimit;
        }
    }


    [Fact]
    public void ReturnIntervalDivide_Integer_CarriesRemaindersAcrossBuckets()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN interval('3 years 2 days 13 hours 2 minutes') / 3 AS delta, " +
            "date_part('year', interval('3 years 2 days 13 hours 2 minutes') / 3) AS y, " +
            "date_part('hour', interval('3 years 2 days 13 hours 2 minutes') / 3) AS h, " +
            "date_part('minute', interval('3 years 2 days 13 hours 2 minutes') / 3) AS m, " +
            "date_part('second', interval('3 years 2 days 13 hours 2 minutes') / 3) AS s");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("P1YT20H20M40S", row.GetString(0));
        Assert.Equal(1L, row.GetInt64(1));
        Assert.Equal(20L, row.GetInt64(2));
        Assert.Equal(20L, row.GetInt64(3));
        Assert.Equal(40L, row.GetInt64(4));
    }

    [Fact]
    public void ReturnNegativeIntervalDivide_Integer_PreservesSign()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN -interval('3 days 3 hours') / 3 AS delta, " +
            "date_part('day', -interval('3 days 3 hours') / 3) AS d, " +
            "date_part('hour', -interval('3 days 3 hours') / 3) AS h");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("-P1DT1H", row.GetString(0));
        Assert.Equal(-1L, row.GetInt64(1));
        Assert.Equal(-1L, row.GetInt64(2));
    }

    [Fact]
    public void ReturnIntervalExtendedNativeUnits_ParseSuccessfully()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN interval('3 quarter') AS quarter, " +
            "interval('3 decade') AS decade, " +
            "interval('3 century') AS century, " +
            "interval('3 millennium') AS millennium");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("P9M", row.GetString(0));
        Assert.Equal("P30Y", row.GetString(1));
        Assert.Equal("P300Y", row.GetString(2));
        Assert.Equal("P3000Y", row.GetString(3));
    }

    [Fact]
    public void ReturnIntervalFractionalMonthFamily_UsesNativeCarries()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN interval('1.5 month') AS month, " +
            "interval('1.5 year') AS year, " +
            "interval('1.5 quarter') AS quarter");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("P1M15D", row.GetString(0));
        Assert.Equal("P1Y6M", row.GetString(1));
        Assert.Equal("P4M15D", row.GetString(2));
    }

    [Fact]
    public void ReturnIntervalTimeLiteralAndAbbreviatedUnits_ParseSuccessfully()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query(
            "RETURN interval('35:10:00') AS time_only, " +
            "interval('1 year 12:00:00') AS trailing_time, " +
            "interval('2 yrs 3 mons 4 d 5 h 6 m 7 s 8 ms 9 us') AS abbreviated");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("P1DT11H10M", row.GetString(0));
        Assert.Equal("P1YT12H", row.GetString(1));
        Assert.Equal("P2Y3M4DT5H6M7.008009S", row.GetString(2));
    }
}
