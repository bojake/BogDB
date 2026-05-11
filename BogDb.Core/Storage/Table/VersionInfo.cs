using BogDb.Core.Transaction;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Simplified row version visibility metadata (insert/delete) for table-layer MVCC.
/// </summary>
public static class VersionInfo
{
    public const ulong AlwaysInsertedVersion = 0;
    public const ulong InvalidVersion = ulong.MaxValue;

    public static bool IsVersionVisible(Transaction.Transaction tx, ulong version)
    {
        if (version == InvalidVersion)
            return false;
        if (version == AlwaysInsertedVersion)
            return true;
        if (version == tx.ID)
            return true;
        return version < Transaction.Transaction.START_TRANSACTION_ID && version <= tx.StartTS;
    }
}
