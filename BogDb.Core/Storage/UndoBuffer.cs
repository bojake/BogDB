using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BogDb.Core.Common;
using BogDb.Core.Transaction;

namespace BogDb.Core.Storage;

public interface IManagedUndoPayload
{
    void Commit();
    void Rollback();
}

public readonly record struct UndoReplayContext(ulong PendingVersion, ulong CommitVersion);

public interface IRawUndoReplayTarget
{
    void CommitUndoRecord(UndoReplayContext context, UndoRecordType recordType, int rowIndex, ReadOnlySpan<byte> payload = default);
    void RollbackUndoRecord(UndoReplayContext context, UndoRecordType recordType, int rowIndex, ReadOnlySpan<byte> payload = default);
}

public enum UndoRecordType : ushort
{
    CATALOG_ENTRY = 0,
    SEQUENCE_ENTRY = 1,
    UPDATE_INFO = 6,
    INSERT_INFO = 7,
    DELETE_INFO = 8,
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UndoRecordHeader
{
    public UndoRecordType RecordType;
    public uint RecordSize;
    
    public UndoRecordHeader(UndoRecordType recordType, uint recordSize)
    {
        RecordType = recordType;
        RecordSize = recordSize;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VersionedRowUndoRecord
{
    public const ulong MAGIC = 0x4B5A554E47554E47UL;

    public ulong Magic;
    public int TargetId;
    public int Reserved;
    public int RowIndex;
    public ulong PendingVersion;

    public VersionedRowUndoRecord(
        int targetId,
        ulong pendingVersion,
        int rowIndex)
    {
        Magic = MAGIC;
        TargetId = targetId;
        Reserved = 0;
        RowIndex = rowIndex;
        PendingVersion = pendingVersion;
    }
}

/// <summary>
/// Simulates UndoMemoryBuffer from C++ - holds raw memory for transaction logging.
/// </summary>
public unsafe class UndoMemoryBuffer : IDisposable
{
    public const ulong UNDO_MEMORY_BUFFER_INIT_CAPACITY = 4096; // BOGDB_PAGE_SIZE

    private IntPtr _buffer;
    public ulong Capacity { get; private set; }
    public ulong CurrentPosition { get; private set; }

    public UndoMemoryBuffer(ulong capacity = UNDO_MEMORY_BUFFER_INIT_CAPACITY)
    {
        Capacity = capacity;
        _buffer = Marshal.AllocHGlobal((int)capacity);
        CurrentPosition = 0;
    }

    public byte* GetDataUnsafe() => (byte*)_buffer.ToPointer();

    public void MoveCurrentPosition(ulong offset)
    {
        if (CurrentPosition + offset > Capacity)
            throw new InvalidOperationException("Undo buffer overflow");
        CurrentPosition += offset;
    }

    public bool CanFit(ulong size) => CurrentPosition + size <= Capacity;

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }
}

/// <summary>
/// Mirrors BogDb C++ storage::UndoBuffer
/// </summary>
public unsafe class UndoBuffer : IDisposable
{
    public sealed record ManagedUndoRecord(UndoRecordType RecordType, object? Payload);

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private interface IManagedUndoOperation
    {
        UndoRecordType RecordType { get; }
        object? Payload { get; }
        void Commit();
        void Rollback();
    }

    private sealed class ManagedUndoOperation<TPayload> : IManagedUndoOperation
    {
        private readonly Action<TPayload>? _commit;
        private readonly Action<TPayload>? _rollback;

        public ManagedUndoOperation(
            UndoRecordType recordType,
            TPayload payload,
            Action<TPayload> commit,
            Action<TPayload> rollback)
        {
            RecordType = recordType;
            PayloadValue = payload;
            _commit = commit;
            _rollback = rollback;
        }

        public ManagedUndoOperation(UndoRecordType recordType, TPayload payload)
        {
            RecordType = recordType;
            PayloadValue = payload;
        }

        public UndoRecordType RecordType { get; }
        public TPayload PayloadValue { get; }
        public object? Payload => PayloadValue;

        public void Commit()
        {
            if (_commit is not null)
            {
                _commit(PayloadValue);
                return;
            }

            if (PayloadValue is IManagedUndoPayload payload)
            {
                payload.Commit();
                return;
            }

            throw new InvalidOperationException("Managed undo payload does not support commit replay.");
        }

        public void Rollback()
        {
            if (_rollback is not null)
            {
                _rollback(PayloadValue);
                return;
            }

            if (PayloadValue is IManagedUndoPayload payload)
            {
                payload.Rollback();
                return;
            }

            throw new InvalidOperationException("Managed undo payload does not support rollback replay.");
        }
    }

    private List<UndoMemoryBuffer> _memoryBuffers;
    private readonly List<IManagedUndoOperation> _managedOperations;
    private readonly Dictionary<int, IRawUndoReplayTarget> _rawReplayTargets;
    private readonly Dictionary<IRawUndoReplayTarget, int> _rawReplayTargetIds;
    private int _nextRawReplayTargetId;
    private readonly object _mtx = new object();

    public UndoBuffer()
    {
        _memoryBuffers = new List<UndoMemoryBuffer>();
        _managedOperations = new List<IManagedUndoOperation>();
        _rawReplayTargets = new Dictionary<int, IRawUndoReplayTarget>();
        _rawReplayTargetIds = new Dictionary<IRawUndoReplayTarget, int>(ReferenceEqualityComparer<IRawUndoReplayTarget>.Instance);
        _nextRawReplayTargetId = 1;
    }

    public byte* CreateUndoRecord(ulong size)
    {
        lock (_mtx)
        {
            if (_memoryBuffers.Count == 0 || !_memoryBuffers[_memoryBuffers.Count - 1].CanFit(size))
            {
                ulong capacity = UndoMemoryBuffer.UNDO_MEMORY_BUFFER_INIT_CAPACITY;
                while (size > capacity)
                {
                    capacity *= 2;
                }
                _memoryBuffers.Add(new UndoMemoryBuffer(capacity));
            }

            var activeBuffer = _memoryBuffers[_memoryBuffers.Count - 1];
            byte* res = activeBuffer.GetDataUnsafe() + activeBuffer.CurrentPosition;
            activeBuffer.MoveCurrentPosition(size);
            return res;
        }
    }

    public void Iterate(Action<UndoRecordType, IntPtr, uint> callback)
    {
        foreach (var buffer in _memoryBuffers)
        {
            byte* current = buffer.GetDataUnsafe();
            byte* end = current + buffer.CurrentPosition;

            while (current < end)
            {
                UndoRecordHeader* header = (UndoRecordHeader*)current;
                current += sizeof(UndoRecordHeader);
                
                callback(header->RecordType, (IntPtr)current, header->RecordSize);
                
                current += header->RecordSize; // Skip the payload to get to the next record
            }
        }
    }

    public void ReverseIterate(Action<UndoRecordType, IntPtr, uint> callback)
    {
        for (int i = _memoryBuffers.Count - 1; i >= 0; i--)
        {
            var buffer = _memoryBuffers[i];
            byte* current = buffer.GetDataUnsafe();
            byte* end = current + buffer.CurrentPosition;

            var entries = new List<(UndoRecordType Type, IntPtr Payload, uint Size)>();

            while (current < end)
            {
                UndoRecordHeader* header = (UndoRecordHeader*)current;
                current += sizeof(UndoRecordHeader);
                
                entries.Add((header->RecordType, (IntPtr)current, header->RecordSize));
                
                current += header->RecordSize;
            }

            // Reverse iterate within the chunk
            for (int j = entries.Count - 1; j >= 0; j--)
            {
                callback(entries[j].Type, entries[j].Payload, entries[j].Size);
            }
        }
    }

    public void RegisterManagedOperation(UndoRecordType recordType, Action commit, Action rollback)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(rollback);

        RegisterManagedOperation<object?>(
            recordType,
            payload: null,
            _ => commit(),
            _ => rollback());
    }

    public void RegisterManagedOperation<TPayload>(
        UndoRecordType recordType,
        TPayload payload,
        Action<TPayload> commit,
        Action<TPayload> rollback)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(rollback);

        lock (_mtx)
        {
            _managedOperations.Add(new ManagedUndoOperation<TPayload>(recordType, payload, commit, rollback));
        }
    }

    public void RegisterManagedOperation<TPayload>(
        UndoRecordType recordType,
        TPayload payload)
        where TPayload : IManagedUndoPayload
    {
        lock (_mtx)
        {
            _managedOperations.Add(new ManagedUndoOperation<TPayload>(recordType, payload));
        }
    }

    public void RegisterVersionedRowOperation(
        UndoRecordType recordType,
        IRawUndoReplayTarget target,
        Transaction.Transaction tx,
        int rowIndex,
        ReadOnlySpan<byte> serializedPayload = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tx);

        var targetId = GetOrAddRawReplayTargetId(target);
        var payloadSize = (uint)(sizeof(VersionedRowUndoRecord) + serializedPayload.Length);
        var raw = CreateUndoRecord((ulong)(sizeof(UndoRecordHeader) + payloadSize));
        var header = (UndoRecordHeader*)raw;
        header->RecordType = recordType;
        header->RecordSize = payloadSize;

        var payload = (VersionedRowUndoRecord*)(raw + sizeof(UndoRecordHeader));
        *payload = new VersionedRowUndoRecord(
            targetId,
            tx.ID,
            rowIndex);
            
        if (serializedPayload.Length > 0)
        {
            var destination = new Span<byte>((byte*)(payload + 1), serializedPayload.Length);
            serializedPayload.CopyTo(destination);
        }
    }

    private int GetOrAddRawReplayTargetId(IRawUndoReplayTarget target)
    {
        lock (_mtx)
        {
            if (_rawReplayTargetIds.TryGetValue(target, out var existing))
                return existing;

            var targetId = _nextRawReplayTargetId++;
            _rawReplayTargetIds[target] = targetId;
            _rawReplayTargets[targetId] = target;
            return targetId;
        }
    }

    private bool TryReplayVersionedRowRecord(
        UndoRecordType recordType,
        IntPtr payloadPtr,
        uint recordSize,
        bool commit,
        ulong commitVersion = 0)
    {
        if (recordType is not (UndoRecordType.INSERT_INFO or UndoRecordType.DELETE_INFO or UndoRecordType.UPDATE_INFO))
            return false;

        var payload = (VersionedRowUndoRecord*)payloadPtr;
        if (payload->Magic != VersionedRowUndoRecord.MAGIC)
            return false;

        if (!_rawReplayTargets.TryGetValue(payload->TargetId, out var target))
            throw new InvalidOperationException("Undo record target is unavailable.");

        var context = new UndoReplayContext(
            payload->PendingVersion,
            commitVersion);

        int structSize = sizeof(VersionedRowUndoRecord);
        int trailingLength = (int)recordSize - structSize;
        ReadOnlySpan<byte> trailingPayload = trailingLength > 0 
            ? new ReadOnlySpan<byte>((byte*)payload + structSize, trailingLength) 
            : default;

        if (commit)
            target.CommitUndoRecord(context, recordType, payload->RowIndex, trailingPayload);
        else
            target.RollbackUndoRecord(context, recordType, payload->RowIndex, trailingPayload);

        return true;
    }

    public void CommitRawOperations(ulong commitVersion)
    {
        Iterate((recordType, payloadPtr, size) => TryReplayVersionedRowRecord(recordType, payloadPtr, size, commit: true, commitVersion));
        ClearRawOperations();
    }

    public void RollbackRawOperations()
    {
        ReverseIterate((recordType, payloadPtr, size) => TryReplayVersionedRowRecord(recordType, payloadPtr, size, commit: false));
        ClearRawOperations();
    }

    private void ClearRawOperations()
    {
        foreach (var buffer in _memoryBuffers)
            buffer.Dispose();
        _memoryBuffers.Clear();
        _rawReplayTargets.Clear();
        _rawReplayTargetIds.Clear();
        _nextRawReplayTargetId = 1;
    }

    public void CommitManagedOperations()
    {
        List<IManagedUndoOperation> operations;
        lock (_mtx)
        {
            operations = new List<IManagedUndoOperation>(_managedOperations);
            _managedOperations.Clear();
        }

        foreach (var operation in operations)
            operation.Commit();
    }

    public void RollbackManagedOperations()
    {
        List<IManagedUndoOperation> operations;
        lock (_mtx)
        {
            operations = new List<IManagedUndoOperation>(_managedOperations);
            _managedOperations.Clear();
        }

        for (var i = operations.Count - 1; i >= 0; i--)
            operations[i].Rollback();
    }

    public IReadOnlyList<ManagedUndoRecord> SnapshotManagedOperations()
    {
        lock (_mtx)
        {
            var snapshot = new List<ManagedUndoRecord>(_managedOperations.Count);
            foreach (var operation in _managedOperations)
                snapshot.Add(new ManagedUndoRecord(operation.RecordType, operation.Payload));
            return snapshot;
        }
    }

    public void Dispose()
    {
        foreach (var buffer in _memoryBuffers)
        {
            buffer.Dispose();
        }
        _memoryBuffers.Clear();
        _managedOperations.Clear();
    }
}
