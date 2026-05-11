using System;
using System.Collections;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// List chunk data using list-entry indirection into a flattened child-value buffer.
/// </summary>
public sealed class ListChunkData
{
    private readonly List<object?> _childValues;
    private readonly List<ListEntry> _entries;
    private readonly List<bool> _isNull;

    public ListChunkData(int capacity)
    {
        _childValues = new List<object?>(capacity * 2);
        _entries = new List<ListEntry>(capacity);
        _isNull = new List<bool>(capacity);
    }

    public int Count => _entries.Count;
    public int ChildValueCount => _childValues.Count;

    public void Append(object? value)
    {
        if (!TryNormalizeListValue(value, out var items, out var isNull))
            throw new NotSupportedException($"ListChunkData expects list-like values, got {value?.GetType().Name ?? "null"}.");

        if (isNull)
        {
            _entries.Add(new ListEntry(0, 0));
            _isNull.Add(true);
            return;
        }

        var offset = (ulong)_childValues.Count;
        foreach (var item in items!)
            _childValues.Add(item);

        _entries.Add(new ListEntry(offset, (uint)items.Count));
        _isNull.Add(false);
    }

    public void Update(int offsetInChunk, object? value)
    {
        if ((uint)offsetInChunk >= (uint)_entries.Count)
            throw new ArgumentOutOfRangeException(nameof(offsetInChunk));

        if (!TryNormalizeListValue(value, out var items, out var isNull))
            throw new NotSupportedException($"ListChunkData expects list-like values, got {value?.GetType().Name ?? "null"}.");

        if (isNull)
        {
            _entries[offsetInChunk] = new ListEntry(0, 0);
            _isNull[offsetInChunk] = true;
            return;
        }

        var offset = (ulong)_childValues.Count;
        foreach (var item in items!)
            _childValues.Add(item);

        // Out-of-place update: append children and rewrite entry.
        _entries[offsetInChunk] = new ListEntry(offset, (uint)items.Count);
        _isNull[offsetInChunk] = false;
    }

    public List<object?>? Lookup(int offsetInChunk)
    {
        if ((uint)offsetInChunk >= (uint)_entries.Count)
            throw new ArgumentOutOfRangeException(nameof(offsetInChunk));

        if (_isNull[offsetInChunk])
            return null;

        var entry = _entries[offsetInChunk];
        var result = new List<object?>((int)entry.Size);
        var start = (int)entry.Offset;
        for (var i = 0; i < entry.Size; i++)
            result.Add(_childValues[start + i]);
        return result;
    }

    public IEnumerable<List<object?>?> Scan(int offsetInChunk, int numValues)
    {
        var end = Math.Min(offsetInChunk + numValues, _entries.Count);
        for (var i = offsetInChunk; i < end; i++)
            yield return Lookup(i);
    }

    public void Truncate(int newCount)
    {
        if (newCount < 0 || newCount > _entries.Count)
            throw new ArgumentOutOfRangeException(nameof(newCount));

        if (newCount == _entries.Count)
            return;

        ulong childCount = 0;
        for (var i = 0; i < newCount; i++)
        {
            if (_isNull[i])
                continue;
            var entry = _entries[i];
            var entryEnd = entry.Offset + entry.Size;
            if (entryEnd > childCount)
                childCount = entryEnd;
        }

        _entries.RemoveRange(newCount, _entries.Count - newCount);
        _isNull.RemoveRange(newCount, _isNull.Count - newCount);
        if ((ulong)_childValues.Count > childCount)
            _childValues.RemoveRange((int)childCount, _childValues.Count - (int)childCount);
    }

    private static bool TryNormalizeListValue(object? value, out IReadOnlyList<object?>? items, out bool isNull)
    {
        if (value is null)
        {
            items = null;
            isNull = true;
            return true;
        }

        if (value is string || value is IDictionary)
        {
            items = null;
            isNull = false;
            return false;
        }

        if (value is IEnumerable<object?> objectEnumerable)
        {
            items = new List<object?>(objectEnumerable);
            isNull = false;
            return true;
        }

        if (value is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
                list.Add(item);
            items = list;
            isNull = false;
            return true;
        }

        items = null;
        isNull = false;
        return false;
    }
}
