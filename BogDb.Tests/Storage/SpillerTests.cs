using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;
using BogDb.Core.Storage.MemoryManager;
using BogDb.Core.Common;

namespace BogDb.Tests.Storage
{
    public class SpillerTests
    {
        [Fact]
        public void Spiller_WritesAndReadsMemoryBlock_Correctly()
        {
            // Arrange
            var dbPath = Path.GetTempFileName() + "_dir";
            var tempPath = Path.GetTempFileName();
            Directory.CreateDirectory(dbPath);
            using var bm = new BogDb.Core.Storage.BufferManager.BufferManager(1024 * 1024, 2048 * 1024);
            using var memoryManager = new MemoryManager(bm);
            var spiller = new Spiller(memoryManager);

            var blockSize = 1024U; // 1KB
            var memoryBlock = new MemoryBlock(blockSize, true);

            try
            {
                // Fill memory block with some deterministic data
                unsafe
                {
                    byte* ptr = memoryBlock.Data;
                    for (int i = 0; i < blockSize; i++)
                    {
                        ptr[i] = (byte)(i % 256);
                    }
                    memoryBlock.Append(memoryBlock.Data, blockSize); // hack to set UsedSize to blockSize
                }

                // Act - Spill to disk
                var partitionInfo = spiller.SpillBlock(memoryBlock);

                // Act - Read from disk
                using var readBlock = spiller.LoadSpilledPartition(partitionInfo);

                // Assert
                unsafe
                {
                    byte* ptr = readBlock.Data;
                    for (int i = 0; i < blockSize; i++)
                    {
                        Assert.Equal((byte)(i % 256), ptr[i]);
                    }
                }
            }
            finally
            {
                memoryBlock.Dispose();
                
                // Cleanup temp files
                foreach(var file in Directory.GetFiles(dbPath))
                {
                    File.Delete(file);
                }
                Directory.Delete(dbPath, true);
            }
        }
    }
}
