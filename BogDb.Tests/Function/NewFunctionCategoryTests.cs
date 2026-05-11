using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Function;

namespace BogDb.Tests.Function;

public sealed class NewFunctionCategoryTests
{
    private static object? Call(string name, params object?[] args)
        => FunctionDispatcher.Invoke(name, args);

    private static List<object?> L(params object?[] items) =>
        items.Select(x => (object?)x).ToList();

    // ── Path functions ────────────────────────────────────────────────────────

    [Fact]
    public void Path_Nodes_ReturnsNodesList()
    {
        var path = new Dictionary<string, object?>
        {
            ["_nodes"] = L("n1", "n2", "n3"),
            ["_rels"]  = L("r1", "r2")
        };
        var result = (List<object?>)Call("nodes", path)!;
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Path_Rels_ReturnsRelsList()
    {
        var path = new Dictionary<string, object?>
        {
            ["_nodes"] = L("n1", "n2"),
            ["_rels"]  = L("r1")
        };
        var result = (List<object?>)Call("rels", path)!;
        Assert.Single(result);
    }

    [Fact]
    public void Path_Length_CountsRels()
    {
        var path = new Dictionary<string, object?>
        {
            ["_nodes"] = L("n1", "n2", "n3"),
            ["_rels"]  = L("r1", "r2")
        };
        Assert.Equal(2L, (long)Call("length", path)!);
    }

    [Fact]
    public void Path_Properties_FiltersInternalKeys()
    {
        var node = new Dictionary<string, object?> { ["name"] = "Alice", ["_id"] = "P:0", ["age"] = 30L };
        var props = (Dictionary<string, object?>)Call("properties", node)!;
        Assert.True(props.ContainsKey("name"));
        Assert.True(props.ContainsKey("age"));
        Assert.False(props.ContainsKey("_id"));
    }

    // ── Array functions ───────────────────────────────────────────────────────

    [Fact]
    public void Array_Value_CreatesListFromArgs()
    {
        var result = (List<object?>)Call("array_value", 1L, 2L, 3L)!;
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Array_InnerProduct_ComputesDot()
    {
        var x = L(1.0, 2.0, 3.0);
        var y = L(4.0, 5.0, 6.0);
        var result = (double)Call("array_inner_product", x, y)!;
        Assert.Equal(32.0, result, 5);
    }

    [Fact]
    public void Array_CosineSimilarity_UnitVectorsReturn1()
    {
        var x = L(1.0, 0.0, 0.0);
        var y = L(1.0, 0.0, 0.0);
        var result = (double)Call("array_cosine_similarity", x, y)!;
        Assert.Equal(1.0, result, 5);
    }

    [Fact]
    public void Array_Distance_OrthogonalVectors()
    {
        var x = L(1.0, 0.0);
        var y = L(0.0, 1.0);
        var result = (double)Call("array_distance", x, y)!;
        Assert.Equal(Math.Sqrt(2), result, 5);
    }

    [Fact]
    public void Array_CrossProduct_3D()
    {
        var x = L(1.0, 0.0, 0.0);
        var y = L(0.0, 1.0, 0.0);
        var result = (List<object?>)Call("array_cross_product", x, y)!;
        Assert.Equal(3, result.Count);
        Assert.Equal(0.0, (double)result[0]!, 5); // x-component = 0
        Assert.Equal(0.0, (double)result[1]!, 5); // y-component = 0
        Assert.Equal(1.0, (double)result[2]!, 5); // z-component = 1
    }

    [Fact]
    public void Array_Contains_FindsElement()
    {
        var arr = L(1L, 2L, 3L);
        Assert.True((bool)Call("array_contains", arr, 2L)!);
        Assert.False((bool)Call("array_contains", arr, 5L)!);
    }

    [Fact]
    public void Array_Normalize_UnitLengthResult()
    {
        var arr = L(3.0, 4.0);
        var result = (List<object?>)Call("array_normalize", arr)!;
        var len = Math.Sqrt(result.Sum(v => { var d = (double)v!; return d * d; }));
        Assert.Equal(1.0, len, 5);
    }

    // ── Pattern functions ─────────────────────────────────────────────────────

    [Fact]
    public void Pattern_Id_FromDict()
    {
        var node = new Dictionary<string, object?> { ["_id"] = "Person:7", ["name"] = "Bob" };
        Assert.Equal("Person:7", (string)Call("id", node)!);
    }

    [Fact]
    public void Pattern_Label_FromDict()
    {
        var node = new Dictionary<string, object?> { ["_label"] = "Person", ["name"] = "Alice" };
        Assert.Equal("Person", (string)Call("label", node)!);
    }

    [Fact]
    public void Pattern_Label_DerivesFromIdString()
    {
        // When no _label key, derive table name from "TableName:offset"
        Assert.Equal("Person", (string)Call("label", "Person:3")!);
    }

    [Fact]
    public void Pattern_StartNode_FromDict()
    {
        var rel = new Dictionary<string, object?> { ["_src"] = "A:0", ["_dst"] = "B:1" };
        Assert.Equal("A:0", (string)Call("start_node", rel)!);
    }

    [Fact]
    public void Pattern_EndNode_FromDict()
    {
        var rel = new Dictionary<string, object?> { ["_src"] = "A:0", ["_dst"] = "B:1" };
        Assert.Equal("B:1", (string)Call("end_node", rel)!);
    }

    // ── Timestamp functions ───────────────────────────────────────────────────

    [Fact]
    public void Timestamp_ToEpochMs_KnownDate()
    {
        // 2024-01-01 00:00:00 UTC → 1704067200000 ms
        var ms = (long)Call("to_epoch_ms", "2024-01-01 00:00:00")!;
        Assert.Equal(1704067200000L, ms);
    }

    [Fact]
    public void Timestamp_EpochMs_ToTimestampString()
    {
        var ts = (string)Call("epoch_ms", 0L)!;
        Assert.StartsWith("1970-01-01", ts);
    }

    [Fact]
    public void Timestamp_Year_Extract()
    {
        var year = (long)Call("timestamp_year", "2026-03-20 12:00:00")!;
        Assert.Equal(2026L, year);
    }

    [Fact]
    public void Timestamp_Month_Extract()
    {
        var month = (long)Call("timestamp_month", "2026-03-20 12:00:00")!;
        Assert.Equal(3L, month);
    }

    [Fact]
    public void Timestamp_Add_Hours_UsesUnitTimestampAmountSignature()
    {
        var ts = (string)Call("timestamp_add", "hour", "2024-01-01T00:00:00Z", 6L)!;
        Assert.Equal("2024-01-01 06:00:00", ts);
    }

    // ── Sequence functions ────────────────────────────────────────────────────

    [Fact]
    public void Sequence_Nextval_Increments()
    {
        BogDb.Core.Function.Sequence.SequenceFunctions.ResetAll();
        Call("setval", "test_seq_nextval", 0L);

        var v1 = (long)Call("nextval", "test_seq_nextval")!;
        var v2 = (long)Call("nextval", "test_seq_nextval")!;
        Assert.Equal(v1 + 1, v2);
    }

    [Fact]
    public void Sequence_Currval_AfterNextval()
    {
        BogDb.Core.Function.Sequence.SequenceFunctions.ResetAll();
        Call("setval", "test_seq_curr", 10L);
        var v1 = (long)Call("nextval", "test_seq_curr")!;
        var curr = (long)Call("currval", "test_seq_curr")!;
        Assert.Equal(v1, curr);
    }
}
