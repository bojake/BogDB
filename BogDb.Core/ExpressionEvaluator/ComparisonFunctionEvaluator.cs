using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Provides equality and comparative operator vectorized execution logic (>, <, >=, <=, ==, !=).
/// Outputs mapped Boolean (represented as bytes: 1 or 0) blocks into the resultant ValueVector.
/// </summary>
public static class ComparisonFunctionEvaluator
{
    public static void Equals<T>(ValueVector left, ValueVector right, ValueVector result) where T : unmanaged, IEquatable<T>
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (left.IsNull(i) || right.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            T l = left.GetValue<T>(i);
            T r = right.GetValue<T>(i);
            
            result.SetValue<byte>(i, (byte)(l.Equals(r) ? 1 : 0));
            result.SetNull(i, false);
        }
    }

    public static void NotEquals<T>(ValueVector left, ValueVector right, ValueVector result) where T : unmanaged, IEquatable<T>
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (left.IsNull(i) || right.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            T l = left.GetValue<T>(i);
            T r = right.GetValue<T>(i);
            
            result.SetValue<byte>(i, (byte)(!l.Equals(r) ? 1 : 0));
            result.SetNull(i, false);
        }
    }

    public static void GreaterThan<T>(ValueVector left, ValueVector right, ValueVector result) where T : unmanaged, IComparable<T>
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (left.IsNull(i) || right.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            T l = left.GetValue<T>(i);
            T r = right.GetValue<T>(i);
            
            result.SetValue<byte>(i, (byte)(l.CompareTo(r) > 0 ? 1 : 0));
            result.SetNull(i, false);
        }
    }

    public static void LessThan<T>(ValueVector left, ValueVector right, ValueVector result) where T : unmanaged, IComparable<T>
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (left.IsNull(i) || right.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            T l = left.GetValue<T>(i);
            T r = right.GetValue<T>(i);
            
            result.SetValue<byte>(i, (byte)(l.CompareTo(r) < 0 ? 1 : 0));
            result.SetNull(i, false);
        }
    }
}
