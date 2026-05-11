using System;
using System.IO;
using Xunit;
using BogDb.Core.Storage;

namespace BogDb.Tests.Storage;

/// <summary>
/// Tests for DatabaseLockManager, DatabaseBusyException, and the
/// DiskStorageManager FileShare.Read concurrency fix.
/// </summary>
public class DatabaseLockManagerTests : IDisposable
{
    private readonly string _dbPath;

    public DatabaseLockManagerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bogdb_lock_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_dbPath);
    }

    [Fact]
    public void Acquire_Succeeds_OnFirstOpen()
    {
        using var lockManager = DatabaseLockManager.Acquire(_dbPath);

        Assert.NotNull(lockManager);
        Assert.True(lockManager!.IsLockHeld);
        Assert.True(File.Exists(Path.Combine(_dbPath, ".lock")));
        Assert.True(File.Exists(Path.Combine(_dbPath, ".lock.info")));
    }

    [Fact]
    public void Acquire_ThrowsDatabaseBusyException_WhenAlreadyLocked()
    {
        using var first = DatabaseLockManager.Acquire(_dbPath);

        var ex = Assert.Throws<DatabaseBusyException>(() =>
        {
            using var second = DatabaseLockManager.Acquire(_dbPath);
        });

        Assert.Equal(_dbPath, ex.DatabasePath);
        Assert.Contains("currently in use by another writer", ex.Message);
        Assert.Contains(_dbPath, ex.Message);
        Assert.NotNull(ex.InnerException);
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public void Acquire_ReportsOwnerPidInException()
    {
        using var first = DatabaseLockManager.Acquire(_dbPath);

        var ex = Assert.Throws<DatabaseBusyException>(() =>
        {
            using var second = DatabaseLockManager.Acquire(_dbPath);
        });

        // The lock file should contain our PID, and the exception should report it
        Assert.True(ex.OwnerProcessId > 0, "Owner PID should be reported");
        Assert.Equal(Environment.ProcessId, ex.OwnerProcessId);
        Assert.NotNull(ex.OwnerTimestamp);
        Assert.Contains($"PID {Environment.ProcessId}", ex.Message);
    }

    [Fact]
    public void Acquire_SucceedsAfterPreviousLockDisposed()
    {
        var first = DatabaseLockManager.Acquire(_dbPath);
        Assert.True(first!.IsLockHeld);

        first.Dispose();
        Assert.False(first.IsLockHeld);

        // Now a second acquisition should succeed
        using var second = DatabaseLockManager.Acquire(_dbPath);
        Assert.NotNull(second);
        Assert.True(second!.IsLockHeld);
    }

    [Fact]
    public void Acquire_ReturnsNull_ForInMemoryPath()
    {
        var result = DatabaseLockManager.Acquire(":memory:");
        Assert.Null(result);
    }

    [Fact]
    public void Acquire_ReturnsNull_ForInMemoryPath_CaseInsensitive()
    {
        var result = DatabaseLockManager.Acquire(":MEMORY:");
        Assert.Null(result);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var lockManager = DatabaseLockManager.Acquire(_dbPath)!;
        lockManager.Dispose();
        lockManager.Dispose(); // Should not throw
        Assert.False(lockManager.IsLockHeld);
    }

    [Fact]
    public void LockInfoFile_ContainsPidAndTimestamp()
    {
        using var lockManager = DatabaseLockManager.Acquire(_dbPath);

        var lockInfoPath = Path.Combine(_dbPath, ".lock.info");
        Assert.True(File.Exists(lockInfoPath));

        // The .lock.info file is a regular file (not held exclusively), so we can read it directly
        var lines = File.ReadAllLines(lockInfoPath);

        Assert.True(lines.Length >= 2);
        Assert.True(int.TryParse(lines[0].Trim(), out var pid));
        Assert.Equal(Environment.ProcessId, pid);
        Assert.True(DateTimeOffset.TryParse(lines[1].Trim(), out _));
    }

    [Fact]
    public void DiskStorageManager_OpensDataFileWithFileShareRead()
    {
        // Open a DiskStorageManager — data.kz should now be opened with FileShare.Read
        using var dsm = new DiskStorageManager(_dbPath);

        // Verify we can open the same data.kz file for reading concurrently
        var dataPath = Path.Combine(_dbPath, "data.kz");
        Assert.True(File.Exists(dataPath));

        // This should NOT throw because DiskStorageManager now uses FileShare.Read
        using var readStream = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Assert.True(readStream.CanRead);
    }

    [Fact]
    public void StorageManager_HoldsWriteLock_ForDiskDatabases()
    {
        using var sm = new StorageManager(_dbPath);
        Assert.True(sm.IsWriteLockHeld);
    }

    [Fact]
    public void StorageManager_NoLock_ForInMemoryDatabases()
    {
        using var sm = new StorageManager(":memory:");
        Assert.False(sm.IsWriteLockHeld);
    }

    [Fact]
    public void StorageManager_SecondOpen_ThrowsDatabaseBusyException()
    {
        using var first = new StorageManager(_dbPath);

        var ex = Assert.Throws<DatabaseBusyException>(() =>
        {
            using var second = new StorageManager(_dbPath);
        });

        Assert.Equal(_dbPath, ex.DatabasePath);
        Assert.Contains("currently in use by another writer", ex.Message);
    }

    [Fact]
    public void StorageManager_SecondOpen_SucceedsAfterFirstDisposed()
    {
        var first = new StorageManager(_dbPath);
        first.Dispose();

        using var second = new StorageManager(_dbPath);
        Assert.True(second.IsWriteLockHeld);
    }

    [Fact]
    public void DatabaseBusyException_ContainsStructuredInfo()
    {
        var now = DateTimeOffset.UtcNow;
        var ex = new DatabaseBusyException("/test/path", 12345, now);

        Assert.Equal("/test/path", ex.DatabasePath);
        Assert.Equal(12345, ex.OwnerProcessId);
        Assert.Equal(now, ex.OwnerTimestamp);
        Assert.Contains("/test/path", ex.Message);
        Assert.Contains("PID 12345", ex.Message);
        Assert.Contains("currently in use by another writer", ex.Message);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public void DatabaseBusyException_FormatMessage_UnknownOwner()
    {
        var ex = new DatabaseBusyException("/test/path", -1, null);

        Assert.Equal(-1, ex.OwnerProcessId);
        Assert.Null(ex.OwnerTimestamp);
        Assert.Contains("/test/path", ex.Message);
        Assert.DoesNotContain("PID", ex.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dbPath))
        {
            try { Directory.Delete(_dbPath, recursive: true); } catch { }
        }
    }
}
