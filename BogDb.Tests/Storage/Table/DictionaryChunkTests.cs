using BogDb.Core.Storage.Table;
using Xunit;

namespace BogDb.Tests.Storage.Table;

public class DictionaryChunkTests
{
    [Fact]
    public void AppendString_Deduplicates_WhenCompressionEnabled()
    {
        var dict = new DictionaryChunk(enableCompression: true);

        var a = dict.AppendString("alpha");
        var b = dict.AppendString("beta");
        var a2 = dict.AppendString("alpha");

        Assert.Equal(0, a);
        Assert.Equal(1, b);
        Assert.Equal(a, a2);
        Assert.Equal(2, dict.DistinctCount);
        Assert.Equal("alpha", dict.GetString(a));
        Assert.Equal("beta", dict.GetString(b));
    }

    [Fact]
    public void AppendString_DoesNotDeduplicate_WhenCompressionDisabled()
    {
        var dict = new DictionaryChunk(enableCompression: false);

        var a = dict.AppendString("alpha");
        var a2 = dict.AppendString("alpha");

        Assert.Equal(0, a);
        Assert.Equal(1, a2);
        Assert.Equal(2, dict.DistinctCount);
    }
}
