using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Executes physical SIMD-driven boolean operations mapping directly onto unmanaged buffers.
/// </summary>
public sealed class BooleanFunctionEvaluator : ExpressionEvaluator
{
    public delegate void SimdBooleanOperation(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, ref SelectionVector selVector);

    private readonly SimdBooleanOperation _simdFunc;

    public BooleanFunctionEvaluator(SimdBooleanOperation simdFunc, ExpressionEvaluator left, ExpressionEvaluator right)
        : base(hasResultVector: false)
    {
        _simdFunc = simdFunc;
        Children.Add(left);
        Children.Add(right);
    }

    public override bool Evaluate()
    {
        throw new InvalidOperationException("Boolean filters must use Select(ref SelectionVector)");
    }

    public override bool Select(ref SelectionVector selVector)
    {
        // 1. Resolve parameters so their ResultVectors populate
        foreach (var child in Children)
        {
            child.Evaluate();
        }

        // 2. Delegate the unmanaged spans directly into our intrinsic evaluators
        var leftSpan = Children[0].ResultVector.GetAsReadOnlySpan();
        var rightSpan = Children[1].ResultVector.GetAsReadOnlySpan();

        _simdFunc(leftSpan, rightSpan, ref selVector);

        return selVector.GetSelSize() > 0;
    }

    // =========================================================================
    // Intrinsic Structural Permutations (e.g. Int64 Equals)
    // =========================================================================

    public static void EqualsInt64(ReadOnlySpan<byte> leftBytes, ReadOnlySpan<byte> rightBytes, ref SelectionVector selVector)
    {
        var left = MemoryMarshal.Cast<byte, long>(leftBytes);
        var right = MemoryMarshal.Cast<byte, long>(rightBytes);

        ushort matchCount = 0;
        var selBuffer = selVector.GetMutableBuffer();

        // Check if platform supports 256-bit SIMD
        if (Vector256.IsHardwareAccelerated && left.Length >= Vector256<long>.Count)
        {
            int vecSize = Vector256<long>.Count;
            int i = 0;

            for (; i <= left.Length - vecSize; i += vecSize)
            {
                var vecLeft = Vector256.Create<long>(left.Slice(i, vecSize));
                var vecRight = Vector256.Create<long>(right.Slice(i, vecSize));
                var mask = Vector256.Equals(vecLeft, vecRight);

                // Extract valid indices based on the comparison bitmask mapping
                for (int j = 0; j < vecSize; j++)
                {
                    if (mask.GetElement(j) == -1L) // SIMD All-1 mask for true
                    {
                        selBuffer[matchCount++] = (ushort)(i + j);
                    }
                }
            }

            // Scalar drain loop
            for (; i < left.Length; i++)
            {
                if (left[i] == right[i])
                {
                    selBuffer[matchCount++] = (ushort)i;
                }
            }
        }
        else
        {
            // Scalar fallback
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] == right[i])
                {
                    selBuffer[matchCount++] = (ushort)i;
                }
            }
        }

        selVector.SetSelSize(matchCount);
    }
}
