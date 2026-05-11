using System;
using System.Collections;
using System.Collections.Generic;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Struct chunk data storing per-row field maps.
/// </summary>
public sealed class StructChunkData
{
    private readonly List<Dictionary<string, object?>?> _rows;
    private readonly HashSet<string> _fieldNames;

    public StructChunkData(int capacity)
    {
        _rows = new List<Dictionary<string, object?>?>(capacity);
        _fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _rows.Count;
    public IReadOnlyCollection<string> FieldNames => _fieldNames;

    public void Append(object? value)
    {
        if (!TryNormalizeStructValue(value, out var normalized, out var isNull))
            throw new NotSupportedException($"StructChunkData expects map-like values, got {value?.GetType().Name ?? "null"}.");

        if (isNull)
        {
            _rows.Add(null);
            return;
        }

        foreach (var key in normalized!.Keys)
            _fieldNames.Add(key);
        _rows.Add(normalized);
    }

    public void Update(int offsetInChunk, object? value)
    {
        if ((uint)offsetInChunk >= (uint)_rows.Count)
            throw new ArgumentOutOfRangeException(nameof(offsetInChunk));

        if (!TryNormalizeStructValue(value, out var normalized, out var isNull))
            throw new NotSupportedException($"StructChunkData expects map-like values, got {value?.GetType().Name ?? "null"}.");

        if (isNull)
        {
            _rows[offsetInChunk] = null;
            return;
        }

        foreach (var key in normalized!.Keys)
            _fieldNames.Add(key);
        _rows[offsetInChunk] = normalized;
    }

    public Dictionary<string, object?>? Lookup(int offsetInChunk)
    {
        if ((uint)offsetInChunk >= (uint)_rows.Count)
            throw new ArgumentOutOfRangeException(nameof(offsetInChunk));

        var row = _rows[offsetInChunk];
        return row is null ? null : new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<Dictionary<string, object?>?> Scan(int offsetInChunk, int numValues)
    {
        var end = Math.Min(offsetInChunk + numValues, _rows.Count);
        for (var i = offsetInChunk; i < end; i++)
            yield return Lookup(i);
    }

    public void Truncate(int newCount)
    {
        if (newCount < 0 || newCount > _rows.Count)
            throw new ArgumentOutOfRangeException(nameof(newCount));

        if (newCount == _rows.Count)
            return;

        _rows.RemoveRange(newCount, _rows.Count - newCount);
        _fieldNames.Clear();
        foreach (var row in _rows)
        {
            if (row is null)
                continue;
            foreach (var key in row.Keys)
                _fieldNames.Add(key);
        }
    }

    private static bool TryNormalizeStructValue(object? value, out Dictionary<string, object?>? normalized, out bool isNull)
    {
        if (value is null)
        {
            normalized = null;
            isNull = true;
            return true;
        }

        if (value is IDictionary<string, object?> genericNullable)
        {
            normalized = new Dictionary<string, object?>(genericNullable, StringComparer.OrdinalIgnoreCase);
            isNull = false;
            return true;
        }

        if (value is IDictionary<string, object> generic)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in generic)
                dict[k] = v;
            normalized = dict;
            isNull = false;
            return true;
        }

        if (value is IDictionary nonGeneric)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in nonGeneric)
            {
                if (entry.Key is not string key)
                {
                    normalized = null;
                    isNull = false;
                    return false;
                }
                dict[key] = entry.Value;
            }
            normalized = dict;
            isNull = false;
            return true;
        }

        normalized = null;
        isNull = false;
        return false;
    }
}
