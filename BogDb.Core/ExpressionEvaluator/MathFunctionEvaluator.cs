using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Implements vectorized mathematical operations matching the C++ native `vector_arithmetic_functions.cpp`.
/// Utilizes `.NET 10` System.Runtime.Intrinsics to execute parallel vector operations without heap allocations.
/// </summary>
public static class MathFunctionEvaluator
{
    public static void AbsDouble(ReadOnlySpan<byte> inputBuffer, Span<byte> outputBuffer, int elementCount)
    {
        ReadOnlySpan<double> input = MemoryMarshal.Cast<byte, double>(inputBuffer);
        Span<double> output = MemoryMarshal.Cast<byte, double>(outputBuffer);

        int vectorLength = Vector256<double>.Count;
        int i = 0;

        // Vectorized loop
        if (Vector256.IsHardwareAccelerated)
        {
            for (; i <= elementCount - vectorLength; i += vectorLength)
            {
                Vector256<double> vIn = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(input.Slice(i)));
                Vector256<double> vOut = Vector256.Abs(vIn);
                vOut.StoreUnsafe(ref MemoryMarshal.GetReference(output.Slice(i)));
            }
        }

        // Scalar fallback for remaining elements
        for (; i < elementCount; i++)
        {
            output[i] = Math.Abs(input[i]);
        }
    }

    public static void AbsInt64(ReadOnlySpan<byte> inputBuffer, Span<byte> outputBuffer, int elementCount)
    {
        ReadOnlySpan<long> input = MemoryMarshal.Cast<byte, long>(inputBuffer);
        Span<long> output = MemoryMarshal.Cast<byte, long>(outputBuffer);

        int vectorLength = Vector256<long>.Count;
        int i = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            for (; i <= elementCount - vectorLength; i += vectorLength)
            {
                Vector256<long> vIn = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(input.Slice(i)));
                Vector256<long> vOut = Vector256.Abs(vIn);
                vOut.StoreUnsafe(ref MemoryMarshal.GetReference(output.Slice(i)));
            }
        }

        for (; i < elementCount; i++)
        {
            output[i] = Math.Abs(input[i]);
        }
    }

    public static void FloorDouble(ReadOnlySpan<byte> inputBuffer, Span<byte> outputBuffer, int elementCount)
    {
        ReadOnlySpan<double> input = MemoryMarshal.Cast<byte, double>(inputBuffer);
        Span<double> output = MemoryMarshal.Cast<byte, double>(outputBuffer);

        int vectorLength = Vector256<double>.Count;
        int i = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            for (; i <= elementCount - vectorLength; i += vectorLength)
            {
                Vector256<double> vIn = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(input.Slice(i)));
                // Vector256.Floor is available in newer .NET if hardware supports it.
                // Otherwise this falls back to scalar if missing, but typical x86/64 supports it.
#if NET10_0_OR_GREATER
                Vector256<double> vOut = Vector256.Floor(vIn);
                vOut.StoreUnsafe(ref MemoryMarshal.GetReference(output.Slice(i)));
#else
                output[i] = Math.Floor(input[i]);
                output[i+1] = Math.Floor(input[i+1]);
                output[i+2] = Math.Floor(input[i+2]);
                output[i+3] = Math.Floor(input[i+3]);
#endif
            }
        }

        for (; i < elementCount; i++)
        {
            output[i] = Math.Floor(input[i]);
        }
    }

    public static void CeilDouble(ReadOnlySpan<byte> inputBuffer, Span<byte> outputBuffer, int elementCount)
    {
        ReadOnlySpan<double> input = MemoryMarshal.Cast<byte, double>(inputBuffer);
        Span<double> output = MemoryMarshal.Cast<byte, double>(outputBuffer);

        int vectorLength = Vector256<double>.Count;
        int i = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            for (; i <= elementCount - vectorLength; i += vectorLength)
            {
                Vector256<double> vIn = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(input.Slice(i)));
#if NET10_0_OR_GREATER
                Vector256<double> vOut = Vector256.Ceiling(vIn);
                vOut.StoreUnsafe(ref MemoryMarshal.GetReference(output.Slice(i)));
#else
                output[i] = Math.Ceiling(input[i]);
                output[i+1] = Math.Ceiling(input[i+1]);
                output[i+2] = Math.Ceiling(input[i+2]);
                output[i+3] = Math.Ceiling(input[i+3]);
#endif
            }
        }

        for (; i < elementCount; i++)
        {
            output[i] = Math.Ceiling(input[i]);
        }
    }
}
