using System;
using System.Runtime.InteropServices;
using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Implements native aggregation logic equivalent to `aggregate_function.cpp`.
/// Evaluates sequentially over unmanaged memory vectors via SelectionVectors.
/// </summary>
public static class AggregateFunctionEvaluator
{
    public static long SumInt64(ReadOnlySpan<byte> inputBuffer, ref SelectionVector selVector, NullMask nullMask = null)
    {
        ReadOnlySpan<long> input = MemoryMarshal.Cast<byte, long>(inputBuffer);
        long sum = 0;
        
        int selSize = selVector.GetSelSize();
        ReadOnlySpan<ushort> selArray = selVector.GetMutableBuffer();
        for (int i = 0; i < selSize; i++)
        {
            uint pos = selArray[i];
            if (nullMask == null || !nullMask.IsNull(pos))
            {
                sum += input[(int)pos];
            }
        }
        
        return sum;
    }

    public static long Count(ref SelectionVector selVector, NullMask nullMask = null)
    {
        if (nullMask == null)
        {
            return selVector.GetSelSize();
        }

        long count = 0;
        int selSize = selVector.GetSelSize();
        ReadOnlySpan<ushort> selArray = selVector.GetMutableBuffer();
        for (int i = 0; i < selSize; i++)
        {
            if (!nullMask.IsNull(selArray[i]))
            {
                count++;
            }
        }
        return count;
    }

    public static long MinInt64(ReadOnlySpan<byte> inputBuffer, ref SelectionVector selVector, NullMask nullMask = null)
    {
        ReadOnlySpan<long> input = MemoryMarshal.Cast<byte, long>(inputBuffer);
        int selSize = selVector.GetSelSize();
        if (selSize == 0) return 0;

        long min = long.MaxValue;
        bool hasValues = false;
        ReadOnlySpan<ushort> selArray = selVector.GetMutableBuffer();
        for (int i = 0; i < selSize; i++)
        {
            uint pos = selArray[i];
            if (nullMask == null || !nullMask.IsNull(pos))
            {
                if (input[(int)pos] < min) min = input[(int)pos];
                hasValues = true;
            }
        }
        
        return hasValues ? min : 0;
    }

    public static long MaxInt64(ReadOnlySpan<byte> inputBuffer, ref SelectionVector selVector, NullMask nullMask = null)
    {
        ReadOnlySpan<long> input = MemoryMarshal.Cast<byte, long>(inputBuffer);
        int selSize = selVector.GetSelSize();
        if (selSize == 0) return 0;

        long max = long.MinValue;
        bool hasValues = false;
        ReadOnlySpan<ushort> selArray = selVector.GetMutableBuffer();
        for (int i = 0; i < selSize; i++)
        {
            uint pos = selArray[i];
            if (nullMask == null || !nullMask.IsNull(pos))
            {
                if (input[(int)pos] > max) max = input[(int)pos];
                hasValues = true;
            }
        }
        
        return hasValues ? max : 0;
    }
}
