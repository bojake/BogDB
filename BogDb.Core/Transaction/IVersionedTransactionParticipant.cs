namespace BogDb.Core.Transaction;

public interface IVersionedTransactionParticipant
{
    void CommitVersionedChanges(Transaction tx, ulong commitTs);
    void RollbackVersionedChanges(Transaction tx);
}
