using System;
using System.Collections.Generic;
using BogDb.Core.Transaction;

namespace BogDb.Core.Storage.Table;

internal sealed class VersionedPropertyStore
{
    private readonly IDictionary<string, Column> _columnStores;
    private readonly Func<int> _rowCountProvider;
    private ColumnFactory? _columnFactory;

    public VersionedPropertyStore(
        IDictionary<string, Column> columnStores,
        Func<int> rowCountProvider)
    {
        _columnStores = columnStores;
        _rowCountProvider = rowCountProvider;
    }

    internal void SetColumnFactory(ColumnFactory? factory) => _columnFactory = factory;

    public void CommitSnapshot(int rowIndex, Dictionary<string, object> properties, ulong commitTs)
    {
        foreach (var (name, columnStore) in _columnStores)
        {
            properties.TryGetValue(name, out var value);
            columnStore.SetCommittedValue(rowIndex, value, commitTs);
        }
    }

    public bool TryGetLatestCommittedSnapshotCommitTs(int rowIndex, out ulong commitTs)
    {
        commitTs = VersionInfo.InvalidVersion;
        var found = false;
        foreach (var columnStore in _columnStores.Values)
        {
            if (!columnStore.TryGetLatestCommitTs(rowIndex, out var candidate))
                continue;
            if (!found || candidate > commitTs)
            {
                commitTs = candidate;
                found = true;
            }
        }

        return found;
    }

    public Dictionary<string, object> BuildVisibleProperties(Transaction.Transaction tx, int rowIndex)
    {
        var visibleProps = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, columnStore) in _columnStores)
        {
            var value = columnStore.Lookup(tx, rowIndex);
            if (value is not null)
                visibleProps[name] = value;
        }
        return visibleProps;
    }

    public Dictionary<string, object> BuildVisibleProperties(ulong visibleCommitVersion, int rowIndex)
    {
        var visibleProps = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, columnStore) in _columnStores)
        {
            var value = columnStore.LookupCommitted(visibleCommitVersion, rowIndex);
            if (value is not null)
                visibleProps[name] = value;
        }
        return visibleProps;
    }

    public void ClearRowProperties(Transaction.Transaction tx, int rowIndex)
    {
        foreach (var columnStore in _columnStores.Values)
            columnStore.Update(tx, rowIndex, null);
    }

    public void ApplyRowProperties(
        Transaction.Transaction tx,
        int rowIndex,
        Dictionary<string, object> properties)
    {
        foreach (var (name, value) in properties)
            GetOrCreateColumnStore(name).Update(tx, rowIndex, value);
    }

    public bool HasRowColumnVersion(int rowIndex, ulong version)
    {
        foreach (var columnStore in _columnStores.Values)
        {
            if (columnStore.HasVersion(rowIndex, version))
                return true;
        }

        return false;
    }

    public void ThrowIfWriteConflict(int rowIndex, Transaction.Transaction tx, string operation)
    {
        if (TryGetLatestCommittedSnapshotCommitTs(rowIndex, out var latestCommitTs) &&
            latestCommitTs != VersionInfo.AlwaysInsertedVersion &&
            latestCommitTs > tx.StartTS)
        {
            throw new InvalidOperationException($"Write-write conflict {operation} row {rowIndex}.");
        }

        foreach (var columnStore in _columnStores.Values)
        {
            if (columnStore.HasConflict(tx, rowIndex))
                throw new InvalidOperationException($"Write-write conflict {operation} row {rowIndex}.");
        }
    }

    private Column GetOrCreateColumnStore(string name)
    {
        if (_columnStores.TryGetValue(name, out var existing))
            return existing;

        var rowCount = _rowCountProvider();
        var columnStore = _columnFactory?.CreateColumn(name, Math.Max(1024, rowCount))
            ?? new Column(name, Math.Max(1024, rowCount));
        for (var i = 0; i < rowCount; i++)
            columnStore.Append(null);
        _columnStores[name] = columnStore;
        return columnStore;
    }
}
