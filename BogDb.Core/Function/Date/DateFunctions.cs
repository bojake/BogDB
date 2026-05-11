using System;
using System.Collections.Generic;
using System.Globalization;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Date;

/// <summary>
/// Date and timestamp scalar functions.
/// C++ parity: src/function/date_functions.cpp
/// </summary>
internal static class DateFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── Current time ───────────────────────────────────────────────────────
        r["now"]               = _ => (object?)DateTime.UtcNow.ToString("o");
        r["current_timestamp"] = r["now"];
        r["utc_timestamp"]     = r["now"];
        r["current_date"]      = _ => (object?)DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        r["today"]             = r["current_date"];
        r["tomorrow"]          = _ => (object?)DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd");
        r["yesterday"]         = _ => (object?)DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd");
        r["getdate"]           = r["now"];
        r["current_time"]      = _ => (object?)DateTime.UtcNow.ToString("HH:mm:ss");

        // ── Construction ───────────────────────────────────────────────────────
        r["date"]      = a => a.Length >= 1 ? ParseDate(TypeCoercionHelper.ToBogDbString(a[0])) : null;
        r["timestamp"] = a => a.Length >= 1 ? ParseTimestamp(TypeCoercionHelper.ToBogDbString(a[0])) : null;
        r["datetime"]  = r["timestamp"];
        r["to_date"]   = r["date"];
        r["to_timestamp"] = r["timestamp"];
        r["make_date"] = a => a.Length >= 3 ? MakeDate(a[0], a[1], a[2]) : null;
        r["make_timestamp"] = a => a.Length >= 3 ? MakeTimestamp(a) : null;

        // ── Extractors (date_part / extract) ───────────────────────────────────
        r["date_part"]  = a => a.Length >= 2 ? ExtractPart(TypeCoercionHelper.ToBogDbString(a[0]), a[1]) : null;
        r["extract"]    = r["date_part"];
        r["date_trunc"] = a => a.Length >= 2 ? TruncDate(TypeCoercionHelper.ToBogDbString(a[0]), a[1]) : null;

        // ── Component extractors ───────────────────────────────────────────────
        r["year"]        = a => Part("year", a);
        r["month"]       = a => Part("month", a);
        r["day"]         = a => Part("day", a);
        r["hour"]        = a => Part("hour", a);
        r["minute"]      = a => Part("minute", a);
        r["second"]      = a => Part("second", a);
        r["millisecond"] = a => Part("millisecond", a);
        r["microsecond"] = a => Part("microsecond", a);
        r["quarter"]     = a => Part("quarter", a);
        r["week"]        = a => Part("week", a);
        r["dayofweek"]   = a => Part("dow", a);
        r["dayofyear"]   = a => Part("doy", a);
        r["epoch"]       = a => Part("epoch", a);

        // ── Arithmetic ─────────────────────────────────────────────────────────
        r["date_add"]    = a => a.Length >= 3 ? DateAdd(TypeCoercionHelper.ToBogDbString(a[0]), a[1], TypeCoercionHelper.ToInt64(a[2])) : null;
        r["date_sub"]    = a => a.Length >= 3 ? DateAdd(TypeCoercionHelper.ToBogDbString(a[0]), a[1], -TypeCoercionHelper.ToInt64(a[2])) : null;
        r["date_diff"]   = a => a.Length >= 3 ? DateDiff(TypeCoercionHelper.ToBogDbString(a[0]), a[1], a[2]) : null;
        r["datediff"]    = r["date_diff"];
        r["timestamp_add"] = r["date_add"];
        r["timestamp_diff"] = r["date_diff"];

        // ── Epoch ──────────────────────────────────────────────────────────────
        r["epoch_ms"]    = a => a.Length >= 1 ? EpochMs(a[0]) : null;
        r["from_epoch_ms"] = a => a.Length >= 1
            ? (object?)DateTimeOffset.FromUnixTimeMilliseconds(TypeCoercionHelper.ToInt64(a[0])).UtcDateTime.ToString("o")
            : null;
        r["epoch_secs"]  = a => a.Length >= 1 ? (object?)(new DateTimeOffset(ParseDtOrNow(a[0])).ToUnixTimeSeconds()) : null;
        r["from_epoch"]  = a => a.Length >= 1
            ? (object?)DateTimeOffset.FromUnixTimeSeconds(TypeCoercionHelper.ToInt64(a[0])).UtcDateTime.ToString("o")
            : null;
        r["epoch_us"]    = a => a.Length >= 1 ? (object?)(new DateTimeOffset(ParseDtOrNow(a[0])).ToUnixTimeMilliseconds() * 1000L) : null;
        r["from_epoch_us"] = a => a.Length >= 1
            ? (object?)DateTimeOffset.FromUnixTimeMilliseconds(TypeCoercionHelper.ToInt64(a[0]) / 1000L).UtcDateTime.ToString("o")
            : null;
        r["strftime"]    = a => a.Length >= 2 ? (object?)ParseDtOrNow(a[1]).ToString(TypeCoercionHelper.ToBogDbString(a[0]) ?? "o") : null;
        r["date_format"] = r["strftime"];

        // ── C++ parity: additional date functions ─────────────────────────────
        r["century"] = a => a.Length >= 1
            ? (object?)(long)((ParseDtOrNow(a[0]).Year - 1) / 100 + 1) : null;

        r["dayname"] = a => a.Length >= 1
            ? (object?)ParseDtOrNow(a[0]).DayOfWeek.ToString() : null;

        r["monthname"] = a => a.Length >= 1
            ? (object?)CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(
                ParseDtOrNow(a[0]).Month) : null;

        r["last_day"] = a =>
        {
            if (a.Length < 1) return null;
            var dt = ParseDtOrNow(a[0]);
            var lastDay = new DateOnly(dt.Year, dt.Month,
                DateTime.DaysInMonth(dt.Year, dt.Month));
            return (object?)lastDay.ToString("yyyy-MM-dd");
        };

        // Aliases: datepart/datetrunc (no underscore variants)
        r["datepart"]  = r["date_part"];
        r["datetrunc"] = r["date_trunc"];
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static object? ParseDate(string? s)
    {
        if (s == null) return null;
        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM-dd");
        return null;
    }

    private static object? ParseTimestamp(string? s)
    {
        if (s == null) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToString("o");
        return null;
    }

    private static DateTime ParseDtOrNow(object? v)
    {
        var s = TypeCoercionHelper.ToBogDbString(v);
        if (s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        return DateTime.UtcNow;
    }

    private static object? EpochMs(object? value)
    {
        if (value == null) return null;

        if (value is DateTime dt)
            return new DateTimeOffset(dt).ToUnixTimeMilliseconds();
        if (value is DateTimeOffset dto)
            return dto.ToUnixTimeMilliseconds();
        if (value is string s)
        {
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed.ToUnixTimeMilliseconds();
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("o");
            return null;
        }

        try
        {
            var ms = TypeCoercionHelper.ToInt64(value);
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("o");
        }
        catch
        {
            return null;
        }
    }

    private static object? Part(string part, object?[] a)
        => a.Length >= 1 ? ExtractPart(part, a[0]) : null;

    private static object? ExtractPart(string? part, object? dateVal)
    {
        if (TypeCoercionHelper.TryParseInterval(dateVal, out var interval))
        {
            return (part?.ToLowerInvariant()) switch
            {
                "year" or "decade" or "century" or "millennium" or "month" or "quarter" or "week" or "day" or "hour" or "minute" or "second" or "millisecond" or "microsecond"
                    => (object?)interval.GetPart(part),
                _ => null
            };
        }

        var dt = ParseDtOrNow(dateVal);
        return (part?.ToLowerInvariant()) switch
        {
            "year"        => (object?)(long)dt.Year,
            "month"       => (long)dt.Month,
            "day"         => (long)dt.Day,
            "hour"        => (long)dt.Hour,
            "minute"      => (long)dt.Minute,
            "second"      => (long)dt.Second,
            "millisecond" => (long)dt.Millisecond,
            "microsecond" => (long)(dt.Ticks % TimeSpan.TicksPerSecond / (TimeSpan.TicksPerMillisecond / 1000)),
            "quarter"     => (long)((dt.Month - 1) / 3 + 1),
            "week"        => (long)CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(dt, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday),
            "dow"         => (long)dt.DayOfWeek,
            "doy"         => (long)dt.DayOfYear,
            "epoch"       => new DateTimeOffset(dt).ToUnixTimeSeconds(),
            _             => null
        };
    }

    private static object? TruncDate(string? part, object? dateVal)
    {
        var dt = ParseDtOrNow(dateVal);
        dt = (part?.ToLowerInvariant()) switch
        {
            "year"    => new DateTime(dt.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "month"   => new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            "day"     => dt.Date,
            "hour"    => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, DateTimeKind.Utc),
            "minute"  => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, DateTimeKind.Utc),
            _         => dt
        };
        return (object?)dt.ToString("o");
    }

    private static object? DateAdd(string? part, object? dateVal, long n)
    {
        var dt = ParseDtOrNow(dateVal);
        dt = (part?.ToLowerInvariant()) switch
        {
            "year"   => dt.AddYears((int)n),
            "month"  => dt.AddMonths((int)n),
            "day"    => dt.AddDays(n),
            "hour"   => dt.AddHours(n),
            "minute" => dt.AddMinutes(n),
            "second" => dt.AddSeconds(n),
            _        => dt
        };
        return (object?)dt.ToString("o");
    }

    private static object? DateDiff(string? part, object? a, object? b)
    {
        var dtA = ParseDtOrNow(a); var dtB = ParseDtOrNow(b);
        var span = dtB - dtA;
        return (part?.ToLowerInvariant()) switch
        {
            "year"   => (object?)(long)(dtB.Year - dtA.Year),
            "month"  => (long)((dtB.Year - dtA.Year) * 12 + dtB.Month - dtA.Month),
            "day"    => (long)span.TotalDays,
            "hour"   => (long)span.TotalHours,
            "minute" => (long)span.TotalMinutes,
            "second" => (long)span.TotalSeconds,
            _        => null
        };
    }

    private static object? MakeDate(object? y, object? m, object? d)
    {
        try
        {
            var year = (int)TypeCoercionHelper.ToInt64(y);
            var month = (int)TypeCoercionHelper.ToInt64(m);
            var day = (int)TypeCoercionHelper.ToInt64(d);
            return new DateOnly(year, month, day).ToString("yyyy-MM-dd");
        }
        catch
        {
            return null;
        }
    }

    private static object? MakeTimestamp(object?[] a)
    {
        try
        {
            var year = (int)TypeCoercionHelper.ToInt64(a[0]);
            var month = (int)TypeCoercionHelper.ToInt64(a[1]);
            var day = (int)TypeCoercionHelper.ToInt64(a[2]);
            var hour = a.Length >= 4 ? (int)TypeCoercionHelper.ToInt64(a[3]) : 0;
            var minute = a.Length >= 5 ? (int)TypeCoercionHelper.ToInt64(a[4]) : 0;
            var second = a.Length >= 6 ? (int)TypeCoercionHelper.ToInt64(a[5]) : 0;
            var dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            return (object?)dt.ToString("o");
        }
        catch
        {
            return null;
        }
    }
}
