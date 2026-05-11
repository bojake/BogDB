using System;
using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Implements vectorized string aggregations matching the C++ native `vector_string_functions.cpp`
/// with .NET 10 allocations where necessary (or spans if possible).
/// </summary>
public static class StringFunctionEvaluator
{
    public static void Concat(ValueVector left, ValueVector right, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (left.IsNull(i) || right.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            var leftStr = left.GetValue<KuString>(i).GetAsString();
            var rightStr = right.GetValue<KuString>(i).GetAsString();
            
            // Simplistic string allocation to bridge the gap until Overflow buffers are ported.
            var combined = leftStr + rightStr;
            
            // Note: Since BogDB doesn't have overflow buffer memory managers implemented yet,
            // we will only write short strings inline or mock them for testing purposes.
            // For strings longer than 12 bytes, this will require the storage manager.
            // But we can emulate it for the test runner.
            SetKuString(result, i, combined);
        }
    }

    public static void Contains(ValueVector left, ValueVector right, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (left.IsNull(i) || right.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            var leftStr = left.GetValue<KuString>(i).GetAsString();
            var rightStr = right.GetValue<KuString>(i).GetAsString();
            
            result.SetValue<byte>(i, (byte)(leftStr.Contains(rightStr) ? 1 : 0));
        }
    }

    public static void StartsWith(ValueVector left, ValueVector right, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (left.IsNull(i) || right.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            var leftStr = left.GetValue<KuString>(i).GetAsString();
            var rightStr = right.GetValue<KuString>(i).GetAsString();
            
            result.SetValue<byte>(i, (byte)(leftStr.StartsWith(rightStr) ? 1 : 0));
        }
    }

    public static void EndsWith(ValueVector left, ValueVector right, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (left.IsNull(i) || right.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            var leftStr = left.GetValue<KuString>(i).GetAsString();
            var rightStr = right.GetValue<KuString>(i).GetAsString();
            
            result.SetValue<byte>(i, (byte)(leftStr.EndsWith(rightStr) ? 1 : 0));
        }
    }

    public static unsafe void SetKuString(ValueVector vector, uint pos, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        SetKuBytes(vector, pos, bytes);
    }

    public static unsafe void SetKuBytes(ValueVector vector, uint pos, ReadOnlySpan<byte> bytes)
    {
        ref var kuStr = ref vector.GetValue<KuString>(pos);
        kuStr.Length = (uint)bytes.Length;

        if ((ulong)bytes.Length <= KuString.SHORT_STR_LENGTH)
        {
            fixed (byte* pPrefix = kuStr.Prefix)
            fixed (byte* pData = kuStr.Data)
            {
                for (int j = 0; j < 4; j++)
                {
                    pPrefix[j] = j < bytes.Length ? bytes[j] : (byte)0;
                }

                for (int j = 0; j < 8; j++)
                {
                    var srcIdx = j + 4;
                    pData[j] = srcIdx < bytes.Length ? bytes[srcIdx] : (byte)0;
                }
            }
        }
        else
        {
            if (vector.OverflowBuffer == null)
            {
                vector.OverflowBuffer = new InMemOverflowBuffer();
            }

            byte* overflowPtr = vector.OverflowBuffer.AllocateSpace(bytes.Length);
            fixed (byte* pPrefix = kuStr.Prefix)
            {
                for (int j = 0; j < 4; j++)
                {
                    pPrefix[j] = bytes[j];
                }
            }

            for (int j = 0; j < bytes.Length; j++)
            {
                overflowPtr[j] = bytes[j];
            }

            kuStr.SetOverflowPtr((ulong)overflowPtr);
        }

        vector.SetNull(pos, false);
    }
    public static void Length(ValueVector input, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (input.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            var str = input.GetValue<KuString>(i).GetAsString();
            result.SetValue<long>(i, str.Length);
        }
    }

    public static void Lower(ValueVector input, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (input.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            var str = input.GetValue<KuString>(i).GetAsString();
            SetKuString(result, i, str.ToLowerInvariant());
        }
    }

    public static void Upper(ValueVector input, ValueVector result)
    {
        int count = result.Capacity;
        for (uint i = 0; i < count; i++)
        {
            if (input.IsNull(i))
            {
                result.SetNull(i, true);
                continue;
            }

            var str = input.GetValue<KuString>(i).GetAsString();
            SetKuString(result, i, str.ToUpperInvariant());
        }
    }
}
