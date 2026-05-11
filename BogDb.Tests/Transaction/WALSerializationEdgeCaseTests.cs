using BogDb.Core.Transaction;
using System.IO;
using Xunit;

namespace BogDb.Tests.Transaction;

public class WALSerializationEdgeCaseTests : IDisposable
{
    private readonly string _testDbPath = "test_wal_err_db_dir";

    public WALSerializationEdgeCaseTests()
    {
        if (Directory.Exists(_testDbPath)) Directory.Delete(_testDbPath, true);
        Directory.CreateDirectory(_testDbPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDbPath)) Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void WAL_Serialization_MissingFileShouldNotCrashIfReadOnlyOrInMemory()
    {
        // Read-only WAL without physical backing shouldn't crash
        using var wal = new WAL(_testDbPath, readOnly: true, inMemory: true);
        
        var tx = new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 111, 222);
        
        wal.LogAndFlushCommit(tx); 
        wal.LogPageUpdate(1, 42);
        
        Assert.Equal(0ul, wal.GetFileSize());
    }

    [Fact]
    public void WAL_Clear_NoOpIfStreamNull()
    {
        using var wal = new WAL(_testDbPath, readOnly: true, inMemory: true);
        
        wal.Clear(); // Just mapping coverage verifying no uninitialized FileStream derefs.
        Assert.True(true);
    }
}
