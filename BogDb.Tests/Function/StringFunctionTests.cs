using Xunit;
using BogDb.Core.Function;

namespace BogDb.Tests.Function;

public sealed class StringFunctionTests
{
    [Fact] public void Lpad_PadsLeft()
        => Assert.Equal("  hi", FunctionDispatcher.Invoke("lpad", ["hi", 4L]));

    [Fact] public void Rpad_PadsRight()
        => Assert.Equal("hi  ", FunctionDispatcher.Invoke("rpad", ["hi", 4L]));

    [Fact] public void Lpad_WithFillChar()
        => Assert.Equal("00hi", FunctionDispatcher.Invoke("lpad", ["hi", 4L, "0"]));

    [Fact] public void Repeat_StringNTimes()
        => Assert.Equal("abab", FunctionDispatcher.Invoke("repeat", ["ab", 2L]));

    [Fact] public void Split_ByDelimiter()
    {
        var result = FunctionDispatcher.Invoke("split", ["a,b,c", ","]) as System.Collections.Generic.List<object?>;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("b", result[1]);
    }

    [Fact] public void Ascii_OfA()
        => Assert.Equal(65L, FunctionDispatcher.Invoke("ascii", ["A"]));

    [Fact] public void Chr_Of65()
        => Assert.Equal("A", FunctionDispatcher.Invoke("chr", [65L]));

    [Fact] public void Levenshtein_SameString()
        => Assert.Equal(0L, FunctionDispatcher.Invoke("levenshtein", ["hello", "hello"]));

    [Fact] public void Levenshtein_OneEdit()
        => Assert.Equal(1L, FunctionDispatcher.Invoke("levenshtein", ["hello", "helo"]));

    [Fact] public void RegexpReplace_Basic()
        => Assert.Equal("xbc", FunctionDispatcher.Invoke("regexp_replace", ["abc", "a", "x"]));

    [Fact] public void RegexpExtract_Group()
        => Assert.Equal("42", FunctionDispatcher.Invoke("regexp_extract", ["abc42def", @"\d+", 0L]));

    [Fact] public void Initcap_ConvertsCase()
        => Assert.Equal("Hello World", FunctionDispatcher.Invoke("initcap", ["hello world"]));

    [Fact] public void Reverse_String()
        => Assert.Equal("cba", FunctionDispatcher.Invoke("reverse", ["abc"]));
}
