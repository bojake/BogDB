using System;
using System.IO;
using Xunit;
using BogDb.Core.Storage.MemoryManager;
using BogDb.Core.Storage.BufferManager;
using BogDb.Core.Main;

namespace BogDb.Tests.Storage
{
    public class MemoryManagerTests
    {
        [Fact]
        public void MemoryManager_ThrowsOutOfMemoryException_WhenLimitExceededAndCannotSpill()
        {
            // Arrange
            // BufferManager defaults limit to 1GB if no config. We can mock it.
            // But we actually made MemoryManager default to 1GB if BufferManager is null!
            // Wait, we can pass null and it limits to 1024 * 1024 * 1024.
            // Let's create an explicit BufferManager with a tiny limit: 1MB.
            var tempPath = Path.GetTempFileName();
            var dbDir = Path.GetTempFileName() + "_dir";
            Directory.CreateDirectory(dbDir);
            
            try
            {
                // BufferManager constructor: (long bufferPoolSize, long maxDbSize)
                using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(1024 * 1024, 2048 * 1024);
                using var memoryManager = new MemoryManager(bm);

                // Act & Assert
                // Attempt to allocate 2MB without spilling allowed
                Assert.Throws<OutOfMemoryException>(() =>
                {
                    memoryManager.AllocateBlock(2048 * 1024, false); // 2MB layout request
                });
            }
            finally
            {
                if (Directory.Exists(dbDir)) Directory.Delete(dbDir, true);
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        [Fact]
        public void MemoryManager_AllocatesSuccessfully_WhenCanSpillIsAllowed()
        {
            // Even if it breaches memory bounds, TryReserve returns false but AllocateBlock
            // succeeds yielding a MemoryBlock ready to overflow gracefully onto disk natively!
            var tempPath = Path.GetTempFileName();
            var dbDir = Path.GetTempFileName() + "_dir";
            Directory.CreateDirectory(dbDir);
            
            try
            {
                using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(1024 * 1024, 2048 * 1024);
                using var memoryManager = new MemoryManager(bm);

                // Act: Request 2MB with canSpill=true
                var block = memoryManager.AllocateBlock(2048 * 1024, true);

                // Assert
                Assert.NotNull(block);
                Assert.Equal(2048U * 1024U, block.Size);
                Assert.True(block.CanSpill);
                
                memoryManager.FreeBlock(block);
            }
            finally
            {
                if (Directory.Exists(dbDir)) Directory.Delete(dbDir, true);
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
    }
}
