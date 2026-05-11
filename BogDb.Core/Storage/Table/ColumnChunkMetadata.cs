using System;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Minimal metadata for a column chunk segment.
/// Mirrors the role of C++ ColumnChunkMetadata for row ranges and summary values.
/// </summary>
public sealed class ColumnChunkMetadata
{
    public long StartRow { get; private set; }
    public long NumValues { get; private set; }
    public long NullCount { get; private set; }
    public object? MinValue { get; private set; }
    public object? MaxValue { get; private set; }

    public ColumnChunkMetadata(long startRow)
    {
        StartRow = startRow;
        NumValues = 0;
        NullCount = 0;
    }

    public void IncrementValue(object? value)
    {
        NumValues++;
        if (value is null)
        {
            NullCount++;
            return;
        }

        if (MinValue is null || Compare(value, MinValue) < 0)
            MinValue = value;
        if (MaxValue is null || Compare(value, MaxValue) > 0)
            MaxValue = value;
    }

    public void Recompute(ReadOnlySpan<object?> values)
    {
        NumValues = values.Length;
        NullCount = 0;
        MinValue = null;
        MaxValue = null;
        foreach (var value in values)
        {
            if (value is null)
            {
                NullCount++;
                continue;
            }

            if (MinValue is null || Compare(value, MinValue) < 0)
                MinValue = value;
            if (MaxValue is null || Compare(value, MaxValue) > 0)
                MaxValue = value;
        }
    }

    private static int Compare(object left, object right)
    {
        if (left is IComparable cmp)
            return cmp.CompareTo(right);
        return StringComparer.Ordinal.Compare(left.ToString(), right.ToString());
    }
}
