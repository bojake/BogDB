using System.Collections.Generic;
using BogDb.Core.Storage;
using BogDb.Core.Transaction;
using Xunit;

namespace BogDb.Tests.Transaction;

public sealed class UndoBufferManagedOperationTests
{
    [Fact]
    public void UndoBuffer_ManagedOperations_RollbackInReverseRegistrationOrder()
    {
        var undoBuffer = new UndoBuffer();
        var events = new List<string>();

        undoBuffer.RegisterManagedOperation(
            UndoRecordType.INSERT_INFO,
            () => events.Add("commit:1"),
            () => events.Add("rollback:1"));
        undoBuffer.RegisterManagedOperation(
            UndoRecordType.DELETE_INFO,
            () => events.Add("commit:2"),
            () => events.Add("rollback:2"));

        undoBuffer.RollbackManagedOperations();

        Assert.Equal(new[] { "rollback:2", "rollback:1" }, events);
    }

    [Fact]
    public void UndoBuffer_ManagedOperations_CommitInRegistrationOrder()
    {
        var undoBuffer = new UndoBuffer();
        var events = new List<string>();

        undoBuffer.RegisterManagedOperation(
            UndoRecordType.INSERT_INFO,
            () => events.Add("commit:1"),
            () => events.Add("rollback:1"));
        undoBuffer.RegisterManagedOperation(
            UndoRecordType.DELETE_INFO,
            () => events.Add("commit:2"),
            () => events.Add("rollback:2"));

        undoBuffer.CommitManagedOperations();

        Assert.Equal(new[] { "commit:1", "commit:2" }, events);
    }

    [Fact]
    public void UndoBuffer_ExecutablePayloads_ReplayWithoutDelegates()
    {
        var undoBuffer = new UndoBuffer();
        var events = new List<string>();

        undoBuffer.RegisterManagedOperation(
            UndoRecordType.INSERT_INFO,
            new RecordingPayload(events, "insert"));
        undoBuffer.RegisterManagedOperation(
            UndoRecordType.DELETE_INFO,
            new RecordingPayload(events, "delete"));

        undoBuffer.CommitManagedOperations();
        Assert.Equal(new[] { "commit:insert", "commit:delete" }, events);

        undoBuffer.RegisterManagedOperation(
            UndoRecordType.INSERT_INFO,
            new RecordingPayload(events, "insert"));
        undoBuffer.RegisterManagedOperation(
            UndoRecordType.DELETE_INFO,
            new RecordingPayload(events, "delete"));

        undoBuffer.RollbackManagedOperations();
        Assert.Equal(
            new[] { "commit:insert", "commit:delete", "rollback:delete", "rollback:insert" },
            events);
    }

    [Fact]
    public void UndoBuffer_RawVersionedRowOperations_ReplayFromMemoryBuffer()
    {
        var undoBuffer = new UndoBuffer();
        var events = new List<string>();
        var targetIds = new List<int>();
        var tx = new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 11, 7);
        tx.Commit(20);
        var target = new RecordingRawTarget(events);

        undoBuffer.RegisterVersionedRowOperation(UndoRecordType.INSERT_INFO, target, tx, 3);
        undoBuffer.RegisterVersionedRowOperation(UndoRecordType.DELETE_INFO, target, tx, 5);
        unsafe
        {
            undoBuffer.Iterate((recordType, payloadPtr, size) =>
            {
                var payload = (VersionedRowUndoRecord*)payloadPtr;
                targetIds.Add(payload->TargetId);
                Assert.NotEqual(0, payload->TargetId);
            });
        }
        Assert.Equal(targetIds[0], targetIds[1]);
        undoBuffer.CommitRawOperations(tx.CommitTS);

        Assert.Equal(new[] { "commit:INSERT_INFO:3:20", "commit:DELETE_INFO:5:20" }, events);

        undoBuffer.RegisterVersionedRowOperation(UndoRecordType.INSERT_INFO, target, tx, 3);
        undoBuffer.RegisterVersionedRowOperation(UndoRecordType.DELETE_INFO, target, tx, 5);
        undoBuffer.RollbackRawOperations();

        Assert.Equal(
            new[]
            {
                "commit:INSERT_INFO:3:20",
                "commit:DELETE_INFO:5:20",
                "rollback:DELETE_INFO:5:11",
                "rollback:INSERT_INFO:3:11"
            },
            events);
    }

    [Fact]
    public void Transaction_TracksVersionedParticipant_OncePerTransaction()
    {
        var tx = new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 11, 7);
        var participant = new RecordingParticipant();

        tx.TrackVersionedParticipant(participant);
        tx.TrackVersionedParticipant(participant);
        tx.Commit(20);
        tx.CommitVersionedChanges();

        Assert.Equal(1, participant.CommitCalls);
        Assert.Equal(20ul, participant.LastCommitTs);
    }

    [Fact]
    public void UndoBuffer_SnapshotManagedOperations_RetainsTypedPayloads()
    {
        var undoBuffer = new UndoBuffer();
        var payload = new TestPayload(17, "node");

        undoBuffer.RegisterManagedOperation(
            UndoRecordType.INSERT_INFO,
            payload,
            _ => { },
            _ => { });

        var snapshot = Assert.Single(undoBuffer.SnapshotManagedOperations());
        Assert.Equal(UndoRecordType.INSERT_INFO, snapshot.RecordType);
        var typed = Assert.IsType<TestPayload>(snapshot.Payload);
        Assert.Equal(17, typed.RowIndex);
        Assert.Equal("node", typed.Kind);
    }

    private sealed class RecordingParticipant : IVersionedTransactionParticipant
    {
        public int CommitCalls { get; private set; }
        public ulong LastCommitTs { get; private set; }

        public void CommitVersionedChanges(BogDb.Core.Transaction.Transaction tx, ulong commitTs)
        {
            CommitCalls++;
            LastCommitTs = commitTs;
        }

        public void RollbackVersionedChanges(BogDb.Core.Transaction.Transaction tx)
        {
        }
    }

    private sealed class RecordingPayload : IManagedUndoPayload
    {
        private readonly List<string> _events;
        private readonly string _name;

        public RecordingPayload(List<string> events, string name)
        {
            _events = events;
            _name = name;
        }

        public void Commit() => _events.Add($"commit:{_name}");
        public void Rollback() => _events.Add($"rollback:{_name}");
    }

    private sealed class RecordingRawTarget : IRawUndoReplayTarget
    {
        private readonly List<string> _events;

        public RecordingRawTarget(List<string> events)
        {
            _events = events;
        }

        public void CommitUndoRecord(UndoReplayContext context, UndoRecordType recordType, int rowIndex, System.ReadOnlySpan<byte> payload = default)
            => _events.Add($"commit:{recordType}:{rowIndex}:{context.CommitVersion}");

        public void RollbackUndoRecord(UndoReplayContext context, UndoRecordType recordType, int rowIndex, System.ReadOnlySpan<byte> payload = default)
            => _events.Add($"rollback:{recordType}:{rowIndex}:{context.PendingVersion}");
    }

    private readonly record struct TestPayload(int RowIndex, string Kind);
}
