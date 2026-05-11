using System;
using System.IO;
using BogDb.Core.Storage;
using BogDb.Core.Storage.Store;
using Xunit;

namespace BogDb.Tests.Storage.Store;

public class CheckpointerTests
{
    // Helper: create a unique temp dir for each test and clean it up after
    private static StorageManager MakeTempStorageManager(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), $"bogdb_ckpt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return new StorageManager(dir);
    }

    [Fact]
    public void StorageManager_UsesTransactionalWAL_ForCheckpointMarkers()
    {
        var storageManager = MakeTempStorageManager(out var dir);
        try
        {
            var walPath = Path.Combine(dir, "data.wal");
            var wal = storageManager.GetWAL();

            wal.LogAndFlushCheckpoint();
            Assert.True(File.Exists(walPath));
            Assert.True(new FileInfo(walPath).Length > 0);

            wal.Clear();
            Assert.Equal(0, new FileInfo(walPath).Length);
        }
        finally
        {
            storageManager.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StorageManager_Checkpoint_ReturnsFalse_WhenNoPendingChanges()
    {
        var storageManager = MakeTempStorageManager(out var dir);
        try
        {
            Assert.False(storageManager.Checkpoint());
        }
        finally
        {
            storageManager.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StorageManager_Checkpoint_ReturnsTrue_WhenPendingChangesExist()
    {
        var storageManager = MakeTempStorageManager(out var dir);
        try
        {
            storageManager.GetShadowFile().GetOrCreateShadowPage(1, 1);
            Assert.True(storageManager.Checkpoint());
        }
        finally
        {
            storageManager.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        var dirFl = Path.Combine(Path.GetTempPath(), $"bogdb_ckpt_fl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dirFl);
        var cleanStorageManager = new StorageManager(dirFl);
        try
        {
            cleanStorageManager.GetFreeSpaceManager().AddUncheckpointedFreePages(new PageRange(0, 10));
            Assert.True(cleanStorageManager.Checkpoint());
        }
        finally
        {
            cleanStorageManager.Dispose();
            if (Directory.Exists(dirFl)) Directory.Delete(dirFl, recursive: true);
        }
    }

    [Fact]
    public void Checkpointer_ShouldBypassIfInMemory()
    {
        var checkpointer = new Checkpointer(isInMemory: true);
        var storageManager = MakeTempStorageManager(out var dir);
        try
        {
            storageManager.GetShadowFile().GetOrCreateShadowPage(1, 1);
            checkpointer.WriteCheckpoint(storageManager);
            // If in-memory, the checkpoint returns early without clearing shadow file
            Assert.True(storageManager.GetShadowFile().HasShadowPage(1, 1));
        }
        finally
        {
            storageManager.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Checkpointer_ShouldExecuteStorageCheckpointIfPersistent()
    {
        var checkpointer = new Checkpointer(isInMemory: false);
        var storageManager = MakeTempStorageManager(out var dir);
        try
        {
            // Emulate a dirty uncheckpointed free list and a shadow page
            storageManager.GetFreeSpaceManager().AddUncheckpointedFreePages(new PageRange(0, 10));
            storageManager.GetShadowFile().GetOrCreateShadowPage(1, 1);

            checkpointer.WriteCheckpoint(storageManager);

            // ShadowFile is cleared post-checkpoint
            Assert.False(storageManager.GetShadowFile().HasShadowPage(1, 1));
            // Uncheckpointed pages are finalized into actual free space
            Assert.Equal(1ul, storageManager.GetFreeSpaceManager().GetNumEntries());
        }
        finally
        {
            storageManager.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Checkpointer_Rollback_EmulatesFailurePath()
    {
        var checkpointer = new Checkpointer(isInMemory: false);
        var storageManager = MakeTempStorageManager(out var dir);
        try
        {
            storageManager.GetFreeSpaceManager().AddUncheckpointedFreePages(new PageRange(0, 10));
            checkpointer.Rollback(storageManager);
            // Uncheckpointed ranges are rolled back (cleared) on failure
            Assert.Equal(0ul, storageManager.GetFreeSpaceManager().GetNumEntries());
            storageManager.GetFreeSpaceManager().FinalizeCheckpoint();
            Assert.Equal(0ul, storageManager.GetFreeSpaceManager().GetNumEntries());
        }
        finally
        {
            storageManager.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
