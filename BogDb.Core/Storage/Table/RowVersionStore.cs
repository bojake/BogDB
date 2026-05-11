using System;
using System.Collections.Generic;
using BogDb.Core.Transaction;

namespace BogDb.Core.Storage.Table;

public readonly record struct RowVersionState(ulong InsertVersion, ulong DeleteVersion);

public sealed class RowVersionStore
{
    private readonly List<RowVersionState> _rows = new();

    public int Count => _rows.Count;

    public RowVersionState Get(int rowIndex) => _rows[rowIndex];

    public void Set(int rowIndex, RowVersionState state) => _rows[rowIndex] = state;

    public void Add(ulong insertVersion)
        => _rows.Add(new RowVersionState(insertVersion, VersionInfo.InvalidVersion));

    public void Copy(int targetRowIndex, int sourceRowIndex)
        => _rows[targetRowIndex] = _rows[sourceRowIndex];

    public void RemoveLast()
        => _rows.RemoveAt(_rows.Count - 1);

    public void Clear()
        => _rows.Clear();

    public void RemoveRange(int index, int count)
        => _rows.RemoveRange(index, count);

    public bool IsVisible(Transaction.Transaction tx, int rowIndex)
    {
        var state = _rows[rowIndex];
        return VersionInfo.IsVersionVisible(tx, state.InsertVersion) &&
               !VersionInfo.IsVersionVisible(tx, state.DeleteVersion);
    }

    public bool IsCommittedVisible(int rowIndex)
    {
        var state = _rows[rowIndex];
        return state.InsertVersion != VersionInfo.InvalidVersion &&
               state.DeleteVersion == VersionInfo.InvalidVersion;
    }

    public void Commit(Transaction.Transaction tx, ulong commitTs, ISet<int>? insertedRows = null)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var state = _rows[i];
            if (state.InsertVersion == tx.ID)
            {
                insertedRows?.Add(i);
                state = state with { InsertVersion = commitTs };
            }

            if (state.DeleteVersion == tx.ID)
                state = state with { DeleteVersion = commitTs };

            _rows[i] = state;
        }
    }

    public bool CommitInsert(Transaction.Transaction tx, int rowIndex, ulong commitTs)
    {
        var state = _rows[rowIndex];
        if (state.InsertVersion != tx.ID)
            return false;

        _rows[rowIndex] = state with { InsertVersion = commitTs };
        return true;
    }

    public bool CommitInsert(UndoReplayContext context, int rowIndex)
    {
        var state = _rows[rowIndex];
        if (state.InsertVersion != context.PendingVersion)
            return false;

        _rows[rowIndex] = state with { InsertVersion = context.CommitVersion };
        return true;
    }

    public bool CommitDelete(Transaction.Transaction tx, int rowIndex, ulong commitTs)
    {
        var state = _rows[rowIndex];
        if (state.DeleteVersion != tx.ID)
            return false;

        _rows[rowIndex] = state with { DeleteVersion = commitTs };
        return true;
    }

    public bool CommitDelete(UndoReplayContext context, int rowIndex)
    {
        var state = _rows[rowIndex];
        if (state.DeleteVersion != context.PendingVersion)
            return false;

        _rows[rowIndex] = state with { DeleteVersion = context.CommitVersion };
        return true;
    }

    public void Rollback(Transaction.Transaction tx)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var state = _rows[i];
            if (state.InsertVersion == tx.ID)
            {
                state = state with
                {
                    InsertVersion = VersionInfo.InvalidVersion,
                    DeleteVersion = VersionInfo.InvalidVersion
                };
            }
            else if (state.DeleteVersion == tx.ID)
            {
                state = state with { DeleteVersion = VersionInfo.InvalidVersion };
            }

            _rows[i] = state;
        }
    }

    public bool RollbackInsert(Transaction.Transaction tx, int rowIndex)
    {
        var state = _rows[rowIndex];
        if (state.InsertVersion != tx.ID)
            return false;

        _rows[rowIndex] = state with
        {
            InsertVersion = VersionInfo.InvalidVersion,
            DeleteVersion = VersionInfo.InvalidVersion
        };
        return true;
    }

    public bool RollbackInsert(UndoReplayContext context, int rowIndex)
    {
        var state = _rows[rowIndex];
        if (state.InsertVersion != context.PendingVersion)
            return false;

        _rows[rowIndex] = state with
        {
            InsertVersion = VersionInfo.InvalidVersion,
            DeleteVersion = VersionInfo.InvalidVersion
        };
        return true;
    }

    public bool RollbackDelete(Transaction.Transaction tx, int rowIndex)
    {
        var state = _rows[rowIndex];
        if (state.DeleteVersion != tx.ID)
            return false;

        _rows[rowIndex] = state with { DeleteVersion = VersionInfo.InvalidVersion };
        return true;
    }

    public bool RollbackDelete(UndoReplayContext context, int rowIndex)
    {
        var state = _rows[rowIndex];
        if (state.DeleteVersion != context.PendingVersion)
            return false;

        _rows[rowIndex] = state with { DeleteVersion = VersionInfo.InvalidVersion };
        return true;
    }

    public int GetTrimmedRowCount()
    {
        var newCount = _rows.Count;
        while (newCount > 0 && _rows[newCount - 1].InsertVersion == VersionInfo.InvalidVersion)
            newCount--;

        return newCount;
    }
}
