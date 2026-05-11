using BogDb.Core.Storage.BufferManager;
using Xunit;

namespace BogDb.Tests.Storage.BufferManager;

public class PageStateTests
{
    [Fact]
    public void PageState_ShouldInitializeToEvicted()
    {
        var pageState = new PageState();
        Assert.Equal(PageState.EVICTED, pageState.GetState());
        Assert.Equal(0Lu, PageState.GetVersion(pageState.GetStateAndVersion()));
    }

    [Fact]
    public void PageState_ShouldHandleBasicTransitions()
    {
        var pageState = new PageState();
        var stateAndVersion = pageState.GetStateAndVersion();

        // Transition: Evicted -> Locked 
        Assert.True(pageState.TryLock(stateAndVersion));
        Assert.Equal(PageState.LOCKED, pageState.GetState());
        
        // Assert version was not incremented by TryLock itself
        Assert.Equal(0Lu, PageState.GetVersion(pageState.GetStateAndVersion()));

        // Transition: Locked -> Unlocked
        pageState.Unlock();
        Assert.Equal(PageState.UNLOCKED, pageState.GetState());
        
        // Assert version was incremented
        Assert.Equal(1Lu, PageState.GetVersion(pageState.GetStateAndVersion()));
    }

    [Fact]
    public void PageState_ShouldHandleMarking()
    {
        var pageState = new PageState();
        var initial = pageState.GetStateAndVersion();
        pageState.TryLock(initial);
        pageState.Unlock(); // state: UNLOCKED, version: 1

        var currentState = pageState.GetStateAndVersion();
        Assert.Equal(PageState.UNLOCKED, PageState.GetState(currentState));

        Assert.True(pageState.TryMark(currentState));
        Assert.Equal(PageState.MARKED, pageState.GetState());

        // Try Clear Mark back to unlocked
        var markedState = pageState.GetStateAndVersion();
        Assert.True(pageState.TryClearMark(markedState));
        Assert.Equal(PageState.UNLOCKED, pageState.GetState());
        Assert.Equal(1Lu, PageState.GetVersion(pageState.GetStateAndVersion()));
    }

    [Fact]
    public void PageState_ShouldSetAndClearDirtyWithoutAffectingVersion()
    {
        var pageState = new PageState();
        var initial = pageState.GetStateAndVersion();
        pageState.TryLock(initial);
        
        Assert.False(pageState.IsDirty());

        pageState.SetDirty();
        Assert.True(pageState.IsDirty());
        Assert.Equal(PageState.LOCKED, pageState.GetState());

        pageState.ClearDirty();
        Assert.False(pageState.IsDirty());
        Assert.Equal(PageState.LOCKED, pageState.GetState());
        
        pageState.Unlock();
    }
}
