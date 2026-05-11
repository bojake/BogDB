using System.Collections.Generic;
using BogDb.Core.Storage.Store;
using Xunit;

namespace BogDb.Tests.Storage.Store;

public class FreeSpaceManagerTests
{
    [Fact]
    public void AddFreePages_ShouldIncreaseEntryCount()
    {
        var fsm = new FreeSpaceManager();
        fsm.AddFreePages(new PageRange(0, 10));
        Assert.Equal(1ul, fsm.GetNumEntries());

        // Adding same range shouldn't increase count due to set behavior
        fsm.AddFreePages(new PageRange(0, 10));
        Assert.Equal(1ul, fsm.GetNumEntries());
    }

    [Fact]
    public void PopFreePages_ShouldReturnSplitRangeWhenAvailable()
    {
        var fsm = new FreeSpaceManager();
        fsm.AddFreePages(new PageRange(0, 10));

        var popped = fsm.PopFreePages(4);
        Assert.NotNull(popped);
        Assert.Equal(0ul, popped.Value.StartPageIdx);
        Assert.Equal(4ul, popped.Value.NumPages);

        // The remaining 6 pages should still be in the manager
        Assert.Equal(1ul, fsm.GetNumEntries());

        var nextPopped = fsm.PopFreePages(6);
        Assert.NotNull(nextPopped);
        Assert.Equal(4ul, nextPopped.Value.StartPageIdx);
        Assert.Equal(6ul, nextPopped.Value.NumPages);
        
        Assert.Equal(0ul, fsm.GetNumEntries());
    }

    [Fact]
    public void MergePageRanges_ShouldCombineAdjacentBlocks()
    {
        var fsm = new FreeSpaceManager();
        // Add non-adjacent but closely sorted blocks initially
        var newEntries = new List<PageRange>
        {
            new PageRange(0, 5),
            new PageRange(5, 5),  // Contiguous to first
            new PageRange(15, 10) // Gap of 5
        };

        fsm.MergePageRanges(newEntries);

        // Should merge 0-5 and 5-5 into 0-10, and keep 15-10
        Assert.Equal(2ul, fsm.GetNumEntries());
        
        var popped1 = fsm.PopFreePages(10);
        Assert.NotNull(popped1);
        Assert.Equal(0ul, popped1.Value.StartPageIdx);

        var popped2 = fsm.PopFreePages(10);
        Assert.NotNull(popped2);
        Assert.Equal(15ul, popped2.Value.StartPageIdx);
    }

    [Fact]
    public void FinalizeCheckpoint_ShouldMergeUncheckpointedPages()
    {
        var fsm = new FreeSpaceManager();
        fsm.AddUncheckpointedFreePages(new PageRange(0, 5));
        fsm.AddUncheckpointedFreePages(new PageRange(5, 5));

        Assert.Equal(0ul, fsm.GetNumEntries());

        fsm.FinalizeCheckpoint();

        // The two uncheckpointed ranges should merge into a single range
        Assert.Equal(1ul, fsm.GetNumEntries());
        var popped = fsm.PopFreePages(10);
        Assert.NotNull(popped);
        Assert.Equal(10ul, popped.Value.NumPages);
    }
}
