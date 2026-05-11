using System;
using System.Runtime.InteropServices;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.ExpressionEvaluator;

namespace BogDb.Tests.ExpressionEvaluator;

public class AggregationTests
{
    [Fact]
    public void AggregateFunctionEvaluator_SumInt64_CalculatesCorrectly()
    {
        int elementCount = 2048;
        var inputBuffer = new byte[elementCount * sizeof(long)];
        Span<long> input = MemoryMarshal.Cast<byte, long>(inputBuffer.AsSpan());
        
        long expectedSumUnfiltered = 0;
        for (int i = 0; i < elementCount; i++)
        {
            input[i] = i;
            expectedSumUnfiltered += i;
        }

        var selVector = new SelectionVector((ushort)elementCount);
        long actualSum = AggregateFunctionEvaluator.SumInt64(inputBuffer.AsSpan(), ref selVector);
        Assert.Equal(expectedSumUnfiltered, actualSum);
        Assert.Equal(elementCount, AggregateFunctionEvaluator.Count(ref selVector));
    }

    [Fact]
    public void AggregateFunctionEvaluator_MinMaxInt64_Filtered_CalculatesCorrectly()
    {
        int elementCount = 100;
        var inputBuffer = new byte[elementCount * sizeof(long)];
        Span<long> input = MemoryMarshal.Cast<byte, long>(inputBuffer.AsSpan());
        
        for (int i = 0; i < elementCount; i++)
        {
            input[i] = i * 10; // 0, 10, 20...990
        }

        var selVector = new SelectionVector((ushort)elementCount);
        selVector.GetMutableBuffer()[0] = 5;  // 50
        selVector.GetMutableBuffer()[1] = 10; // 100
        selVector.GetMutableBuffer()[2] = 2;  // 20
        selVector.SetSelSize(3);

        long min = AggregateFunctionEvaluator.MinInt64(inputBuffer.AsSpan(), ref selVector);
        long max = AggregateFunctionEvaluator.MaxInt64(inputBuffer.AsSpan(), ref selVector);
        long count = AggregateFunctionEvaluator.Count(ref selVector);
        
        Assert.Equal(20, min);
        Assert.Equal(100, max);
        Assert.Equal(3, count);
    }

    [Fact]
    public void AggregateFunctionEvaluator_WithNulls_ExcludesNullValues()
    {
        // Setup similar to C++ tck: CREATE (:A {num: 33}), (:A {num: 42}), (:A)
        // RETURN count(n.num), sum(n.num) -> should be 2, 75
        int elementCount = 3;
        var inputBuffer = new byte[elementCount * sizeof(long)];
        Span<long> input = MemoryMarshal.Cast<byte, long>(inputBuffer.AsSpan());
        
        input[0] = 33;
        input[1] = 42;
        input[2] = 999; // Value shouldn't matter as it's null

        var nullMask = new NullMask((ulong)elementCount);
        nullMask.SetNull(2, true);

        var selVector = new SelectionVector((ushort)elementCount);

        long sum = AggregateFunctionEvaluator.SumInt64(inputBuffer.AsSpan(), ref selVector, nullMask);
        long count = AggregateFunctionEvaluator.Count(ref selVector, nullMask);
        long min = AggregateFunctionEvaluator.MinInt64(inputBuffer.AsSpan(), ref selVector, nullMask);
        long max = AggregateFunctionEvaluator.MaxInt64(inputBuffer.AsSpan(), ref selVector, nullMask);

        Assert.Equal(75, sum);
        Assert.Equal(2, count);
        Assert.Equal(33, min);
        Assert.Equal(42, max);
    }
}
