using System;
using System.Runtime.InteropServices;
using BogDb.Core.Common;

namespace BogDb.Core.ExpressionEvaluator;

/// <summary>
/// Maps complex nested data transformations bypassing C# managed nested arrays (`list_extract.cpp`, `list_slice.cpp`).
/// Operates exclusively through linear offset calculations across `System.Memory`.
/// </summary>
public static class ListFunctionEvaluator
{
    public struct ListEntry
    {
        public uint Offset;
        public uint Size;
    }

    /// <summary>
    /// Port of `list_extract_function.cpp`.
    /// Extracts a specific zero-indexed element from a nested list based on its offset in the global list chunk buffer.
    /// </summary>
    public static void ListExtractInt64(
        ReadOnlySpan<ListEntry> listEntries, 
        ReadOnlySpan<long> listDataBuffer,
        int targetIndex,
        ValueVector resultVector)
    {
        for (uint i = 0; i < listEntries.Length; i++)
        {
            var entry = listEntries[(int)i];
            if (targetIndex < 0 || targetIndex >= entry.Size)
            {
                resultVector.SetNull(i, true);
            }
            else
            {
                long val = listDataBuffer[(int)(entry.Offset + targetIndex)];
                resultVector.SetValue<long>(i, val);
            }
        }
    }

    /// <summary>
    /// Port of `list_slice_function.cpp`.
    /// Modifies the `ListEntry` metadata block reducing the boundary without needing to reallocate the underlying data spans.
    /// </summary>
    public static void ListSlice(
        ReadOnlySpan<ListEntry> sourceEntries,
        Span<ListEntry> targetEntries,
        int startIndex,
        int sliceLength)
    {
        for (int i = 0; i < sourceEntries.Length; i++)
        {
            var src = sourceEntries[i];
            var dest = new ListEntry();
            
            if (startIndex < src.Size)
            {
                dest.Offset = src.Offset + (uint)startIndex;
                dest.Size = Math.Min((uint)sliceLength, src.Size - (uint)startIndex);
            }
            else
            {
                dest.Offset = src.Offset;
                dest.Size = 0;
            }
            targetEntries[i] = dest;
        }
    }
}
