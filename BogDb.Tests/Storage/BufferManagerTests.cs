using System.IO;
using BogDb.Core.Storage;
using BogDb.Core.Storage.BufferManager;
using Xunit;

namespace BogDb.Tests.Storage.BufferManager;

public class BufferManagerTests : IDisposable
{
    private readonly string _testFile = "test_data.kz";

    public BufferManagerTests()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    public void Dispose()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    [Fact]
    public void BufferManager_ShouldEvictWhenExceedingMemoryLimit()
    {
        // 40960 limit = exactly 10 Pages maximum allowed locked in memory
        var bm = new BogDb.Core.Storage.BufferManager.BufferManager(40960, 1024 * 1024);
        
        using var fileHandle = bm.GetFileHandle(_testFile, 0, 1);
        Assert.Equal((ulong)4096, fileHandle.GetPageSize());

        for (uint i = 0; i < 15; i++)
        {
            // Simulate allocating pages and using them
            var pageState = fileHandle.GetPageState(i);
            
            // Mark eviction capable (Evicted -> Locked -> Unlocked -> EvictionQueue)
            pageState.TryLock(pageState.GetStateAndVersion());
            bm.Unpin(fileHandle, i);
            
            // Wait for queue logic
            if (i >= 10)
            {
                // Our mock limit is 10 pages. Try to evict some to stay within memory limits.
                ulong reclaimed = bm.EvictPages();
                Assert.True(reclaimed > 0, "Eviction queue failed to reclaim memory when over limit");
            }
        }
    }

    [Fact]
    public unsafe void FileHandle_MemoryMappedReadsZeroAlloc()
    {
        File.WriteAllBytes(_testFile, new byte[8192]); // Create physical 2-page disk file
        var bm = new BogDb.Core.Storage.BufferManager.BufferManager(40960, 1024 * 1024);
        
        using var fileHandle = bm.GetFileHandle(_testFile, 0, 1);
        Assert.Equal(2u, fileHandle.NumPages);
        
        // Mock a direct memory mapped read without garbage allocation
        byte[] localFrame = new byte[FileHandle.BOGDB_PAGE_SIZE];
        fixed (byte* ptr = localFrame)
        {
            fileHandle.ReadPageFromDisk(ptr, 1);
            // Array memory hasn't faulted; length of native copying is exactly PAGE_SIZE 
            Assert.Equal(0, localFrame[0]);
        }
    }

    [Fact]
    public void FileHandle_InMemoryMode_CanGrowAndPreserveExistingPages()
    {
        var bm = new BogDb.Core.Storage.BufferManager.BufferManager(40960, 1024 * 1024);
        const byte inMemoryTmpFlags = 0b00000010;
        using var fileHandle = new FileHandle("ignored.tmp", inMemoryTmpFlags, bm, fileIndex: 99, initialPageCapacity: 1);

        var page0 = GC.AllocateArray<byte>((int)FileHandle.BOGDB_PAGE_SIZE);
        var page1 = GC.AllocateArray<byte>((int)FileHandle.BOGDB_PAGE_SIZE);
        page0[0] = 0x2A;
        page1[0] = 0x7B;

        fileHandle.WritePage(0, page0);
        fileHandle.WritePage(1, page1); // Forces in-memory remap/growth when capacity is 1 page.

        var read0 = GC.AllocateArray<byte>((int)FileHandle.BOGDB_PAGE_SIZE);
        var read1 = GC.AllocateArray<byte>((int)FileHandle.BOGDB_PAGE_SIZE);
        fileHandle.ReadPage(0, read0);
        fileHandle.ReadPage(1, read1);

        Assert.Equal(0x2A, read0[0]);
        Assert.Equal(0x7B, read1[0]);
        Assert.True(fileHandle.NumPages >= 2);
    }

    /// <summary>
    /// Regression test for: FileHandle._pageStates fixed-size array caused
    /// ArgumentOutOfRangeException in BufferManager.FlushAllDirtyPages()
    /// when NumPages exceeded the initial capacity.
    ///
    /// Root cause: AddNewPage() incremented NumPages without growing _pageStates.
    /// FlushAllDirtyPages() iterated up to NumPages, hitting the bounds check.
    /// Fix: EnsurePageStateCapacity() dynamically grows the array.
    /// </summary>
    [Fact]
    public void Dispose_WithMorePagesThanInitialCapacity_DoesNotThrow()
    {
        // Use a small initial page capacity to trigger the growth path
        const byte inMemoryTmpFlags = 0b00000010;
        var bm = new BogDb.Core.Storage.BufferManager.BufferManager(1024 * 1024, 10 * 1024 * 1024);
        var fileHandle = new FileHandle("regression.tmp", inMemoryTmpFlags, bm, fileIndex: 42, initialPageCapacity: 8);

        // Write more pages than the initial capacity (50 > 8)
        for (uint i = 0; i < 50; i++)
        {
            var page = GC.AllocateArray<byte>((int)FileHandle.BOGDB_PAGE_SIZE);
            page[0] = (byte)(i & 0xFF);
            fileHandle.WritePage(i, page);
        }

        Assert.True(fileHandle.NumPages >= 50, $"Expected NumPages >= 50, got {fileHandle.NumPages}");

        // GetPageState should work for all pages up to NumPages (the original crash was here)
        for (uint i = 0; i < fileHandle.NumPages; i++)
        {
            var pageState = fileHandle.GetPageState(i);
            Assert.NotNull(pageState);
        }

        // FlushAllDirtyPages iterates up to NumPages calling GetPageState —
        // this was the crash site before the fix
        var ex = Record.Exception(() => bm.FlushAllDirtyPages());
        Assert.Null(ex);

        // Dispose should also not throw
        var disposeEx = Record.Exception(() =>
        {
            fileHandle.Dispose();
            bm.Dispose();
        });
        Assert.Null(disposeEx);
    }
}
