using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Dictionary storage for string values.
/// Stores UTF-8 bytes in a contiguous buffer and offsets per distinct string.
/// </summary>
public sealed class DictionaryChunk
{
    private readonly bool _enableCompression;
    private readonly List<byte> _stringData = new();
    private readonly List<int> _offsets = new();
    private readonly Dictionary<string, int> _indexByValue = new(StringComparer.Ordinal);

    public DictionaryChunk(bool enableCompression = true)
    {
        _enableCompression = enableCompression;
    }

    public int DistinctCount => _offsets.Count;
    public int TotalBytes => _stringData.Count;

    public int AppendString(string value)
    {
        if (_enableCompression && _indexByValue.TryGetValue(value, out var existing))
            return existing;

        var startOffset = _stringData.Count;
        var bytes = Encoding.UTF8.GetBytes(value);
        _stringData.AddRange(bytes);
        _offsets.Add(startOffset);

        var idx = _offsets.Count - 1;
        if (_enableCompression)
            _indexByValue[value] = idx;
        return idx;
    }

    public string GetString(int index)
    {
        if ((uint)index >= (uint)_offsets.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var start = _offsets[index];
        var end = (index + 1 < _offsets.Count) ? _offsets[index + 1] : _stringData.Count;
        var length = end - start;
        return Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(_stringData).Slice(start, length));
    }

    public void ResetToEmpty()
    {
        _stringData.Clear();
        _offsets.Clear();
        _indexByValue.Clear();
    }
}
