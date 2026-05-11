using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.ExpressionEvaluator;

namespace BogDb.Tests.Processor;

public class SIMDBooleanTests
{
    [Fact]
    public void SIMDEquals_MatchesVectorsCorrectly()
    {
        // 1. Arrange Vector buffers
        var selVector = new SelectionVector(2048);
        var leftSpan = new byte[2048 * sizeof(long)];
        var rightSpan = new byte[2048 * sizeof(long)];
        
        Span<long> left = MemoryMarshal.Cast<byte, long>(leftSpan.AsSpan());
        Span<long> right = MemoryMarshal.Cast<byte, long>(rightSpan.AsSpan());

        for (int i = 0; i < 2048; i++)
        {
            left[i] = i;
            right[i] = (i % 2 == 0) ? i : i + 1; // Match on even indices
        }

        // 2. Act
        BooleanFunctionEvaluator.EqualsInt64(leftSpan, rightSpan, ref selVector);

        // 3. Assert
        Assert.Equal(1024, selVector.GetSelSize());
        var buffer = selVector.GetMutableBuffer();
        
        for (int i = 0; i < 1024; i++)
        {
            Assert.Equal((ushort)(i * 2), buffer[i]);
        }
    }
}
