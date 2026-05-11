using System;
using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Provides tightly-looped bulk vector conversion functions equivalent to الكِتاب `cast` C++ operators.
/// Maps unmanaged types safely upcasting or downcasting logical fields per chunk.
/// </summary>
public static class CastFunctionEvaluator
{
    public static void CastInt64ToDouble(ValueVector input, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (input.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            long val = input.GetValue<long>(i);
            result.SetValue<double>(i, (double)val);
            result.SetNull(i, false);
        }
    }

    public static void CastDoubleToInt64(ValueVector input, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (input.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            double val = input.GetValue<double>(i);
            result.SetValue<long>(i, (long)val);
            result.SetNull(i, false);
        }
    }

    public static void CastInt32ToInt64(ValueVector input, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (input.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            int val = input.GetValue<int>(i);
            result.SetValue<long>(i, (long)val);
            result.SetNull(i, false);
        }
    }
}
