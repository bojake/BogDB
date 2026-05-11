using System;
using System.Collections.Generic;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Column composed of fixed-capacity chunks.
/// Supports two storage backends:
///   1. In-memory (default): List<ColumnChunk> backed by managed arrays
///   2. Page-backed: PageBackedColumn using FileHandle for disk-backed pages
/// </summary>
public sealed class Column : IColumnReader, IColumnWriter
{
    private readonly List<ColumnChunk> _chunks = new();
    private readonly int _chunkCapacity;
    private long _numValues;
    private readonly UpdateInfo _updateInfo = new();
    private readonly PageBackedColumn? _pageBacking;

    public Column(string name, int chunkCapacity = 1024)
    {
        Name = name;
        _chunkCapacity = chunkCapacity;
        _numValues = 0;
        _pageBacking = null;
    }

    /// <summary>
    /// Creates a column with page-backed storage.
    /// All data operations delegate to the PageBackedColumn while
    /// the UpdateInfo version chain layer provides MVCC on top.
    /// </summary>
    public Column(string name, PageBackedColumn pageBacking, int chunkCapacity = 1024)
    {
        Name = name;
        _chunkCapacity = chunkCapacity;
        _pageBacking = pageBacking ?? throw new ArgumentNullException(nameof(pageBacking));
        _numValues = pageBacking.Count;
    }

    public string Name { get; }
    public long Count => _pageBacking != null ? _pageBacking.Count : _numValues;
    public int NumChunks => _chunks.Count;
    public IReadOnlyList<ColumnChunk> Chunks => _chunks;
    public bool IsPageBacked => _pageBacking != null;

    public void Append(object? value)
    {
        if (_pageBacking != null)
        {
            _pageBacking.Append(value);
            return;
        }
        var chunk = GetOrCreateWritableChunk();
        chunk.Append(value);
        _numValues++;
    }

    public object? Lookup(long rowOffset)
    {
        if (_pageBacking != null)
            return _pageBacking.Lookup(rowOffset);
        var (chunk, offset) = ResolveOffset(rowOffset);
        return chunk.Lookup(offset);
    }

    public object? Lookup(Transaction.Transaction tx, long rowOffset)
    {
        if (_updateInfo.TryLookup(tx, rowOffset, out var updated))
            return updated;
        if (_updateInfo.TryLookupCommitted(tx.StartTS, rowOffset, out var committed))
            return committed;
        return Lookup(rowOffset);
    }

    public object? LookupCommitted(ulong visibleCommitVersion, long rowOffset)
    {
        if (_updateInfo.TryLookupCommitted(visibleCommitVersion, rowOffset, out var updated))
            return updated;
        return Lookup(rowOffset);
    }

    public void Update(long rowOffset, object? value)
    {
        if (_pageBacking != null)
        {
            _pageBacking.Update(rowOffset, value);
            return;
        }
        var (chunk, offset) = ResolveOffset(rowOffset);
        chunk.Update(offset, value);
    }

    /// <summary>
    /// WAL recovery: directly sets a value at a row offset, bypassing MVCC.
    /// Safe to call during replay when no concurrent transactions are active.
    /// </summary>
    public void DirectSet(long rowOffset, object? value)
    {
        if (rowOffset < 0 || rowOffset >= Count)
            return; // Silently skip out-of-range during recovery
        Update(rowOffset, value);
    }

    public void Update(Transaction.Transaction tx, long rowOffset, object? value)
    {
        if (rowOffset < 0 || rowOffset >= Count)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));
        _updateInfo.Update(tx, rowOffset, value);
    }

    public IEnumerable<object?> Scan(long startOffset, long numValues)
    {
        if (_pageBacking != null)
            return _pageBacking.Scan(startOffset, numValues);

        if (startOffset < 0 || numValues <= 0)
            return Array.Empty<object?>();

        return ScanInMemory(startOffset, numValues);
    }

    private IEnumerable<object?> ScanInMemory(long startOffset, long numValues)
    {
        var endOffset = Math.Min(startOffset + numValues, _numValues);
        for (var i = startOffset; i < endOffset; i++)
            yield return Lookup(i);
    }

    public IEnumerable<object?> Scan(Transaction.Transaction tx, long startOffset, long numValues)
    {
        if (startOffset < 0 || numValues <= 0)
            yield break;

        var endOffset = Math.Min(startOffset + numValues, _numValues);
        for (var i = startOffset; i < endOffset; i++)
            yield return Lookup(tx, i);
    }

    public void CommitUpdates(Transaction.Transaction tx, ulong commitTS) =>
        _updateInfo.Commit(tx, commitTS);

    public bool CommitRowUpdate(Transaction.Transaction tx, long rowOffset, ulong commitTS) =>
        _updateInfo.CommitRow(tx, rowOffset, commitTS);

    public bool CommitRowUpdate(UndoReplayContext context, long rowOffset) =>
        _updateInfo.CommitRow(context, rowOffset);

    public void RollbackUpdates(Transaction.Transaction tx) =>
        _updateInfo.Rollback(tx);

    public bool RollbackRowUpdate(Transaction.Transaction tx, long rowOffset) =>
        _updateInfo.RollbackRow(tx, rowOffset);

    public bool RollbackRowUpdate(UndoReplayContext context, long rowOffset) =>
        _updateInfo.RollbackRow(context, rowOffset);

    public void SetCommittedValue(long rowOffset, object? value, ulong commitTS)
    {
        Update(rowOffset, value);
        _updateInfo.SetCommittedValue(rowOffset, value, commitTS);
    }

    public bool TryGetLatestCommitTs(long rowOffset, out ulong commitTS) =>
        _updateInfo.TryGetLatestCommitTs(rowOffset, out commitTS);

    public bool HasVersion(long rowOffset, ulong version) =>
        _updateInfo.HasVersion(rowOffset, version);

    public bool HasConflict(Transaction.Transaction tx, long rowOffset) =>
        _updateInfo.HasConflict(tx, rowOffset);

    public void MoveRow(long fromRowOffset, long toRowOffset)
    {
        if (fromRowOffset < 0 || fromRowOffset >= _numValues)
            throw new ArgumentOutOfRangeException(nameof(fromRowOffset));
        if (toRowOffset < 0 || toRowOffset >= _numValues)
            throw new ArgumentOutOfRangeException(nameof(toRowOffset));
        if (fromRowOffset == toRowOffset)
            return;

        Update(toRowOffset, Lookup(fromRowOffset));
        _updateInfo.MoveRow(fromRowOffset, toRowOffset);
    }

    public void Truncate(long newCount)
    {
        if (_pageBacking != null)
        {
            _pageBacking.Truncate(newCount);
            _updateInfo.Truncate(newCount);
            return;
        }

        if (newCount < 0 || newCount > _numValues)
            throw new ArgumentOutOfRangeException(nameof(newCount));

        if (newCount == _numValues)
            return;

        var targetChunkCount = newCount == 0
            ? 0
            : (int)((newCount + _chunkCapacity - 1) / _chunkCapacity);

        while (_chunks.Count > targetChunkCount)
            _chunks.RemoveAt(_chunks.Count - 1);

        if (targetChunkCount > 0)
        {
            var finalChunkCount = (int)(newCount - ((long)(targetChunkCount - 1) * _chunkCapacity));
            _chunks[^1].Truncate(finalChunkCount);
        }

        _numValues = newCount;
        _updateInfo.Truncate(newCount);
    }

    private ColumnChunk GetOrCreateWritableChunk()
    {
        if (_chunks.Count == 0 || _chunks[^1].IsFull)
            _chunks.Add(new ColumnChunk(_numValues, _chunkCapacity));

        return _chunks[^1];
    }

    private (ColumnChunk chunk, int offsetInChunk) ResolveOffset(long rowOffset)
    {
        if (rowOffset < 0 || rowOffset >= _numValues)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        var chunkIdx = (int)(rowOffset / _chunkCapacity);
        var offsetInChunk = (int)(rowOffset % _chunkCapacity);
        return (_chunks[chunkIdx], offsetInChunk);
    }
}
