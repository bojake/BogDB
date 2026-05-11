using System;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// String chunk data backed by a dictionary + per-row dictionary indices.
/// </summary>
public sealed class StringChunkData
{
    private DictionaryChunk _dictionary;
    private readonly List<int> _indices;
    private readonly List<bool> _isNull;

    public StringChunkData(int capacity, bool enableCompression = true)
    {
        _dictionary = new DictionaryChunk(enableCompression);
        _indices = new List<int>(capacity);
        _isNull = new List<bool>(capacity);
    }

    public int Count => _indices.Count;
    public int DistinctCount => _dictionary.DistinctCount;

    public void Append(object? value)
    {
        if (!TryNormalizeStringValue(value, out var str, out var isNull))
            throw new NotSupportedException($"StringChunkData only supports string-compatible values, got {value?.GetType().Name ?? "null"}.");

        if (isNull)
        {
            _indices.Add(0);
            _isNull.Add(true);
            return;
        }

        var idx = _dictionary.AppendString(str!);
        _indices.Add(idx);
        _isNull.Add(false);
    }

    public void Update(int offsetInChunk, object? value)
    {
        if ((uint)offsetInChunk >= (uint)_indices.Count)
            throw new ArgumentOutOfRangeException(nameof(offsetInChunk));

        if (!TryNormalizeStringValue(value, out var str, out var isNull))
            throw new NotSupportedException($"StringChunkData only supports string-compatible values, got {value?.GetType().Name ?? "null"}.");

        if (isNull)
        {
            _indices[offsetInChunk] = 0;
            _isNull[offsetInChunk] = true;
            return;
        }

        var idx = _dictionary.AppendString(str!);
        _indices[offsetInChunk] = idx;
        _isNull[offsetInChunk] = false;
    }

    public string? Lookup(int offsetInChunk)
    {
        if ((uint)offsetInChunk >= (uint)_indices.Count)
            throw new ArgumentOutOfRangeException(nameof(offsetInChunk));

        return _isNull[offsetInChunk] ? null : _dictionary.GetString(_indices[offsetInChunk]);
    }

    public IEnumerable<string?> Scan(int offsetInChunk, int numValues)
    {
        var end = Math.Min(offsetInChunk + numValues, _indices.Count);
        for (var i = offsetInChunk; i < end; i++)
            yield return Lookup(i);
    }

    /// <summary>
    /// Prunes unused dictionary entries and rewrites index references.
    /// </summary>
    public void FinalizeDictionary()
    {
        var newDictionary = new DictionaryChunk(enableCompression: true);
        var remap = new Dictionary<int, int>();

        for (var i = 0; i < _indices.Count; i++)
        {
            if (_isNull[i])
                continue;

            var oldIdx = _indices[i];
            if (!remap.TryGetValue(oldIdx, out var newIdx))
            {
                var value = _dictionary.GetString(oldIdx);
                newIdx = newDictionary.AppendString(value);
                remap[oldIdx] = newIdx;
            }
            _indices[i] = newIdx;
        }

        _dictionary = newDictionary;
    }

    public void Truncate(int newCount)
    {
        if (newCount < 0 || newCount > _indices.Count)
            throw new ArgumentOutOfRangeException(nameof(newCount));

        if (newCount == _indices.Count)
            return;

        _indices.RemoveRange(newCount, _indices.Count - newCount);
        _isNull.RemoveRange(newCount, _isNull.Count - newCount);
        FinalizeDictionary();
    }

    private static bool TryNormalizeStringValue(object? value, out string? normalized, out bool isNull)
    {
        if (value is null)
        {
            normalized = null;
            isNull = true;
            return true;
        }

        if (value is string s)
        {
            normalized = s;
            isNull = false;
            return true;
        }

        if (value is KuString kuString)
        {
            normalized = kuString.GetAsString();
            isNull = false;
            return true;
        }

        normalized = null;
        isNull = false;
        return false;
    }
}
