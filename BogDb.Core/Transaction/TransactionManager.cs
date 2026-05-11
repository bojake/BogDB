using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BogDb.Core.Main;
using BogDb.Core.Storage;
using BogDb.Core.Storage.Store;

namespace BogDb.Core.Transaction
{
    public class TransactionManagerException : Exception
    {
        public TransactionManagerException(string message) : base(message) { }
    }

    public class TransactionManager
    {
        private readonly object _mtxForSerializingPublicFunctionCalls = new object();
        private readonly object _mtxForStartingNewTransactions = new object();

        private ulong _lastTransactionID;
        private ulong _lastTimestamp;
        private readonly List<Transaction> _activeTransactions = new List<Transaction>();
        private readonly ulong _checkpointWaitTimeoutInMicros = 5000000; // 5s BogDb default
        private readonly StorageManager _storageManager;

        public TransactionManager(StorageManager storageManager)
        {
            _storageManager = storageManager;
            _lastTransactionID = Transaction.START_TRANSACTION_ID;
            _lastTimestamp = 1;
        }

        public Transaction BeginTransaction(ClientContext clientContext, TransactionType type)
        {
            lock (_mtxForSerializingPublicFunctionCalls)
            {
                lock (_mtxForStartingNewTransactions)
                {
                    switch (type)
                    {
                        case TransactionType.READ_ONLY:
                        {
                            var transaction = new Transaction(type, ++_lastTransactionID, _lastTimestamp);
                            _activeTransactions.Add(transaction);
                            return transaction;
                        }
                        case TransactionType.RECOVERY:
                        case TransactionType.WRITE:
                        {
                            if (HasActiveWriteTransactionNoLock())
                            {
                                throw new TransactionManagerException(
                                    "Cannot start a new write transaction in the system. " +
                                    "Only one write transaction at a time is allowed in the system.");
                            }
                            var transaction = new Transaction(type, ++_lastTransactionID, _lastTimestamp);
                            _activeTransactions.Add(transaction);
                            
                            // Log BEGIN_TRANSACTION to the WAL (C++ parity: wal_replayer.cpp)
                            if (type == TransactionType.WRITE)
                            {
                                _storageManager.GetWAL().LogBeginTransaction();
                            }
                            
                            return transaction;
                        }
                        default:
                            throw new TransactionManagerException("Invalid transaction type to begin transaction.");
                    }
                }
            }
        }

        public void Commit(ClientContext clientContext, Transaction transaction)
        {
            lock (_mtxForSerializingPublicFunctionCalls)
            {
                // In BogDb, clientContext.cleanUp() is called here
                switch (transaction.Type)
                {
                    case TransactionType.READ_ONLY:
                    {
                        ClearTransactionNoLock(transaction.ID);
                        break;
                    }
                    case TransactionType.RECOVERY:
                    case TransactionType.WRITE:
                    {
                        _lastTimestamp++;
                        transaction.Commit(_lastTimestamp);
                        transaction.CommitVersionedChanges();
                        _storageManager.GetWAL().LogAndFlushCommit(
                            transaction,
                            _storageManager.GetGraphLogFileSize());
                        
                        var shouldCheckpoint = transaction.ForceCheckpoint; // Emulate Checkpointer::canAutoCheckpoint
                        
                        ClearTransactionNoLock(transaction.ID);
                        
                        if (shouldCheckpoint)
                        {
                            CheckpointNoLock(clientContext);
                        }
                        break;
                    }
                    default:
                        throw new TransactionManagerException("Invalid transaction type to commit.");
                }
            }
        }

        public void Rollback(ClientContext clientContext, Transaction transaction)
        {
            lock (_mtxForSerializingPublicFunctionCalls)
            {
                switch (transaction.Type)
                {
                    case TransactionType.READ_ONLY:
                    {
                        ClearTransactionNoLock(transaction.ID);
                        break;
                    }
                    case TransactionType.RECOVERY:
                    case TransactionType.WRITE:
                    {
                        transaction.Rollback();
                        transaction.RollbackVersionedChanges();
                        ClearTransactionNoLock(transaction.ID);
                        break;
                    }
                    default:
                        throw new TransactionManagerException("Invalid transaction type to rollback.");
                }
            }
        }

        public void Checkpoint(ClientContext clientContext)
        {
            lock (_mtxForSerializingPublicFunctionCalls)
            {
                CheckpointNoLock(clientContext);
            }
        }

        private void CheckpointNoLock(ClientContext clientContext)
        {
            // Thread safety isolation wait
            StopNewTransactionsAndWaitUntilAllTransactionsLeave();

            try
            {
                // Emulate Checkpointer write
                var checkpointer = new Checkpointer(_storageManager.IsInMemory);
                checkpointer.WriteCheckpoint(_storageManager);
            }
            finally
            {
                Monitor.Exit(_mtxForStartingNewTransactions);
            }
        }

        private void StopNewTransactionsAndWaitUntilAllTransactionsLeave()
        {
            Monitor.Enter(_mtxForStartingNewTransactions);
            
            ulong numTimesWaited = 0;
            const int THREAD_SLEEP_TIME_WHEN_WAITING_IN_MICROS = 10000;
            
            while (true)
            {
                if (HasNoActiveTransactions()) break;

                numTimesWaited++;
                ulong waitedMicros = numTimesWaited * THREAD_SLEEP_TIME_WHEN_WAITING_IN_MICROS;

                if (waitedMicros > _checkpointWaitTimeoutInMicros)
                {
                    Monitor.Exit(_mtxForStartingNewTransactions);
                    throw new TransactionManagerException(
                        "Timeout waiting for active transactions to leave the system before " +
                        "checkpointing. If you have an open transaction, please close it and try again.");
                }

                Thread.Sleep(TimeSpan.FromTicks(THREAD_SLEEP_TIME_WHEN_WAITING_IN_MICROS * 10)); // 10 ticks = 1 microsecond
            }
        }

        private bool HasNoActiveTransactions() => _activeTransactions.Count == 0;

        private bool HasActiveWriteTransactionNoLock()
        {
            return _activeTransactions.Any(t => t.IsWriteTransaction());
        }

        private void ClearTransactionNoLock(ulong transactionID)
        {
            _activeTransactions.RemoveAll(t => t.ID == transactionID);
        }
    }
}
