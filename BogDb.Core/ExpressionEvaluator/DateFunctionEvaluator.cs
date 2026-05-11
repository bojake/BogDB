using System;
using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Implements vectorized date aggregations matching the native C++ `vector_date_functions.cpp`.
/// </summary>
public static class DateFunctionEvaluator
{
    // BogDb C++ natively utilizes 1970-01-01 as the Epoch for date_t (int32_t days).
    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void DayName(ValueVector dateVector, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (dateVector.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            // date_t is stored natively as INT32 (days since epoch).
            int daysSinceEpoch = dateVector.GetValue<int>(i);
            var date = UnixEpoch.AddDays(daysSinceEpoch);
            
            // Map the DayOfWeek directly to KuString chunk
            string dayName = date.DayOfWeek.ToString();

            StringFunctionEvaluator.SetKuString(result, i, dayName);
        }
    }

    public static void DatePart(ValueVector partVector, ValueVector dateVector, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (partVector.IsNull(i) || dateVector.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            string part = partVector.GetValue<KuString>(i).GetAsString().ToLowerInvariant();
            int daysSinceEpoch = dateVector.GetValue<int>(i);
            var date = UnixEpoch.AddDays(daysSinceEpoch);

            long partValue = part switch
            {
                "year" => date.Year,
                "month" => date.Month,
                "day" => date.Day,
                _ => 0 // Defaults to 0 for unhandled string mappings in this bridge
            };

            // Return INT64 chunk matching the native C++ scalar outputs
            result.SetValue<long>(i, partValue);
        }
    }
}
