using System;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Interval;

internal static class IntervalFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        r["interval"] = r["duration"] = a =>
        {
            if (a.Length < 1)
                return null;
            return TypeCoercionHelper.TryParseInterval(a[0], out var interval) ? (object?)interval : null;
        };

        r["to_interval"] = r["interval"];

        r["to_years"] = a => BuildIntegralInterval(a, BogDbInterval.FromYears);
        r["to_months"] = a => BuildIntegralInterval(a, BogDbInterval.FromMonths);
        r["to_days"] = a => BuildIntegralInterval(a, BogDbInterval.FromDays);
        r["to_hours"] = a => BuildFloatingInterval(a, BogDbInterval.FromHours);
        r["to_minutes"] = a => BuildFloatingInterval(a, BogDbInterval.FromMinutes);
        r["to_seconds"] = a => BuildFloatingInterval(a, BogDbInterval.FromSeconds);
        r["to_milliseconds"] = a => BuildFloatingInterval(a, BogDbInterval.FromMilliseconds);
        r["to_microseconds"] = a => BuildFloatingInterval(a, BogDbInterval.FromMicroseconds);
    }

    private static object? BuildIntegralInterval(object?[] args, Func<long, BogDbInterval> factory)
    {
        if (args.Length < 1)
            return null;

        try
        {
            return factory(TypeCoercionHelper.ToInt64(args[0]));
        }
        catch
        {
            return null;
        }
    }

    private static object? BuildFloatingInterval(object?[] args, Func<double, BogDbInterval> factory)
    {
        if (args.Length < 1)
            return null;

        try
        {
            return factory(TypeCoercionHelper.ToDouble(args[0]));
        }
        catch
        {
            return null;
        }
    }
}
