using System;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Timestamp;

/// <summary>
/// Timestamp scalar functions.
/// In BogDB, timestamps are stored as DateTimeOffset or as ISO-8601 strings.
///
/// C++ parity: src/function/timestamp/ (to_epoch_ms.cpp)
///             Also covers functions not yet in DateFunctions.
/// </summary>
internal static class TimestampFunctions
{
    // Unix epoch reference
    private static readonly DateTimeOffset Epoch =
        new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── Epoch conversions ─────────────────────────────────────────────────

        // to_epoch_ms(ts) — timestamp → milliseconds since Unix epoch
        r["to_epoch_ms"] = r["epoch_ms_from_timestamp"] = a =>
        {
            if (a.Length < 1) return null;
            var dto = ParseTimestamp(a[0]);
            return dto.HasValue ? (object?)dto.Value.ToUnixTimeMilliseconds() : null;
        };

        // epoch_ms(ms) — ms since epoch → timestamp string
        r["ms_to_timestamp"] = a =>
        {
            if (a.Length < 1) return null;
            try
            {
                var ms = TypeCoercionHelper.ToInt64(a[0]);
                return (object?)DateTimeOffset.FromUnixTimeMilliseconds(ms)
                    .ToString("yyyy-MM-dd HH:mm:ss.fff");
            }
            catch { return null; }
        };

        if (!r.ContainsKey("epoch_ms"))
            r["epoch_ms"] = r["ms_to_timestamp"];

        // to_epoch_us(ts) — timestamp → microseconds since Unix epoch
        r["to_epoch_us"] = r["epoch_us_from_timestamp"] = a =>
        {
            if (a.Length < 1) return null;
            var dto = ParseTimestamp(a[0]);
            return dto.HasValue
                ? (object?)((dto.Value - Epoch).Ticks / 10)   // 100-ns ticks / 10 = µs
                : null;
        };

        // epoch_us(us) — µs since epoch → timestamp
        r["us_to_timestamp"] = a =>
        {
            if (a.Length < 1) return null;
            try
            {
                var us = TypeCoercionHelper.ToInt64(a[0]);
                return (object?)(Epoch + TimeSpan.FromTicks(us * 10))
                    .ToString("yyyy-MM-dd HH:mm:ss.ffffff");
            }
            catch { return null; }
        };

        if (!r.ContainsKey("epoch_us"))
            r["epoch_us"] = r["us_to_timestamp"];

        // to_epoch_s(ts) — timestamp → seconds since Unix epoch
        r["to_epoch_s"] = r["epoch_s_from_timestamp"] = a =>
        {
            if (a.Length < 1) return null;
            var dto = ParseTimestamp(a[0]);
            return dto.HasValue ? (object?)dto.Value.ToUnixTimeSeconds() : null;
        };

        // epoch_s(s) → timestamp
        r["epoch_s"] = a =>
        {
            if (a.Length < 1) return null;
            try
            {
                var s = TypeCoercionHelper.ToInt64(a[0]);
                return (object?)DateTimeOffset.FromUnixTimeSeconds(s)
                    .ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { return null; }
        };

        // ── Timestamp constructors ────────────────────────────────────────────

        r["timestamp"] = r["to_timestamp"] = a =>
        {
            if (a.Length < 1) return null;
            if (a[0] is string s)
                return (object?)s; // pass-through

            // Numeric → epoch seconds interpretation
            try
            {
                var sec = TypeCoercionHelper.ToInt64(a[0]);
                return (object?)DateTimeOffset.FromUnixTimeSeconds(sec)
                    .ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { return null; }
        };

        // ── Timestamp arithmetic ──────────────────────────────────────────────

        r["timestamp_add"] = a =>
        {
            if (a.Length < 3) return null;
            var unit = TypeCoercionHelper.ToBogDbString(a[0])?.ToLowerInvariant();
            var dto = ParseTimestamp(a[1]); if (!dto.HasValue) return null;
            var n    = TypeCoercionHelper.ToInt64(a[2]);
            var result = unit switch
            {
                "year" or "years"   => dto.Value.AddYears((int)n),
                "month" or "months" => dto.Value.AddMonths((int)n),
                "day" or "days"     => dto.Value.AddDays(n),
                "hour" or "hours"   => dto.Value.AddHours(n),
                "minute" or "minutes" => dto.Value.AddMinutes(n),
                "second" or "seconds" => dto.Value.AddSeconds(n),
                "millisecond" or "milliseconds" => dto.Value.AddMilliseconds(n),
                _ => dto.Value.AddDays(n)
            };
            return (object?)result.ToString("yyyy-MM-dd HH:mm:ss");
        };

        r["timestamp_diff"] = a =>
        {
            if (a.Length < 3) return null;
            var t1 = ParseTimestamp(a[1]); var t2 = ParseTimestamp(a[2]);
            if (!t1.HasValue || !t2.HasValue) return null;
            var unit = TypeCoercionHelper.ToBogDbString(a[0])?.ToLowerInvariant();
            var diff = t1.Value - t2.Value;
            return (object?)(long)(unit switch
            {
                "year" or "years"   => diff.TotalDays / 365.25,
                "month" or "months" => diff.TotalDays / 30.44,
                "day" or "days"     => diff.TotalDays,
                "hour" or "hours"   => diff.TotalHours,
                "minute" or "minutes" => diff.TotalMinutes,
                "second" or "seconds" => diff.TotalSeconds,
                "millisecond" or "milliseconds" => diff.TotalMilliseconds,
                _ => diff.TotalSeconds
            });
        };

        // ── Parts extraction ──────────────────────────────────────────────────

        r["timestamp_year"]    = a => Extract(a, dto => (long)dto.Year);
        r["timestamp_month"]   = a => Extract(a, dto => (long)dto.Month);
        r["timestamp_day"]     = a => Extract(a, dto => (long)dto.Day);
        r["timestamp_hour"]    = a => Extract(a, dto => (long)dto.Hour);
        r["timestamp_minute"]  = a => Extract(a, dto => (long)dto.Minute);
        r["timestamp_second"]  = a => Extract(a, dto => (long)dto.Second);
        r["timestamp_millisecond"] = a => Extract(a, dto => (long)dto.Millisecond);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal static DateTimeOffset? ParseTimestamp(object? value)
    {
        if (value == null) return null;
        if (value is DateTimeOffset dto) return dto;
        if (value is DateTime dt) return new DateTimeOffset(dt, TimeSpan.Zero);
        if (value is string s)
        {
            // Prefer explicit UTC parse. If the string already has +/- offset or Z, respect it.
            // Otherwise treat as UTC (matching C++ BogDb timestamp semantics).
            if (DateTimeOffset.TryParse(s, null,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var parsed))
                return parsed;
        }
        if (value is long l) return DateTimeOffset.FromUnixTimeSeconds(l);
        return null;
    }

    private static object? Extract(object?[] a, Func<DateTimeOffset, long> selector)
    {
        if (a.Length < 1) return null;
        var dto = ParseTimestamp(a[0]);
        return dto.HasValue ? (object?)selector(dto.Value) : null;
    }
}
