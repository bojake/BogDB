using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.ExpressionEvaluator;

namespace BogDb.Tests.ExpressionEvaluator;

public class SIMDMathTests
{
    [Fact]
    public void MathFunctionEvaluator_AbsDouble_MatchesScalar()
    {
        // 1. Arrange Vector buffers
        int elementCount = 2048;
        var inputBuffer = new byte[elementCount * sizeof(double)];
        var outputBuffer = new byte[elementCount * sizeof(double)];
        
        Span<double> input = MemoryMarshal.Cast<byte, double>(inputBuffer.AsSpan());
        Span<double> expectedOutput = new double[elementCount];

        for (int i = 0; i < elementCount; i++)
        {
            input[i] = (i % 2 == 0) ? -i * 1.5 : i * 1.5;
            expectedOutput[i] = Math.Abs(input[i]);
        }

        // 2. Act
        MathFunctionEvaluator.AbsDouble(inputBuffer.AsSpan(), outputBuffer.AsSpan(), elementCount);

        // 3. Assert
        Span<double> output = MemoryMarshal.Cast<byte, double>(outputBuffer.AsSpan());
        for (int i = 0; i < elementCount; i++)
        {
            Assert.Equal(expectedOutput[i], output[i]);
        }
    }

    [Fact]
    public void MathFunctionEvaluator_FloorDouble_MatchesScalar()
    {
        int elementCount = 2048;
        var inputBuffer = new byte[elementCount * sizeof(double)];
        var outputBuffer = new byte[elementCount * sizeof(double)];
        
        Span<double> input = MemoryMarshal.Cast<byte, double>(inputBuffer.AsSpan());

        for (int i = 0; i < elementCount; i++)
        {
            input[i] = (i * 1.5) - 0.7; // E.g., -0.7, 0.8, 2.3
        }

        MathFunctionEvaluator.FloorDouble(inputBuffer.AsSpan(), outputBuffer.AsSpan(), elementCount);

        Span<double> output = MemoryMarshal.Cast<byte, double>(outputBuffer.AsSpan());
        for (int i = 0; i < elementCount; i++)
        {
            Assert.Equal(Math.Floor(input[i]), output[i]);
        }
    }

    [Fact]
    public void MathFunctionEvaluator_BoundaryCases_MatchesScalar()
    {
        // Test edge cases explicitly checked in C++ tck: NaN, Infinity, Negative Fractions
        int elementCount = Vector256<double>.Count * 2; // Enough to trigger SIMD and scalar fallback if needed
        var inputBuffer = new byte[elementCount * sizeof(double)];
        var outputBuffer = new byte[elementCount * sizeof(double)];
        
        Span<double> input = MemoryMarshal.Cast<byte, double>(inputBuffer.AsSpan());
        
        input[0] = double.NaN;
        input[1] = double.PositiveInfinity;
        input[2] = double.NegativeInfinity;
        input[3] = -5.5;
        input[4] = 0.0;
        input[5] = -0.0;
        
        // Fill the rest with normal numbers
        for(int i = 6; i < elementCount; i++) {
            input[i] = i * -1.23;
        }

        // --- Test AbsDouble ---
        MathFunctionEvaluator.AbsDouble(inputBuffer.AsSpan(), outputBuffer.AsSpan(), elementCount);
        Span<double> output = MemoryMarshal.Cast<byte, double>(outputBuffer.AsSpan());
        
        for (int i = 0; i < elementCount; i++)
        {
            Assert.Equal(Math.Abs(input[i]), output[i]);
        }
        
        // --- Test FloorDouble ---
        MathFunctionEvaluator.FloorDouble(inputBuffer.AsSpan(), outputBuffer.AsSpan(), elementCount);
        output = MemoryMarshal.Cast<byte, double>(outputBuffer.AsSpan());
        
        for (int i = 0; i < elementCount; i++)
        {
            Assert.Equal(Math.Floor(input[i]), output[i]);
        }

        // --- Test CeilDouble ---
        MathFunctionEvaluator.CeilDouble(inputBuffer.AsSpan(), outputBuffer.AsSpan(), elementCount);
        output = MemoryMarshal.Cast<byte, double>(outputBuffer.AsSpan());
        
        for (int i = 0; i < elementCount; i++)
        {
            Assert.Equal(Math.Ceiling(input[i]), output[i]);
        }
    }
}
