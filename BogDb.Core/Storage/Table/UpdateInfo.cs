using System;
using System.Collections.Generic;
using BogDb.Core.Transaction;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Transaction-versioned row updates for a column chunk/column.
/// Mirrors the C++ update-info chain model at a simplified row granularity.
/// </summary>
public sealed class UpdateInfo
{
    private sealed class VersionNode
    {
        public ulong Version;
        public object? Value;
        public VersionNode? Prev;
        public VersionNode? Next;

        public VersionNode(ulong version, object? value)
        {
            Version = version;
            Value = value;
        }
    }

    private readonly Dictionary<long, VersionNode> _heads = new();
    private readonly object _lock = new();

    public void Update(Transaction.Transaction tx, long rowOffset, object? value)
    {
        if (rowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        lock (_lock)
        {
            _heads.TryGetValue(rowOffset, out var head);
            VersionNode? current = head;
            VersionNode? sameTx = null;

            while (current is not null)
            {
                if (current.Version == tx.ID)
                {
                    sameTx = current;
                    break;
                }

                if (current.Version > tx.StartTS)
                {
                    throw new InvalidOperationException(
                        $"Write-write conflict updating row {rowOffset}.");
                }
                current = current.Prev;
            }

            if (sameTx is not null)
            {
                sameTx.Value = value;
                return;
            }

            var newHead = new VersionNode(tx.ID, value)
            {
                Prev = head
            };
            if (head is not null)
                head.Next = newHead;
            _heads[rowOffset] = newHead;
        }
    }

    public bool TryLookup(Transaction.Transaction tx, long rowOffset, out object? value)
    {
        lock (_lock)
        {
            value = null;
            if (!_heads.TryGetValue(rowOffset, out var head))
                return false;

            var current = head;
            while (current is not null)
            {
                if (IsVisible(tx, current.Version))
                {
                    value = current.Value;
                    return true;
                }
                current = current.Prev;
            }
            return false;
        }
    }

    public void Commit(Transaction.Transaction tx, ulong commitTS)
    {
        lock (_lock)
        {
            foreach (var head in _heads.Values)
            {
                var current = head;
                while (current is not null)
                {
                    if (current.Version == tx.ID)
                        current.Version = commitTS;
                    current = current.Prev;
                }
            }
        }
    }

    public bool CommitRow(Transaction.Transaction tx, long rowOffset, ulong commitTS)
    {
        if (rowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        lock (_lock)
        {
            if (!_heads.TryGetValue(rowOffset, out var head))
                return false;

            var changed = false;
            var current = head;
            while (current is not null)
            {
                if (current.Version == tx.ID)
                {
                    current.Version = commitTS;
                    changed = true;
                }
                current = current.Prev;
            }

            return changed;
        }
    }

    public bool CommitRow(UndoReplayContext context, long rowOffset)
    {
        if (rowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        lock (_lock)
        {
            if (!_heads.TryGetValue(rowOffset, out var head))
                return false;

            var changed = false;
            var current = head;
            while (current is not null)
            {
                if (current.Version == context.PendingVersion)
                {
                    current.Version = context.CommitVersion;
                    changed = true;
                }
                current = current.Prev;
            }

            return changed;
        }
    }

    public void SetCommittedValue(long rowOffset, object? value, ulong commitTS)
    {
        if (rowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        lock (_lock)
        {
            _heads.TryGetValue(rowOffset, out var head);
            if (head is not null && head.Version == commitTS)
            {
                head.Value = value;
                return;
            }

            var newHead = new VersionNode(commitTS, value)
            {
                Prev = head
            };
            if (head is not null)
                head.Next = newHead;
            _heads[rowOffset] = newHead;
        }
    }

    public void Rollback(Transaction.Transaction tx)
    {
        lock (_lock)
        {
            var toRemove = new List<long>();
            foreach (var kvp in _heads)
            {
                var row = kvp.Key;
                VersionNode? current = kvp.Value;
                VersionNode? newHead = kvp.Value;
                while (current is not null)
                {
                    var prev = current.Prev;
                    if (current.Version == tx.ID)
                    {
                        if (current.Next is not null)
                            current.Next.Prev = prev;
                        if (prev is not null)
                            prev.Next = current.Next;
                        else
                            newHead = current.Next;
                    }
                    current = prev;
                }
                if (newHead is null)
                    toRemove.Add(row);
                else
                    _heads[row] = newHead;
            }

            foreach (var row in toRemove)
                _heads.Remove(row);
        }
    }

    public bool RollbackRow(Transaction.Transaction tx, long rowOffset)
    {
        if (rowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        lock (_lock)
        {
            if (!_heads.TryGetValue(rowOffset, out var head))
                return false;

            var changed = false;
            VersionNode? current = head;
            VersionNode? newHead = head;
            while (current is not null)
            {
                var prev = current.Prev;
                if (current.Version == tx.ID)
                {
                    changed = true;
                    if (current.Next is not null)
                        current.Next.Prev = prev;
                    if (prev is not null)
                        prev.Next = current.Next;
                    else
                        newHead = current.Next;
                }
                current = prev;
            }

            if (newHead is null)
                _heads.Remove(rowOffset);
            else
                _heads[rowOffset] = newHead;

            return changed;
        }
    }

    public bool RollbackRow(UndoReplayContext context, long rowOffset)
    {
        if (rowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        lock (_lock)
        {
            if (!_heads.TryGetValue(rowOffset, out var head))
                return false;

            var changed = false;
            VersionNode? current = head;
            VersionNode? newHead = head;
            while (current is not null)
            {
                var prev = current.Prev;
                if (current.Version == context.PendingVersion)
                {
                    changed = true;
                    if (current.Next is not null)
                        current.Next.Prev = prev;
                    if (prev is not null)
                        prev.Next = current.Next;
                    else
                        newHead = current.Next;
                }
                current = prev;
            }

            if (newHead is null)
                _heads.Remove(rowOffset);
            else
                _heads[rowOffset] = newHead;

            return changed;
        }
    }

    public bool TryGetLatestCommitTs(long rowOffset, out ulong commitTS)
    {
        if (rowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        lock (_lock)
        {
            commitTS = 0;
            if (!_heads.TryGetValue(rowOffset, out var head))
                return false;

            var current = head;
            while (current is not null)
            {
                if (current.Version < Transaction.Transaction.START_TRANSACTION_ID)
                {
                    commitTS = current.Version;
                    return true;
                }

                current = current.Prev;
            }

            return false;
        }
    }

    public bool TryLookupCommitted(ulong visibleCommitVersion, long rowOffset, out object? value)
    {
        lock (_lock)
        {
            value = null;
            if (!_heads.TryGetValue(rowOffset, out var head))
                return false;

            var current = head;
            while (current is not null)
            {
                if (IsVisibleCommitted(visibleCommitVersion, current.Version))
                {
                    value = current.Value;
                    return true;
                }
                current = current.Prev;
            }
            return false;
        }
    }

    public bool HasVersion(long rowOffset, ulong version)
    {
        if (rowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        lock (_lock)
        {
            if (!_heads.TryGetValue(rowOffset, out var head))
                return false;

            var current = head;
            while (current is not null)
            {
                if (current.Version == version)
                    return true;
                current = current.Prev;
            }

            return false;
        }
    }

    public bool HasConflict(Transaction.Transaction tx, long rowOffset)
    {
        if (rowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        lock (_lock)
        {
            if (!_heads.TryGetValue(rowOffset, out var head))
                return false;

            var current = head;
            while (current is not null)
            {
                if (current.Version != tx.ID && current.Version > tx.StartTS)
                    return true;
                current = current.Prev;
            }

            return false;
        }
    }

    public void MoveRow(long fromRowOffset, long toRowOffset)
    {
        if (fromRowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(fromRowOffset));
        if (toRowOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(toRowOffset));
        if (fromRowOffset == toRowOffset)
            return;

        lock (_lock)
        {
            if (_heads.TryGetValue(fromRowOffset, out var head))
                _heads[toRowOffset] = head;
            else
                _heads.Remove(toRowOffset);

            _heads.Remove(fromRowOffset);
        }
    }

    public void Truncate(long newCount)
    {
        if (newCount < 0)
            throw new ArgumentOutOfRangeException(nameof(newCount));

        lock (_lock)
        {
            var toRemove = new List<long>();
            foreach (var rowOffset in _heads.Keys)
            {
                if (rowOffset >= newCount)
                    toRemove.Add(rowOffset);
            }

            foreach (var rowOffset in toRemove)
                _heads.Remove(rowOffset);
        }
    }

    private static bool IsVisible(Transaction.Transaction tx, ulong version)
    {
        return version == tx.ID ||
               (version < Transaction.Transaction.START_TRANSACTION_ID && version <= tx.StartTS);
    }

    private static bool IsVisibleCommitted(ulong visibleCommitVersion, ulong version) =>
        version < Transaction.Transaction.START_TRANSACTION_ID && version <= visibleCommitVersion;
}
