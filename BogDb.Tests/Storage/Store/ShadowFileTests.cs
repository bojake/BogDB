using System;
using BogDb.Core.Storage.Store;
using Xunit;

namespace BogDb.Tests.Storage.Store;

public class ShadowFileTests
{
    private const string TestDbPath = "test_shadow.kz";

    [Fact]
    public void ShadowFile_GetOrCreateShadowPage_MapsCorrectly()
    {
        var shadowFile = new ShadowFile(TestDbPath);
        
        Assert.False(shadowFile.HasShadowPage(1, 10));

        var shadowPageIdx = shadowFile.GetOrCreateShadowPage(1, 10);
        Assert.True(shadowPageIdx > 0);
        Assert.True(shadowFile.HasShadowPage(1, 10));

        // Calling it again should return the exact same shadow page
        var secondCall = shadowFile.GetOrCreateShadowPage(1, 10);
        Assert.Equal(shadowPageIdx, secondCall);
    }

    [Fact]
    public void ShadowFile_ClearShadowPage_RemovesMapping()
    {
        var shadowFile = new ShadowFile(TestDbPath);
        shadowFile.GetOrCreateShadowPage(2, 20);
        Assert.True(shadowFile.HasShadowPage(2, 20));

        shadowFile.ClearShadowPage(2, 20);
        Assert.False(shadowFile.HasShadowPage(2, 20));
    }

    [Fact]
    public void ShadowFile_Reset_ShouldClearAllAndRemoveFileIfPresent()
    {
        var shadowFile = new ShadowFile(TestDbPath);
        shadowFile.GetOrCreateShadowPage(3, 30);
        
        // Emulate writing a physical shadow file
        System.IO.File.WriteAllText(TestDbPath + ".shadow", "dummy_content");
        Assert.True(System.IO.File.Exists(TestDbPath + ".shadow"));

        shadowFile.Reset();

        Assert.False(shadowFile.HasShadowPage(3, 30));
        Assert.False(System.IO.File.Exists(TestDbPath + ".shadow"));
    }
}
