using System.Collections.Generic;
using Xunit;
using BogDb.Core.Function;
using BogDb.Core.Common;

namespace BogDb.Tests.Function;

public sealed class ListFunctionTests
{
    private static List<object?> L(params object?[] items) => new(items);

    [Fact] public void ListLen_CorrectCount()
        => Assert.Equal(3L, FunctionDispatcher.Invoke("list_len", [L(1L, 2L, 3L)]));

    [Fact] public void ListElement_OneBased()
        => Assert.Equal(2L, FunctionDispatcher.Invoke("list_element", [L(1L, 2L, 3L), 2L]));

    [Fact] public void ListElement_NegativeIndex()
        => Assert.Equal(3L, FunctionDispatcher.Invoke("list_element", [L(1L, 2L, 3L), -1L]));

    [Fact] public void ListContains_True()
        => Assert.Equal(true, FunctionDispatcher.Invoke("list_contains", [L(1L, 2L, 3L), 2L]));

    [Fact] public void ListContains_False()
        => Assert.Equal(false, FunctionDispatcher.Invoke("list_contains", [L(1L, 2L, 3L), 5L]));

    [Fact] public void ListPosition_Found()
        => Assert.Equal(2L, FunctionDispatcher.Invoke("list_position", [L("a", "b", "c"), "b"]));

    [Fact] public void ListPosition_NotFound()
        => Assert.Equal(0L, FunctionDispatcher.Invoke("list_position", [L("a", "b"), "z"]));

    [Fact] public void ListSlice_SubList()
    {
        var result = FunctionDispatcher.Invoke("list_slice", [L(1L, 2L, 3L, 4L), 2L, 4L]) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(2L, result[0]);
    }

    [Fact] public void ListConcat_MergesLists()
    {
        var result = FunctionDispatcher.Invoke("list_concat", [L(1L, 2L), L(3L, 4L)]) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(4, result.Count);
    }

    [Fact] public void Range_ProducesCorrectNumbers()
    {
        var result = FunctionDispatcher.Invoke("range", [1L, 5L]) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(5, result.Count);
        Assert.Equal(1L, result[0]);
        Assert.Equal(5L, result[4]);
    }

    [Fact] public void ListSort_SortsAscending()
    {
        var result = FunctionDispatcher.Invoke("list_sort", [L("c", "a", "b")]) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal("a", result[0]);
    }

    [Fact] public void ListUnique_DropsDuplicates()
    {
        var result = FunctionDispatcher.Invoke("list_unique", [L(1L, 2L, 1L, 3L)]) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void ListContains_NestedIntervals_UsesStructuralEquality()
    {
        var list = L(L(BogDbInterval.FromDays(1)), L(BogDbInterval.FromDays(2)));
        Assert.Equal(true, FunctionDispatcher.Invoke("list_contains", [list, L(BogDbInterval.FromDays(1))]));
    }

    [Fact]
    public void ListUnique_NestedIntervals_DeduplicatesStructurally()
    {
        var result = FunctionDispatcher.Invoke(
            "list_unique",
            [L(L(BogDbInterval.FromDays(1)), L(BogDbInterval.FromDays(1)), L(BogDbInterval.FromDays(2)))]) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact] public void ListSum_CorrectTotal()
        => Assert.Equal(6.0, (double)FunctionDispatcher.Invoke("list_sum", [L(1L, 2L, 3L)])!, 5);

    [Fact] public void ListAvg_CorrectAverage()
        => Assert.Equal(2.0, (double)FunctionDispatcher.Invoke("list_avg", [L(1L, 2L, 3L)])!, 5);

    [Fact] public void ListMin_CorrectMin()
        => Assert.Equal(1.0, (double)FunctionDispatcher.Invoke("list_min", [L(3L, 1L, 2L)])!, 5);

    [Fact] public void ListMax_CorrectMax()
        => Assert.Equal(3.0, (double)FunctionDispatcher.Invoke("list_max", [L(3L, 1L, 2L)])!, 5);

    [Fact] public void ListToString_JoinsWithSep()
        => Assert.Equal("a,b,c", FunctionDispatcher.Invoke("list_to_string", [L("a", "b", "c"), ","]));

    [Fact] public void ListAppend_AddsElement()
    {
        var result = FunctionDispatcher.Invoke("list_append", [L(1L, 2L), 3L]) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(3L, result[2]);
    }
}
