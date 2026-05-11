using System;
using System.IO;
using System.Linq;
using BogDb.Core.Main;
using BogDb.Core.Transaction;
using Xunit;

namespace BogDb.Tests.Transaction;

public class WALReplayerTests : IDisposable
{
    private readonly string _testDir;
    
    public WALReplayerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"bogdb_wal_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Fact]
    public void PageUpdateWALRecord_ReplayOvewritesTargetPage()
    {
        // 1. Create a dummy data file with two pages
        var dummyDataFile = Path.Combine(_testDir, "data.kz");
        var pageSize = 4096;
        var originalData = new byte[pageSize * 2];
        Array.Fill(originalData, (byte)1); // original filling
        File.WriteAllBytes(dummyDataFile, originalData);

        // 2. Prepare the new page payload (Page 1)
        var newPageData = new byte[pageSize];
        Array.Fill(newPageData, (byte)2); // new filling

        var record = new PageUpdateWALRecord
        {
            FilePath = dummyDataFile, // The record can have an absolute path, Replay extracts the filename
            PageIdx = 1,
            PageData = newPageData
        };

        // 3. Write a mock WAL file and replay it
        using (var wal = new WAL(_testDir, readOnly: false, inMemory: false))
        {
            // We write a commit record also to ensure there's a valid end of transaction
            wal.LogPageUpdateWithData(dummyDataFile, 1, newPageData, (uint)pageSize);
            wal.LogAndFlushCommit(new BogDb.Core.Transaction.Transaction(BogDb.Core.Transaction.TransactionType.WRITE, 1, 1));
        }

        var replayer = new WALReplayer(_testDir);
        replayer.Replay();

        // 4. Verify
        var updatedData = File.ReadAllBytes(dummyDataFile);
        
        // Page 0 should still be (byte)1
        for (int i = 0; i < pageSize; i++)
        {
            Assert.Equal((byte)1, updatedData[i]);
        }
        
        // Page 1 should be overwritten with (byte)2
        for (int i = pageSize; i < pageSize * 2; i++)
        {
            Assert.Equal((byte)2, updatedData[i]);
        }
    }
}
