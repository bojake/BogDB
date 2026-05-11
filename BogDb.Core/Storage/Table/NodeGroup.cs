using System;
using System.Collections.Generic;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// In-memory representation of a persisted node group in column-major shape.
/// </summary>
public sealed class NodeGroup
{
    private struct RowVersion
    {
        public ulong InsertVersion;
        public ulong DeleteVersion;
    }

    private readonly int _capacity;
    private readonly List<object> _nodeIds = new();
    private readonly List<RowVersion> _rowVersions = new();
    private readonly Dictionary<string, Column> _columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly ColumnFactory? _columnFactory;

    public NodeGroup(int capacity = 1024, ColumnFactory? columnFactory = null)
    {
        _capacity = capacity;
        _columnFactory = columnFactory;
    }

    public int Capacity => _capacity;
    public bool IsFull => _nodeIds.Count >= _capacity;
    public ulong GetNumRows() => (ulong)_nodeIds.Count;
    public IReadOnlyDictionary<string, Column> Columns => _columns;

    public void AppendRow(object nodeId, Dictionary<string, object> properties)
    {
        AppendRowInternal(nodeId, properties, VersionInfo.AlwaysInsertedVersion);
    }

    public void AppendRow(Transaction.Transaction tx, object nodeId, Dictionary<string, object> properties)
    {
        AppendRowInternal(nodeId, properties, tx.ID);
    }

    private void AppendRowInternal(object nodeId, Dictionary<string, object> properties, ulong insertVersion)
    {
        if (IsFull)
            throw new InvalidOperationException("NodeGroup is full.");

        var rowIdx = _nodeIds.Count;
        _nodeIds.Add(nodeId);
        _rowVersions.Add(new RowVersion
        {
            InsertVersion = insertVersion,
            DeleteVersion = VersionInfo.InvalidVersion
        });

        foreach (var col in _columns.Values)
            col.Append(null);

        foreach (var (name, value) in properties)
        {
            if (!_columns.TryGetValue(name, out var col))
            {
                col = _columnFactory?.CreateColumn(name, _capacity)
                    ?? new Column(name, _capacity);
                for (var i = 0; i < rowIdx; i++)
                    col.Append(null);
                col.Append(value);
                _columns[name] = col;
                continue;
            }
            col.Update(rowIdx, value);
        }
    }

    public void MarkDeleted(Transaction.Transaction tx, int rowIdx)
    {
        if (rowIdx < 0 || rowIdx >= _rowVersions.Count)
            throw new ArgumentOutOfRangeException(nameof(rowIdx));

        var rv = _rowVersions[rowIdx];
        if (rv.DeleteVersion == tx.ID)
            return;

        if (rv.DeleteVersion != VersionInfo.InvalidVersion && rv.DeleteVersion > tx.StartTS)
            throw new InvalidOperationException($"Write-write conflict deleting row {rowIdx}.");

        rv.DeleteVersion = tx.ID;
        _rowVersions[rowIdx] = rv;
    }

    public void CommitVersions(Transaction.Transaction tx, ulong commitTS)
    {
        for (var i = 0; i < _rowVersions.Count; i++)
        {
            var rv = _rowVersions[i];
            if (rv.InsertVersion == tx.ID)
                rv.InsertVersion = commitTS;
            if (rv.DeleteVersion == tx.ID)
                rv.DeleteVersion = commitTS;
            _rowVersions[i] = rv;
        }
    }

    public void RollbackVersions(Transaction.Transaction tx)
    {
        for (var i = 0; i < _rowVersions.Count; i++)
        {
            var rv = _rowVersions[i];
            if (rv.InsertVersion == tx.ID)
            {
                // Tombstone rolled-back inserts so they are never visible.
                rv.InsertVersion = VersionInfo.InvalidVersion;
                rv.DeleteVersion = VersionInfo.InvalidVersion;
            }
            else if (rv.DeleteVersion == tx.ID)
            {
                rv.DeleteVersion = VersionInfo.InvalidVersion;
            }
            _rowVersions[i] = rv;
        }

        TrimRolledBackTail();
    }

    public IEnumerable<KeyValuePair<object, Dictionary<string, object>>> EnumerateRows()
    {
        for (var i = 0; i < _nodeIds.Count; i++)
        {
            if (_rowVersions[i].InsertVersion == VersionInfo.InvalidVersion)
                continue;
            var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, values) in _columns)
            {
                var value = values.Lookup(i);
                if (value is not null)
                    props[name] = value;
            }
            yield return new KeyValuePair<object, Dictionary<string, object>>(_nodeIds[i], props);
        }
    }

    public IEnumerable<KeyValuePair<object, Dictionary<string, object>>> EnumerateRows(Transaction.Transaction tx)
    {
        for (var i = 0; i < _nodeIds.Count; i++)
        {
            var rv = _rowVersions[i];
            var insertVisible = VersionInfo.IsVersionVisible(tx, rv.InsertVersion);
            if (!insertVisible)
                continue;
            var deleteVisible = VersionInfo.IsVersionVisible(tx, rv.DeleteVersion);
            if (deleteVisible)
                continue;

            var props = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, values) in _columns)
            {
                var value = values.Lookup(i);
                if (value is not null)
                    props[name] = value;
            }
            yield return new KeyValuePair<object, Dictionary<string, object>>(_nodeIds[i], props);
        }
    }

    private void TrimRolledBackTail()
    {
        var newCount = _rowVersions.Count;
        while (newCount > 0 && _rowVersions[newCount - 1].InsertVersion == VersionInfo.InvalidVersion)
            newCount--;

        if (newCount == _rowVersions.Count)
            return;

        _nodeIds.RemoveRange(newCount, _nodeIds.Count - newCount);
        _rowVersions.RemoveRange(newCount, _rowVersions.Count - newCount);
        foreach (var column in _columns.Values)
            column.Truncate(newCount);
    }
}
