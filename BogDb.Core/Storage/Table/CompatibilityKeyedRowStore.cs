using System;
using System.Collections.Generic;

namespace BogDb.Core.Storage.Table;

internal sealed class CompatibilityKeyedRowStore<TKey>
    where TKey : notnull
{
    private readonly List<TKey> _rowKeys;
    private readonly IDictionary<TKey, Dictionary<string, object>> _data;
    private readonly CompatibilityColumnMirror _columnMirror;

    public CompatibilityKeyedRowStore(
        List<TKey> rowKeys,
        IDictionary<TKey, Dictionary<string, object>> data,
        CompatibilityColumnMirror columnMirror)
    {
        _rowKeys = rowKeys;
        _data = data;
        _columnMirror = columnMirror;
    }

    public void ReplaceRow(int rowIndex, Dictionary<string, object> properties)
    {
        var key = _rowKeys[rowIndex];
        _data[key] = properties;
        _columnMirror.OverwriteRow(rowIndex, _rowKeys.Count, properties);
    }

    public void TrimTail(int newCount, Action<TKey, int> onRemovingRow)
    {
        if (newCount == _rowKeys.Count)
            return;

        for (var i = _rowKeys.Count - 1; i >= newCount; i--)
        {
            var key = _rowKeys[i];
            onRemovingRow(key, i);
            _data.Remove(key);
        }

        _rowKeys.RemoveRange(newCount, _rowKeys.Count - newCount);
        _columnMirror.Truncate(newCount);
    }
}
