using System.Collections.Generic;

namespace BogDb.Core.Storage.Table;

internal sealed class RowKeyIndex<TKey>
    where TKey : notnull
{
    private readonly List<TKey> _keys;
    private readonly Dictionary<TKey, int> _indexByKey;

    public RowKeyIndex(List<TKey> keys, Dictionary<TKey, int> indexByKey)
    {
        _keys = keys;
        _indexByKey = indexByKey;
    }

    public int Count => _keys.Count;

    public TKey this[int index] => _keys[index];

    public void Add(TKey key)
    {
        var rowIndex = _keys.Count;
        _keys.Add(key);
        _indexByKey[key] = rowIndex;
    }

    public bool TryGetIndex(TKey key, out int rowIndex)
        => _indexByKey.TryGetValue(key, out rowIndex);

    public bool ContainsKey(TKey key)
        => _indexByKey.ContainsKey(key);

    public void Clear()
    {
        _keys.Clear();
        _indexByKey.Clear();
    }

    public void Remove(TKey key)
        => _indexByKey.Remove(key);

    public (int LastIndex, TKey LastKey, bool Moved) RemoveSwap(TKey removedKey, int removeIndex)
    {
        var lastIndex = _keys.Count - 1;
        var lastKey = _keys[lastIndex];
        var moved = removeIndex != lastIndex;
        if (moved)
        {
            _keys[removeIndex] = lastKey;
            _indexByKey[lastKey] = removeIndex;
        }

        _keys.RemoveAt(lastIndex);
        _indexByKey.Remove(removedKey);
        return (lastIndex, lastKey, moved);
    }

    public bool TryFindVisibleIndex(TKey key, System.Func<int, bool> isVisible, out int rowIndex)
    {
        var preferredIndex = -1;
        if (_indexByKey.TryGetValue(key, out preferredIndex) && isVisible(preferredIndex))
        {
            rowIndex = preferredIndex;
            return true;
        }

        for (var i = _keys.Count - 1; i >= 0; i--)
        {
            if (i == preferredIndex)
                continue;
            if (!EqualityComparer<TKey>.Default.Equals(_keys[i], key))
                continue;
            if (!isVisible(i))
                continue;
            rowIndex = i;
            return true;
        }

        rowIndex = -1;
        return false;
    }
}
