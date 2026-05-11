using System;
using System.Collections.Generic;
using System.Globalization;
using BogDb.Core.Common;
using BogDb.Core.Function.Timestamp;
using SysMath = global::System.Math;

namespace BogDb.Core.Function.Arithmetic;

/// <summary>
/// Arithmetic and trigonometric scalar functions.
/// C++ parity: src/function/arithmetic_functions.cpp
/// </summary>
internal static class ArithmeticFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── Binary arithmetic ──────────────────────────────────────────────────
        r["+"]       = a => Add(a);
        r["-"]       = a => a.Length == 1 ? UnaryNegate(a[0]) : Subtract(a);
        r["*"]       = a => Mul(a);
        r["/"]       = a => Div(a);
        r["%"]       = a => Mod(a);
        r["mod"]     = r["%"];
        r["^"]       = a => Pow2(a);
        r["pow"]     = r["^"];
        r["power"]   = r["^"];

        // ── Rounding ───────────────────────────────────────────────────────────
        r["abs"]     = a => UnaryNum(a, SysMath.Abs, SysMath.Abs);
        r["ceil"]    = a => a.Length >= 1 ? (object?)SysMath.Ceiling(ToD(a[0])) : null;
        r["ceiling"] = r["ceil"];
        r["floor"]   = a => a.Length >= 1 ? (object?)SysMath.Floor(ToD(a[0])) : null;
        r["round"]   = a => a.Length >= 1 ? (object?)SysMath.Round(ToD(a[0]), a.Length >= 2 ? (int)ToL(a[1]) : 0) : null;
        r["sign"]    = a => a.Length >= 1 ? (object?)(long)SysMath.Sign(ToD(a[0])) : null;
        r["even"]    = a => a.Length >= 1 ? (object?)(2L * (long)SysMath.Floor(ToD(a[0]) / 2.0)) : null;

        // ── Roots & powers ─────────────────────────────────────────────────────
        r["sqrt"]    = a => a.Length >= 1 ? (object?)SysMath.Sqrt(ToD(a[0])) : null;
        r["cbrt"]    = a => a.Length >= 1 ? (object?)SysMath.Cbrt(ToD(a[0])) : null;
        r["exp"]     = a => a.Length >= 1 ? (object?)SysMath.Exp(ToD(a[0])) : null;
        r["square"]  = a => a.Length >= 1 ? (object?)SysMath.Pow(ToD(a[0]), 2) : null;

        // ── Logarithms ─────────────────────────────────────────────────────────
        r["log"]     = a => a.Length >= 1 ? (object?)SysMath.Log10(ToD(a[0])) : null; // BogDb log = log10
        r["log2"]    = a => a.Length >= 1 ? (object?)SysMath.Log2(ToD(a[0])) : null;
        r["log10"]   = a => a.Length >= 1 ? (object?)SysMath.Log10(ToD(a[0])) : null;
        r["ln"]      = a => a.Length >= 1 ? (object?)SysMath.Log(ToD(a[0])) : null;

        // ── Constants ──────────────────────────────────────────────────────────
        r["pi"]      = _ => (object?)SysMath.PI;
        r["e"]       = _ => (object?)SysMath.E;

        // ── Trigonometry ───────────────────────────────────────────────────────
        r["sin"]     = a => a.Length >= 1 ? (object?)SysMath.Sin(ToD(a[0])) : null;
        r["cos"]     = a => a.Length >= 1 ? (object?)SysMath.Cos(ToD(a[0])) : null;
        r["tan"]     = a => a.Length >= 1 ? (object?)SysMath.Tan(ToD(a[0])) : null;
        r["asin"]    = a => a.Length >= 1 ? (object?)SysMath.Asin(ToD(a[0])) : null;
        r["acos"]    = a => a.Length >= 1 ? (object?)SysMath.Acos(ToD(a[0])) : null;
        r["atan"]    = a => a.Length >= 1 ? (object?)SysMath.Atan(ToD(a[0])) : null;
        r["atan2"]   = a => a.Length >= 2 ? (object?)SysMath.Atan2(ToD(a[0]), ToD(a[1])) : null;
        r["cot"]     = a => a.Length >= 1 ? (object?)(1.0 / SysMath.Tan(ToD(a[0]))) : null;

        // ── Angle conversion ───────────────────────────────────────────────────
        r["degrees"] = a => a.Length >= 1 ? (object?)(ToD(a[0]) * 180.0 / SysMath.PI) : null;
        r["radians"] = a => a.Length >= 1 ? (object?)(ToD(a[0]) * SysMath.PI / 180.0) : null;

        // ── Float predicates ───────────────────────────────────────────────────
        r["isinf"]    = a => a.Length >= 1 ? (object?)double.IsInfinity(ToD(a[0])) : null;
        r["isnan"]    = a => a.Length >= 1 ? (object?)double.IsNaN(ToD(a[0])) : null;
        r["isfinite"] = a => a.Length >= 1 ? (object?)double.IsFinite(ToD(a[0])) : null;

        // ── Number constants ───────────────────────────────────────────────────
        r["infinity"] = _ => (object?)double.PositiveInfinity;
        r["nan"]      = _ => (object?)double.NaN;

        // ── Bitwise operators ─────────────────────────────────────────────────
        // C++ parity: src/function/arithmetic/ bitwise_* and bitshift_*
        r["bitwise_and"]   = a => a.Length >= 2 ? (object?)(ToL(a[0]) & ToL(a[1])) : null;
        r["bitwise_or"]    = a => a.Length >= 2 ? (object?)(ToL(a[0]) | ToL(a[1])) : null;
        r["bitwise_xor"]   = a => a.Length >= 2 ? (object?)(ToL(a[0]) ^ ToL(a[1])) : null;
        r["bitshift_left"]  = a => a.Length >= 2 ? (object?)(ToL(a[0]) << (int)ToL(a[1])) : null;
        r["bitshift_right"] = a => a.Length >= 2 ? (object?)(ToL(a[0]) >> (int)ToL(a[1])) : null;
        r["&"]  = r["bitwise_and"];
        r["|"]  = r["bitwise_or"];
        r["<<"] = r["bitshift_left"];
        r[">>"] = r["bitshift_right"];
        r["~"]  = a => a.Length >= 1 ? (object?)(~ToL(a[0])) : null;  // bitwise NOT

        // ── Negate (unary) ────────────────────────────────────────────────────
        r["negate"] = a => a.Length >= 1 ? UnaryNegate(a[0]) : null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static object? Add(object?[] a)
    {
        if (a.Length < 2) return null;
        if (TypeCoercionHelper.TryParseInterval(a[0], out var leftIntervalBoth) &&
            TypeCoercionHelper.TryParseInterval(a[1], out var rightIntervalBoth))
            return leftIntervalBoth + rightIntervalBoth;
        if (TryParseDate(a[0], out var leftDate) &&
            TypeCoercionHelper.TryParseInterval(a[1], out var rightIntervalForDate))
            return rightIntervalForDate.ApplyToDate(leftDate).ToString("yyyy-MM-dd");
        if (TypeCoercionHelper.TryParseInterval(a[0], out var leftIntervalForDate) &&
            TryParseDate(a[1], out var rightDate))
            return leftIntervalForDate.ApplyToDate(rightDate).ToString("yyyy-MM-dd");
        if (TryParseTimestamp(a[0], out var leftTimestamp) &&
            TypeCoercionHelper.TryParseInterval(a[1], out var rightIntervalForTimestamp))
            return rightIntervalForTimestamp.ApplyToTimestamp(leftTimestamp).ToString("o");
        if (TypeCoercionHelper.TryParseInterval(a[0], out var leftIntervalForTimestamp) &&
            TryParseTimestamp(a[1], out var rightTimestamp))
            return leftIntervalForTimestamp.ApplyToTimestamp(rightTimestamp).ToString("o");
        if (a[0] is string || a[1] is string)
            return $"{a[0]}{a[1]}";
        if (a[0] is long la && a[1] is long lb) return la + lb;
        return ToD(a[0]) + ToD(a[1]);
    }

    private static object? Subtract(object?[] a)
    {
        if (a.Length < 2) return null;
        if (TypeCoercionHelper.TryParseInterval(a[0], out var leftIntervalBoth) &&
            TypeCoercionHelper.TryParseInterval(a[1], out var rightIntervalBoth))
            return leftIntervalBoth - rightIntervalBoth;
        if (TryParseDate(a[0], out var leftDate) &&
            TypeCoercionHelper.TryParseInterval(a[1], out var rightIntervalForDate))
            return Negate(rightIntervalForDate)
                .ApplyToDate(leftDate)
                .ToString("yyyy-MM-dd");
        if (TryParseDate(a[0], out leftDate) &&
            TryParseDate(a[1], out var rightDate))
            return BogDbInterval.FromDays(leftDate.DayNumber - rightDate.DayNumber);
        if (TryParseTimestamp(a[0], out var leftTimestamp) &&
            TypeCoercionHelper.TryParseInterval(a[1], out var rightIntervalForTimestamp))
            return Negate(rightIntervalForTimestamp)
                .ApplyToTimestamp(leftTimestamp)
                .ToString("o");
        if (TryParseTimestamp(a[0], out leftTimestamp) &&
            TryParseTimestamp(a[1], out var rightTimestamp))
            return BogDbInterval.FromMicroseconds((leftTimestamp - rightTimestamp).Ticks / 10d);
        if (a[0] is long la && a[1] is long lb) return la - lb;
        return ToD(a[0]) - ToD(a[1]);
    }

    private static object? Mul(object?[] a)
    {
        if (a.Length < 2) return null;
        if (a[0] is long la && a[1] is long lb) return la * lb;
        return ToD(a[0]) * ToD(a[1]);
    }

    private static object? Div(object?[] a)
    {
        if (a.Length < 2) return null;
        if (TypeCoercionHelper.TryParseInterval(a[0], out var interval))
        {
            var divisor = TypeCoercionHelper.ToInt64(a[1]);
            return interval.Divide(divisor);
        }
        if (a[0] is long la && a[1] is long lb)
            return lb != 0 ? (object?)(la / lb) : throw new DivideByZeroException();
        var dv = ToD(a[1]);
        return dv != 0.0 ? (object?)(ToD(a[0]) / dv) : double.NaN;
    }

    private static object? Mod(object?[] a)
    {
        if (a.Length < 2) return null;
        if (a[0] is long la && a[1] is long lb) return la % lb;
        return ToD(a[0]) % ToD(a[1]);
    }

    private static object? Pow2(object?[] a)
        => a.Length >= 2 ? (object?)SysMath.Pow(ToD(a[0]), ToD(a[1])) : null;

    private static object? UnaryNegate(object? v) => v switch
    {
        long l => (object?)(-l),
        double d => (object?)(-d),
        BogDbInterval interval => (object?)Negate(interval),
        _ => null
    };

    private static object? UnaryNum(object?[] a, Func<long, long> iOp, Func<double, double> dOp)
    {
        if (a.Length < 1) return null;
        return a[0] switch { long l => (object?)iOp(l), _ => (object?)dOp(ToD(a[0])) };
    }

    private static double ToD(object? v) => TypeCoercionHelper.ToDouble(v);
    private static long   ToL(object? v) => TypeCoercionHelper.ToInt64(v);
    private static BogDbInterval Negate(BogDbInterval interval)
        => new(-interval.Months, -interval.Days, -interval.Microseconds);

    private static bool TryParseDate(object? value, out DateOnly date)
    {
        date = default;
        return value switch
        {
            DateOnly d => (date = d) == d,
            string s when DateOnly.TryParseExact(
                s,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed) => (date = parsed) == parsed,
            _ => false
        };
    }

    private static bool TryParseTimestamp(object? value, out DateTimeOffset timestamp)
    {
        var parsed = TimestampFunctions.ParseTimestamp(value);
        timestamp = parsed ?? default;
        return parsed.HasValue;
    }
}
