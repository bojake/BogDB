using BogDb.Core.Common;
using BogDb.Core.Function;
using Xunit;

namespace BogDb.Tests.Function;

public sealed class IntervalFunctionTests
{
    [Fact]
    public void Interval_ParsesWordDuration()
    {
        var interval = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["3 years 2 days"])!);
        Assert.Equal("P3Y2D", interval.ToString());
    }

    [Fact]
    public void ToYears_ReturnsIntervalValue()
    {
        var interval = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("to_years", [2L])!);
        Assert.Equal("P2Y", interval.ToString());
    }

    [Fact]
    public void DatePart_ExtractsIntervalComponent()
    {
        var years = FunctionDispatcher.Invoke("date_part", ["year", FunctionDispatcher.Invoke("to_years", [4L])!]);
        Assert.Equal(4L, years);
    }

    [Fact]
    public void DatePart_ExtractsIntervalQuarterAndWeek()
    {
        var quarters = FunctionDispatcher.Invoke("date_part", ["quarter", FunctionDispatcher.Invoke("to_months", [14L])!]);
        var weeks = FunctionDispatcher.Invoke("date_part", ["week", FunctionDispatcher.Invoke("to_days", [15L])!]);
        Assert.Equal(4L, quarters);
        Assert.Equal(2L, weeks);
    }

    [Fact]
    public void DatePart_ExtractsExtendedIntervalCalendarBuckets()
    {
        var value = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["1234 years 5 months"])!);
        Assert.Equal(1234L, FunctionDispatcher.Invoke("date_part", ["year", value]));
        Assert.Equal(123L, FunctionDispatcher.Invoke("date_part", ["decade", value]));
        Assert.Equal(12L, FunctionDispatcher.Invoke("date_part", ["century", value]));
        Assert.Equal(1L, FunctionDispatcher.Invoke("date_part", ["millennium", value]));
        Assert.Equal(5L, FunctionDispatcher.Invoke("date_part", ["month", value]));
    }

    [Fact]
    public void IntervalCompareTo_UsesNativeOrderingNormalization()
    {
        var month = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["1 month"])!);
        var thirtyDays = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["30 days"])!);
        var thirtyOneDays = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["31 days"])!);
        var thirtyDaysAndHour = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["30 days 1 hour"])!);

        Assert.Equal(0, month.CompareTo(thirtyDays));
        Assert.True(month.CompareTo(thirtyOneDays) < 0);
        Assert.True(thirtyDaysAndHour.CompareTo(month) > 0);
    }

    [Fact]
    public void DatePlusInterval_ReturnsShiftedDate()
    {
        var shifted = FunctionDispatcher.Invoke("+", ["2024-01-01", FunctionDispatcher.Invoke("interval", ["3 days"])!]);
        Assert.Equal("2024-01-04", shifted);
    }

    [Fact]
    public void TimestampMinusTimestamp_ReturnsInterval()
    {
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("-", ["2024-01-03T00:00:00Z", "2024-01-01T00:00:00Z"])!);
        Assert.Equal("P2D", delta.ToString());
    }

    [Fact]
    public void TimeOnlyInterval_StringifiesWholeDaysAndRemainder()
    {
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("to_hours", [49L])!);
        Assert.Equal("P2DT1H", delta.ToString());
    }

    [Fact]
    public void MixedDayTimeInterval_CanonicalizesAcrossDayBoundary()
    {
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("-", [
            FunctionDispatcher.Invoke("to_days", [1L])!,
            FunctionDispatcher.Invoke("to_hours", [1L])!])!);
        Assert.Equal("PT23H", delta.ToString());
        Assert.Equal(0L, FunctionDispatcher.Invoke("date_part", ["day", delta]));
        Assert.Equal(23L, FunctionDispatcher.Invoke("date_part", ["hour", delta]));
    }

    [Fact]
    public void MixedMonthAndDaySigns_StringifyAsSignedComponents()
    {
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("-", [
            FunctionDispatcher.Invoke("to_months", [1L])!,
            FunctionDispatcher.Invoke("to_days", [1L])!])!);
        Assert.Equal("1 month -1 day", delta.ToString());
        Assert.Equal(1L, FunctionDispatcher.Invoke("date_part", ["month", delta]));
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["day", delta]));
    }

    [Fact]
    public void UnaryNegate_ProducesNegativeInterval()
    {
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("-", [FunctionDispatcher.Invoke("to_days", [2L])!])!);
        Assert.Equal("-P2D", delta.ToString());
        Assert.Equal(-2L, FunctionDispatcher.Invoke("date_part", ["day", delta]));
    }

    [Fact]
    public void NegativeTimeOnlyInterval_PreservesSignedTimeParts()
    {
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("-", [FunctionDispatcher.Invoke("to_hours", [2L])!])!);
        Assert.Equal("-PT2H", delta.ToString());
        Assert.Equal(-2L, FunctionDispatcher.Invoke("date_part", ["hour", delta]));
        Assert.Equal(0L, FunctionDispatcher.Invoke("date_part", ["minute", delta]));
    }

    [Fact]
    public void NegativeCrossDayInterval_PreservesSignedRemainderParts()
    {
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["-25 hours -30 minutes"])!);
        Assert.Equal("-P1DT1H30M", delta.ToString());
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["day", delta]));
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["hour", delta]));
        Assert.Equal(-30L, FunctionDispatcher.Invoke("date_part", ["minute", delta]));
    }

    [Fact]
    public void UnaryNegate_ComposedIntervalExpression_PreservesCrossDayRemainderParts()
    {
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("-", [
            FunctionDispatcher.Invoke("+", [
                FunctionDispatcher.Invoke("to_hours", [25L])!,
                FunctionDispatcher.Invoke("to_minutes", [30L])!])!])!);
        Assert.Equal("-P1DT1H30M", delta.ToString());
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["day", delta]));
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["hour", delta]));
        Assert.Equal(-30L, FunctionDispatcher.Invoke("date_part", ["minute", delta]));
    }

    [Fact]
    public void Typeof_Interval_IsInterval()
    {
        var kind = FunctionDispatcher.Invoke("typeof", [FunctionDispatcher.Invoke("to_days", [5L])!]);
        Assert.Equal("INTERVAL", kind);
    }

    // ── G-010: Sub-hour / sub-minute normalization ──────────────────────────────

    [Fact]
    public void ToMinutes_NormalizesAcrossHourBoundary()
    {
        // 70 minutes = 1 hour + 10 minutes; the engine canonicalizes across the hour boundary.
        var v = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("to_minutes", [70L])!);
        Assert.Equal("PT1H10M", v.ToString());
        Assert.Equal(1L,  FunctionDispatcher.Invoke("date_part", ["hour",   v]));
        Assert.Equal(10L, FunctionDispatcher.Invoke("date_part", ["minute", v]));
    }

    [Fact]
    public void ToMilliseconds_NormalizesAcrossSecondBoundary()
    {
        // 1500 ms = 1 second + 500 ms.
        var v = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("to_milliseconds", [1500L])!);
        Assert.Equal("PT1.5S", v.ToString());
        Assert.Equal(1L,   FunctionDispatcher.Invoke("date_part", ["second",      v]));
        Assert.Equal(500L, FunctionDispatcher.Invoke("date_part", ["millisecond", v]));
    }

    // ── G-010: interval + interval arithmetic ───────────────────────────────────

    [Fact]
    public void IntervalPlusInterval_CombinesSameComponent()
    {
        // to_days(2) + to_days(3) = P5D
        var sum = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("+",
            [FunctionDispatcher.Invoke("to_days", [2L])!, FunctionDispatcher.Invoke("to_days", [3L])!])!);
        Assert.Equal("P5D", sum.ToString());
        Assert.Equal(5L, FunctionDispatcher.Invoke("date_part", ["day", sum]));
    }

    [Fact]
    public void IntervalPlusInterval_CombinesMixedComponents()
    {
        // to_months(1) + to_days(15) = P1M15D
        var sum = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("+",
            [FunctionDispatcher.Invoke("to_months", [1L])!, FunctionDispatcher.Invoke("to_days", [15L])!])!);
        Assert.Equal("P1M15D", sum.ToString());
        Assert.Equal(1L,  FunctionDispatcher.Invoke("date_part", ["month", sum]));
        Assert.Equal(15L, FunctionDispatcher.Invoke("date_part", ["day",   sum]));
    }

    [Fact]
    public void IntervalPlusInterval_TimeComponentsAggregateAndNormalize()
    {
        // to_hours(23) + to_hours(2) = 25 hours = P1DT1H
        var sum = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("+",
            [FunctionDispatcher.Invoke("to_hours", [23L])!, FunctionDispatcher.Invoke("to_hours", [2L])!])!);
        Assert.Equal("P1DT1H", sum.ToString());
        Assert.Equal(1L, FunctionDispatcher.Invoke("date_part", ["day",  sum]));
        Assert.Equal(1L, FunctionDispatcher.Invoke("date_part", ["hour", sum]));
    }

    // ── G-010: date - interval ──────────────────────────────────────────────────

    [Fact]
    public void DateMinusInterval_ShiftsDateBackward()
    {
        // date('2024-01-10') - interval('3 days') = '2024-01-07'
        var result = FunctionDispatcher.Invoke("-",
            ["2024-01-10", FunctionDispatcher.Invoke("interval", ["3 days"])!]);
        Assert.Equal("2024-01-07", result);
    }

    // ── G-010: reverse mixed-sign (negative month, positive day) ────────────────

    [Fact]
    public void ReverseMixedSign_NegativeMonthPositiveDay_StringifiesAsSignedComponents()
    {
        // to_days(5) - to_months(1) → "-1 month 5 days"
        // This is the complement of the existing MixedMonthAndDaySigns test.
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("-",
            [FunctionDispatcher.Invoke("to_days", [5L])!, FunctionDispatcher.Invoke("to_months", [1L])!])!);
        Assert.Equal("-1 month 5 days", delta.ToString());
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["month", delta]));
        Assert.Equal(5L,  FunctionDispatcher.Invoke("date_part", ["day",   delta]));
    }

    // ── G-010: zero interval ────────────────────────────────────────────────────

    [Fact]
    public void ZeroInterval_StringifiesAsIso()
    {
        // to_seconds(0) must produce a valid zero-duration ISO string, not throw.
        var v = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("to_seconds", [0L])!);
        Assert.Equal("PT0S", v.ToString());
    }

    // ── G-010: ISO-8601 string parsing ──────────────────────────────────────────

    [Fact]
    public void Interval_ParsesIsoDurationString()
    {
        // interval('P1Y2M3DT4H5M6S') must round-trip through the ISO designator form.
        var v = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["P1Y2M3DT4H5M6S"])!);
        Assert.Equal("P1Y2M3DT4H5M6S", v.ToString());
        Assert.Equal(1L, FunctionDispatcher.Invoke("date_part", ["year",   v]));
        Assert.Equal(2L, FunctionDispatcher.Invoke("date_part", ["month",  v]));
        Assert.Equal(3L, FunctionDispatcher.Invoke("date_part", ["day",    v]));
        Assert.Equal(4L, FunctionDispatcher.Invoke("date_part", ["hour",   v]));
        Assert.Equal(5L, FunctionDispatcher.Invoke("date_part", ["minute", v]));
        Assert.Equal(6L, FunctionDispatcher.Invoke("date_part", ["second", v]));
    }

    [Fact]
    public void ToSeconds_NormalizesAcrossMinuteBoundary()
    {
        var v = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("to_seconds", [90L])!);
        Assert.Equal("PT1M30S", v.ToString());
        Assert.Equal(1L,  FunctionDispatcher.Invoke("date_part", ["minute", v]));
        Assert.Equal(30L, FunctionDispatcher.Invoke("date_part", ["second", v]));
    }

    [Fact]
    public void Interval_ParsesNegativeFractionalMinutes()
    {
        var v = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["-1.5 minutes"])!);
        Assert.Equal("-PT1M30S", v.ToString());
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["minute", v]));
        Assert.Equal(-30L, FunctionDispatcher.Invoke("date_part", ["second", v]));
    }

    [Fact]
    public void MixedSign_MonthDayTime_NormalizesAcrossAllBuckets()
    {
        var delta = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("-",
            [
                FunctionDispatcher.Invoke("-",
                    [FunctionDispatcher.Invoke("to_months", [1L])!, FunctionDispatcher.Invoke("to_days", [1L])!])!,
                FunctionDispatcher.Invoke("to_hours", [25L])!
            ])!);
        Assert.Equal("1 month -2 days -1 hour", delta.ToString());
        Assert.Equal(1L, FunctionDispatcher.Invoke("date_part", ["month", delta]));
        Assert.Equal(-2L, FunctionDispatcher.Invoke("date_part", ["day", delta]));
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["hour", delta]));
    }

    [Fact]
    public void IntervalDivide_Integer_CarriesRemaindersAcrossBuckets()
    {
        var value = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("/",
            [FunctionDispatcher.Invoke("interval", ["3 years 2 days 13 hours 2 minutes"])!, 3L])!);
        Assert.Equal("P1YT20H20M40S", value.ToString());
        Assert.Equal(1L, FunctionDispatcher.Invoke("date_part", ["year", value]));
        Assert.Equal(20L, FunctionDispatcher.Invoke("date_part", ["hour", value]));
        Assert.Equal(20L, FunctionDispatcher.Invoke("date_part", ["minute", value]));
        Assert.Equal(40L, FunctionDispatcher.Invoke("date_part", ["second", value]));
    }

    [Fact]
    public void IntervalDivide_NegativeInterval_PreservesSign()
    {
        var value = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("/",
            [FunctionDispatcher.Invoke("-", [FunctionDispatcher.Invoke("interval", ["3 days 3 hours"])!])!, 3L])!);
        Assert.Equal("-P1DT1H", value.ToString());
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["day", value]));
        Assert.Equal(-1L, FunctionDispatcher.Invoke("date_part", ["hour", value]));
    }

    [Fact]
    public void Interval_ParsesExtendedNativeUnits()
    {
        Assert.Equal("P9M", Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["3 quarter"])!).ToString());
        Assert.Equal("P30Y", Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["3 decade"])!).ToString());
        Assert.Equal("P300Y", Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["3 century"])!).ToString());
        Assert.Equal("P3000Y", Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["3 millennium"])!).ToString());
    }

    [Fact]
    public void Interval_ParsesFractionalMonthFamilyWithNativeCarries()
    {
        Assert.Equal("P1M15D", Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["1.5 month"])!).ToString());
        Assert.Equal("P1Y6M", Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["1.5 year"])!).ToString());
        Assert.Equal("P4M15D", Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["1.5 quarter"])!).ToString());
    }

    [Fact]
    public void Interval_ParsesTimeLiteralAndTrailingTimeLiteral()
    {
        Assert.Equal("P1DT11H10M", Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["35:10:00"])!).ToString());
        Assert.Equal("P1YT12H", Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["1 year 12:00:00"])!).ToString());
    }

    [Fact]
    public void Interval_ParsesAbbreviatedNativeUnits()
    {
        var interval = Assert.IsType<BogDbInterval>(FunctionDispatcher.Invoke("interval", ["2 yrs 3 mons 4 d 5 h 6 m 7 s 8 ms 9 us"])!);
        Assert.Equal("P2Y3M4DT5H6M7.008009S", interval.ToString());
    }
}
