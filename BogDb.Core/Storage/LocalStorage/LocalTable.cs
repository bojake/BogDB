using System;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.LocalStorage
{
    public struct TableInsertState
    {
        // Add required fields later
    }

    public struct TableUpdateState
    {
        // Add required fields later
    }

    public struct TableDeleteState
    {
        // Add required fields later
    }

    public struct TableAddColumnState
    {
        // Add required fields later
    }

    /// <summary>
    /// LocalTable manages local, uncommitted inserts/updates/deletes for a transaction.
    /// This is the backbone of BogDb's MVCC concurrency model before data is flushed to persistent storage.
    /// </summary>
    public abstract class LocalTable
    {
        protected LocalTable()
        {
        }

        public abstract bool Insert(Transaction.Transaction transaction, ref TableInsertState insertState);
        public abstract bool Update(Transaction.Transaction transaction, ref TableUpdateState updateState);
        public abstract bool Delete(Transaction.Transaction transaction, ref TableDeleteState deleteState);
        public abstract bool AddColumn(ref TableAddColumnState addColumnState);
        
        public abstract void Clear();
        public abstract TableType GetTableType();
        public abstract ulong GetNumTotalRows();

        public T Cast<T>() where T : LocalTable
        {
            return (T)this;
        }
    }
}
