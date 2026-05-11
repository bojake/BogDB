using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Transaction;
using BogDb.Core.Storage;

namespace BogDb.Tests.Transaction
{
    public class TransactionManagerTests
    {
        private static TransactionManager MakeTxManager()
        {
            var dir = Path.Combine(Path.GetTempPath(), "bogdb-txmgr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return new TransactionManager(new StorageManager(dir));
        }

        [Fact]
        public void TransactionManager_AllowsConcurrentReadTransactions()
        {
            // Arrange
            var txManager = MakeTxManager();
            var clientContext = new ClientContext(null); // Mock context

            var activeTxs = new ConcurrentBag<BogDb.Core.Transaction.Transaction>();
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < 50; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var tx = txManager.BeginTransaction(clientContext, TransactionType.READ_ONLY);
                    activeTxs.Add(tx);
                }));
            }

            Task.WaitAll(tasks.ToArray());

            // Assert
            Assert.Equal(50, activeTxs.Count);
            foreach (var tx in activeTxs)
            {
                Assert.True(tx.IsReadOnly());
                Assert.True(tx.ID > 0);
            }
        }

        [Fact]
        public void TransactionManager_ThrowsOnConcurrentWriteTransactions()
        {
            // Arrange
            var txManager = MakeTxManager();
            var clientContext = new ClientContext(null);

            // Act - Begin the first write transaction
            var writeTx1 = txManager.BeginTransaction(clientContext, TransactionType.WRITE);

            // Assert - The second write transaction must fail since BogDb explicitly prevents concurrent writers
            var exception = Assert.Throws<TransactionManagerException>(() =>
            {
                txManager.BeginTransaction(clientContext, TransactionType.WRITE);
            });

            Assert.Contains("Only one write transaction", exception.Message);
            Assert.True(writeTx1.IsWriteTransaction());
        }

        [Fact]
        public void TransactionManager_AllowsWriteAndConcurrentReadTransactions()
        {
            // Arrange
            var txManager = MakeTxManager();
            var clientContext = new ClientContext(null);

            // Act
            var writeTx = txManager.BeginTransaction(clientContext, TransactionType.WRITE);
            var readTx1 = txManager.BeginTransaction(clientContext, TransactionType.READ_ONLY);
            var readTx2 = txManager.BeginTransaction(clientContext, TransactionType.READ_ONLY);

            // Assert
            Assert.True(writeTx.IsWriteTransaction());
            Assert.True(readTx1.IsReadOnly());
            Assert.True(readTx2.IsReadOnly());
            Assert.NotEqual(writeTx.ID, readTx1.ID);
            Assert.NotEqual(readTx1.ID, readTx2.ID);
        }
    }
}
