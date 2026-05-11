using System;
using System.Collections;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Fixed-capacity chunk of column values with metadata/stats.
/// </summary>
public sealed class ColumnChunk
{
    private enum StorageMode : byte
    {
        Generic = 0,
        StringDictionary = 1,
        ListEntries = 2,
        StructRows = 3,
        NullOnly = 4
    }

    private readonly int _capacity;
    private readonly List<object?> _values;
    private StorageMode _mode;
    private StringChunkData? _stringData;
    private ListChunkData? _listData;
    private StructChunkData? _structData;
    private NullChunkData? _nullData;

    public ColumnChunkMetadata Metadata { get; }
    public ColumnChunkStats Stats { get; }

    public ColumnChunk(long startRow, int capacity)
    {
        _capacity = capacity;
        _values = new List<object?>(capacity);
        _mode = StorageMode.Generic;
        _stringData = null;
        _listData = null;
        _structData = null;
        _nullData = null;
        Metadata = new ColumnChunkMetadata(startRow);
        Stats = new ColumnChunkStats();
    }

    public int Capacity => _capacity;
    public int Count => GetCurrentCount();
    public bool IsFull => Count >= _capacity;

    public void Append(object? value)
    {
        if (IsFull)
            throw new InvalidOperationException("ColumnChunk is full.");

        if (_mode == StorageMode.NullOnly && value is not null)
            ConvertNullModeToGeneric();

        if (_mode == StorageMode.Generic && value is null && _values.Count == 0)
            EnsureNullMode();

        if (ShouldUseStringDictionary(value))
            EnsureStringDictionaryMode();
        else if (ShouldUseStructStorage(value))
            EnsureStructMode();
        else if (ShouldUseListStorage(value))
            EnsureListMode();

        if (_mode == StorageMode.StringDictionary)
        {
            _stringData!.Append(value);
        }
        else if (_mode == StorageMode.ListEntries)
        {
            _listData!.Append(value);
        }
        else if (_mode == StorageMode.StructRows)
        {
            _structData!.Append(value);
        }
        else if (_mode == StorageMode.NullOnly)
        {
            _nullData!.Append();
        }
        else
        {
            _values.Add(value);
        }

        Metadata.IncrementValue(value);
        Stats.Update(value);
    }

    public object? Lookup(int offsetInChunk)
    {
        if (_mode == StorageMode.StringDictionary)
            return _stringData!.Lookup(offsetInChunk);
        if (_mode == StorageMode.ListEntries)
            return _listData!.Lookup(offsetInChunk);
        if (_mode == StorageMode.StructRows)
            return _structData!.Lookup(offsetInChunk);
        if (_mode == StorageMode.NullOnly)
            return _nullData!.Lookup(offsetInChunk);
        return _values[offsetInChunk];
    }

    public void Update(int offsetInChunk, object? value)
    {
        if (_mode == StorageMode.NullOnly && value is not null)
            ConvertNullModeToGeneric();

        if (_mode == StorageMode.StringDictionary || ShouldUseStringDictionary(value))
        {
            EnsureStringDictionaryMode();
            _stringData!.Update(offsetInChunk, value);
        }
        else if (_mode == StorageMode.ListEntries || ShouldUseListStorage(value))
        {
            EnsureListMode();
            _listData!.Update(offsetInChunk, value);
        }
        else if (_mode == StorageMode.StructRows || ShouldUseStructStorage(value))
        {
            EnsureStructMode();
            _structData!.Update(offsetInChunk, value);
        }
        else if (_mode == StorageMode.NullOnly)
        {
            if (value is not null)
                throw new NotSupportedException("Null-only chunk update expects null value.");
            _ = _nullData!.Lookup(offsetInChunk);
        }
        else
        {
            _values[offsetInChunk] = value;
        }
        RecomputeStats();
    }

    public IEnumerable<object?> Scan(int offsetInChunk, int numValues)
    {
        if (_mode == StorageMode.StringDictionary)
        {
            foreach (var value in _stringData!.Scan(offsetInChunk, numValues))
                yield return value;
            yield break;
        }
        if (_mode == StorageMode.ListEntries)
        {
            foreach (var value in _listData!.Scan(offsetInChunk, numValues))
                yield return value;
            yield break;
        }
        if (_mode == StorageMode.StructRows)
        {
            foreach (var value in _structData!.Scan(offsetInChunk, numValues))
                yield return value;
            yield break;
        }
        if (_mode == StorageMode.NullOnly)
        {
            foreach (var value in _nullData!.Scan(offsetInChunk, numValues))
                yield return value;
            yield break;
        }

        var end = Math.Min(offsetInChunk + numValues, Count);
        for (var i = offsetInChunk; i < end; i++)
            yield return _values[i];
    }

    public void Truncate(int newCount)
    {
        if (newCount < 0 || newCount > Count)
            throw new ArgumentOutOfRangeException(nameof(newCount));

        if (newCount == Count)
            return;

        switch (_mode)
        {
            case StorageMode.StringDictionary:
                _stringData!.Truncate(newCount);
                break;
            case StorageMode.ListEntries:
                _listData!.Truncate(newCount);
                break;
            case StorageMode.StructRows:
                _structData!.Truncate(newCount);
                break;
            case StorageMode.NullOnly:
                _nullData!.Truncate(newCount);
                break;
            default:
                _values.RemoveRange(newCount, _values.Count - newCount);
                break;
        }

        RecomputeStats();
    }

    public IReadOnlyList<object?> Values
    {
        get
        {
            if (_mode == StorageMode.StringDictionary)
            {
                var values = new object?[Count];
                for (var i = 0; i < values.Length; i++)
                    values[i] = _stringData!.Lookup(i);
                return values;
            }
            if (_mode == StorageMode.ListEntries)
            {
                var values = new object?[Count];
                for (var i = 0; i < values.Length; i++)
                    values[i] = _listData!.Lookup(i);
                return values;
            }
            if (_mode == StorageMode.StructRows)
            {
                var values = new object?[Count];
                for (var i = 0; i < values.Length; i++)
                    values[i] = _structData!.Lookup(i);
                return values;
            }
            if (_mode == StorageMode.NullOnly)
            {
                return new object?[Count];
            }

            return _values;
        }
    }

    public bool IsDictionaryEncodedString => _mode == StorageMode.StringDictionary;
    public bool IsListEncoded => _mode == StorageMode.ListEntries;
    public bool IsStructEncoded => _mode == StorageMode.StructRows;
    public bool IsNullEncoded => _mode == StorageMode.NullOnly;
    public int DistinctStringCount => _stringData?.DistinctCount ?? 0;
    public int ListChildValueCount => _listData?.ChildValueCount ?? 0;
    public int StructFieldCount => _structData?.FieldNames.Count ?? 0;

    private void RecomputeStats()
    {
        var snapshot = new object?[Count];
        for (var i = 0; i < Count; i++)
            snapshot[i] = Lookup(i);
        Metadata.Recompute(snapshot);
        Stats.Recompute(snapshot);
    }

    private void EnsureStringDictionaryMode()
    {
        if (_mode == StorageMode.StringDictionary)
            return;
        if (_mode == StorageMode.NullOnly)
            ConvertNullModeToGeneric();

        foreach (var existing in _values)
        {
            if (existing is null || existing is string || existing is KuString)
                continue;
            throw new NotSupportedException($"Cannot switch ColumnChunk to dictionary string mode with existing non-string value type {existing.GetType().Name}.");
        }

        _stringData = new StringChunkData(_capacity);
        foreach (var existing in _values)
            _stringData.Append(existing);

        _values.Clear();
        _mode = StorageMode.StringDictionary;
    }

    private void EnsureListMode()
    {
        if (_mode == StorageMode.ListEntries)
            return;
        if (_mode == StorageMode.NullOnly)
            ConvertNullModeToGeneric();

        foreach (var existing in _values)
        {
            if (existing is null || IsListValue(existing))
                continue;
            throw new NotSupportedException($"Cannot switch ColumnChunk to list mode with existing non-list value type {existing.GetType().Name}.");
        }

        _listData = new ListChunkData(_capacity);
        foreach (var existing in _values)
            _listData.Append(existing);

        _values.Clear();
        _mode = StorageMode.ListEntries;
    }

    private void EnsureStructMode()
    {
        if (_mode == StorageMode.StructRows)
            return;
        if (_mode == StorageMode.NullOnly)
            ConvertNullModeToGeneric();

        foreach (var existing in _values)
        {
            if (existing is null || IsStructValue(existing))
                continue;
            throw new NotSupportedException($"Cannot switch ColumnChunk to struct mode with existing non-struct value type {existing.GetType().Name}.");
        }

        _structData = new StructChunkData(_capacity);
        foreach (var existing in _values)
            _structData.Append(existing);

        _values.Clear();
        _mode = StorageMode.StructRows;
    }

    private static bool ShouldUseStringDictionary(object? value)
    {
        return value is string || value is KuString;
    }

    private static bool ShouldUseListStorage(object? value)
    {
        return IsListValue(value);
    }

    private static bool ShouldUseStructStorage(object? value)
    {
        return IsStructValue(value);
    }

    private static bool IsListValue(object? value)
    {
        return value is not null && value is IEnumerable && value is not string && value is not IDictionary;
    }

    private static bool IsStructValue(object? value)
    {
        return value is IDictionary<string, object?> || value is IDictionary<string, object> || value is IDictionary;
    }

    private void EnsureNullMode()
    {
        if (_mode == StorageMode.NullOnly)
            return;
        if (_mode != StorageMode.Generic || _values.Count != 0)
            throw new InvalidOperationException("Null mode can only be enabled on an empty generic chunk.");
        _nullData = new NullChunkData();
        _mode = StorageMode.NullOnly;
    }

    private void ConvertNullModeToGeneric()
    {
        if (_mode != StorageMode.NullOnly)
            return;

        var count = _nullData?.Count ?? 0;
        _values.Clear();
        for (var i = 0; i < count; i++)
            _values.Add(null);

        _nullData = null;
        _mode = StorageMode.Generic;
    }

    private int GetCurrentCount()
    {
        return _mode switch
        {
            StorageMode.StringDictionary => _stringData?.Count ?? 0,
            StorageMode.ListEntries => _listData?.Count ?? 0,
            StorageMode.StructRows => _structData?.Count ?? 0,
            StorageMode.NullOnly => _nullData?.Count ?? 0,
            _ => _values.Count
        };
    }
}
