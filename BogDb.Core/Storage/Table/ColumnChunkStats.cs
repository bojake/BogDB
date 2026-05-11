using System;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Lightweight stats accumulator for a chunk.
/// </summary>
public sealed class ColumnChunkStats
{
    public object? MinValue { get; private set; }
    public object? MaxValue { get; private set; }
    public long NullCount { get; private set; }

    public void Update(object? value)
    {
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
        MinValue = null;
        MaxValue = null;
        NullCount = 0;
        foreach (var value in values)
            Update(value);
    }

    private static int Compare(object left, object right)
    {
        if (left is IComparable cmp)
            return cmp.CompareTo(right);
        return StringComparer.Ordinal.Compare(left.ToString(), right.ToString());
    }
}
