using System;
using System.Collections.Generic;
using BogDb.Core.Storage;

namespace BogDb.Core.Storage.Table;

internal interface IRowUndoReplayHost
{
    void ReplaceCommittedRow(int rowIndex, Dictionary<string, object> properties);
    void TrimRolledBackTail();
    void PersistCommittedUpdate(int rowIndex, Dictionary<string, object> properties);
    void PersistCommittedDelete(int rowIndex);
}

internal sealed class RowUndoReplayHandler : IRawUndoReplayTarget
{
    private readonly RowVersionStore _rowVersions;
    private readonly IDictionary<string, Column> _columnStores;
    private readonly IRowUndoReplayHost _host;

    public RowUndoReplayHandler(
        RowVersionStore rowVersions,
        IDictionary<string, Column> columnStores,
        IRowUndoReplayHost host)
    {
        _rowVersions = rowVersions;
        _columnStores = columnStores;
        _host = host;
    }

    public void CommitUndoRecord(UndoReplayContext context, UndoRecordType recordType, int rowIndex, ReadOnlySpan<byte> payload = default)
    {
        switch (recordType)
        {
            case UndoRecordType.INSERT_INFO:
                CommitInsertedRow(context, rowIndex);
                break;
            case UndoRecordType.DELETE_INFO:
                CommitDeletedRow(context, rowIndex);
                break;
            case UndoRecordType.UPDATE_INFO:
                CommitUpdatedRow(context, rowIndex);
                break;
            default:
                throw new InvalidOperationException($"Unsupported undo record type: {recordType}.");
        }
    }

    public void RollbackUndoRecord(UndoReplayContext context, UndoRecordType recordType, int rowIndex, ReadOnlySpan<byte> payload = default)
    {
        switch (recordType)
        {
            case UndoRecordType.INSERT_INFO:
                RollbackInsertedRow(context, rowIndex);
                break;
            case UndoRecordType.DELETE_INFO:
                RollbackDeletedRow(context, rowIndex);
                break;
            case UndoRecordType.UPDATE_INFO:
                RollbackUpdatedRow(context, rowIndex);
                break;
            default:
                throw new InvalidOperationException($"Unsupported undo record type: {recordType}.");
        }
    }

    private void CommitInsertedRow(UndoReplayContext context, int rowIndex)
    {
        if (rowIndex >= _rowVersions.Count || !_rowVersions.CommitInsert(context, rowIndex))
            return;

        var rowVersion = _rowVersions.Get(rowIndex);
        if (rowVersion.DeleteVersion == context.PendingVersion || HasColumnVersion(rowIndex, context.PendingVersion))
            return;

        var properties = BuildVisibleProperties(context.CommitVersion, rowIndex);
        CommitColumnSnapshot(rowIndex, properties, context.CommitVersion);
    }

    private void RollbackInsertedRow(UndoReplayContext context, int rowIndex)
    {
        if (rowIndex >= _rowVersions.Count || !_rowVersions.RollbackInsert(context, rowIndex))
            return;

        _host.TrimRolledBackTail();
    }

    private void CommitDeletedRow(UndoReplayContext context, int rowIndex)
    {
        if (rowIndex < _rowVersions.Count)
        {
            if (_rowVersions.CommitDelete(context, rowIndex))
                _host.PersistCommittedDelete(rowIndex);
        }
    }

    private void RollbackDeletedRow(UndoReplayContext context, int rowIndex)
    {
        if (rowIndex < _rowVersions.Count)
            _rowVersions.RollbackDelete(context, rowIndex);
    }

    private void CommitUpdatedRow(UndoReplayContext context, int rowIndex)
    {
        if (rowIndex >= _rowVersions.Count)
            return;

        var changed = false;
        foreach (var columnStore in _columnStores.Values)
            changed |= columnStore.CommitRowUpdate(context, rowIndex);

        if (!changed)
            return;

        var properties = BuildVisibleProperties(context.CommitVersion, rowIndex);
        var rowVersion = _rowVersions.Get(rowIndex);
        if (rowVersion.InsertVersion == context.CommitVersion && rowVersion.DeleteVersion != context.PendingVersion)
            CommitColumnSnapshot(rowIndex, properties, context.CommitVersion);
        _host.ReplaceCommittedRow(rowIndex, properties);
        _host.PersistCommittedUpdate(rowIndex, properties);
    }

    private void RollbackUpdatedRow(UndoReplayContext context, int rowIndex)
    {
        if (rowIndex >= _rowVersions.Count)
            return;

        foreach (var columnStore in _columnStores.Values)
            columnStore.RollbackRowUpdate(context, rowIndex);
    }

    private bool HasColumnVersion(int rowIndex, ulong version)
    {
        foreach (var columnStore in _columnStores.Values)
        {
            if (columnStore.HasVersion(rowIndex, version))
                return true;
        }

        return false;
    }

    private Dictionary<string, object> BuildVisibleProperties(ulong visibleCommitVersion, int rowIndex)
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

    private void CommitColumnSnapshot(int rowIndex, Dictionary<string, object> properties, ulong commitVersion)
    {
        foreach (var (name, columnStore) in _columnStores)
        {
            properties.TryGetValue(name, out var value);
            columnStore.SetCommittedValue(rowIndex, value, commitVersion);
        }
    }
}
