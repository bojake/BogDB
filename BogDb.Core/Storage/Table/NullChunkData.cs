using System;
using System.Collections.Generic;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Compact storage for all-null chunk segments.
/// </summary>
public sealed class NullChunkData
{
    private int _count;

    public NullChunkData(int initialCount = 0)
    {
        _count = initialCount;
    }

    public int Count => _count;

    public void Append()
    {
        _count++;
    }

    public object? Lookup(int offsetInChunk)
    {
        if ((uint)offsetInChunk >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(offsetInChunk));
        return null;
    }

    public IEnumerable<object?> Scan(int offsetInChunk, int numValues)
    {
        var end = Math.Min(offsetInChunk + numValues, _count);
        for (var i = offsetInChunk; i < end; i++)
            yield return null;
    }

    public void Truncate(int newCount)
    {
        if (newCount < 0 || newCount > _count)
            throw new ArgumentOutOfRangeException(nameof(newCount));
        _count = newCount;
    }
}
