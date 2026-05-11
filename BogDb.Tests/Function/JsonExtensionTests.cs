using BogDb.Core.Main;
using BogDb.Core.Main.QueryResult;
using BogDb.Extensions.Json;
using Xunit;

namespace BogDb.Tests.Function;

/// <summary>
/// Tests for the complete Json extension scalar function surface.
/// Verifies parity with C++ json extension.
/// </summary>
[Trait("Category", "JsonExtension")]
public class JsonExtensionTests
{
    private static QueryResult Q(string cypher)
    {
        using var db = BogDatabase.CreateInMemory();
        new JsonExtension().Load(db);
        using var conn = new BogConnection(db);
        return conn.Query(cypher);
    }

    // ── json_keys ─────────────────────────────────────────────────────────

    [Fact]
    public void JsonKeys_ReturnsObjectKeys()
    {
        var r = Q("RETURN json_keys('{\"a\":1,\"b\":2,\"c\":3}') AS keys");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0);
        var list = Assert.IsType<System.Collections.Generic.List<object?>>(val);
        Assert.Equal(3, list.Count);
        Assert.Contains("a", list.Cast<string>());
        Assert.Contains("b", list.Cast<string>());
        Assert.Contains("c", list.Cast<string>());
    }

    [Fact]
    public void JsonKeys_ArrayReturnsNull()
    {
        var r = Q("RETURN json_keys('[1,2,3]') AS keys");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Null(r.GetNext().GetValue(0));
    }

    // ── json_contains ─────────────────────────────────────────────────────

    [Fact]
    public void JsonContains_ObjectSubset()
    {
        var r = Q("RETURN json_contains('{\"a\":1,\"b\":2}', '{\"a\":1}') AS result");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(true, r.GetNext().GetValue(0));
    }

    [Fact]
    public void JsonContains_MissingKey()
    {
        var r = Q("RETURN json_contains('{\"a\":1}', '{\"b\":1}') AS result");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(false, r.GetNext().GetValue(0));
    }

    // ── json_merge_patch ──────────────────────────────────────────────────

    [Fact]
    public void JsonMergePatch_MergesObjects()
    {
        var r = Q("RETURN json_merge_patch('{\"a\":1,\"b\":2}', '{\"b\":3,\"c\":4}') AS merged");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var json = r.GetNext().GetValue(0)?.ToString();
        Assert.NotNull(json);
        Assert.Contains("\"a\"", json);
        Assert.Contains("\"b\":3", json);
        Assert.Contains("\"c\":4", json);
    }

    [Fact]
    public void JsonMergePatch_NullRemovesKey()
    {
        var r = Q("RETURN json_merge_patch('{\"a\":1,\"b\":2}', '{\"b\":null}') AS merged");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var json = r.GetNext().GetValue(0)?.ToString();
        Assert.NotNull(json);
        Assert.DoesNotContain("\"b\"", json);
    }

    // ── json_array ────────────────────────────────────────────────────────

    [Fact]
    public void JsonArray_BuildsArray()
    {
        var r = Q("RETURN json_array(1, 'hello', 3) AS arr");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0)?.ToString();
        Assert.NotNull(val);
        Assert.StartsWith("[", val);
        Assert.EndsWith("]", val);
    }

    // ── json_object ───────────────────────────────────────────────────────

    [Fact]
    public void JsonObject_BuildsObject()
    {
        var r = Q("RETURN json_object('name', 'Alice', 'age', 30) AS obj");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0)?.ToString();
        Assert.NotNull(val);
        Assert.Contains("\"name\"", val);
        Assert.Contains("Alice", val);
    }

    // ── json_quote ────────────────────────────────────────────────────────

    [Fact]
    public void JsonQuote_QuotesString()
    {
        var r = Q("RETURN json_quote('hello world') AS quoted");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0)?.ToString();
        Assert.Equal("\"hello world\"", val);
    }

    [Fact]
    public void JsonQuote_NullReturnsNull()
    {
        var r = Q("RETURN json_quote(null) AS quoted");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("null", r.GetNext().GetValue(0)?.ToString());
    }

    // ── json_structure ────────────────────────────────────────────────────

    [Fact]
    public void JsonStructure_DescribesObject()
    {
        var r = Q("RETURN json_structure('{\"name\":\"Alice\",\"age\":30}') AS structure");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0)?.ToString();
        Assert.NotNull(val);
        Assert.StartsWith("STRUCT(", val);
        Assert.Contains("name", val);
    }

    [Fact]
    public void JsonStructure_DescribesArray()
    {
        var r = Q("RETURN json_structure('[1,2,3]') AS structure");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0)?.ToString();
        Assert.NotNull(val);
        Assert.Contains("UBIGINT[]", val);
    }

    // ── to_json / cast_to_json ────────────────────────────────────────────

    [Fact]
    public void ToJson_Number()
    {
        var r = Q("RETURN to_json(42) AS j");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("42", r.GetNext().GetValue(0)?.ToString());
    }

    [Fact]
    public void CastToJson_String()
    {
        var r = Q("RETURN cast_to_json('hello') AS j");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("\"hello\"", r.GetNext().GetValue(0)?.ToString());
    }

    // ── json() validator ──────────────────────────────────────────────────

    [Fact]
    public void Json_ValidReturnsString()
    {
        var r = Q("RETURN json('{\"a\":1}') AS j");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("{\"a\":1}", r.GetNext().GetValue(0));
    }

    [Fact]
    public void Json_InvalidReturnsNull()
    {
        var r = Q("RETURN json('not json') AS j");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Null(r.GetNext().GetValue(0));
    }

    // ── Existing functions still work ────────────────────────────────────

    [Fact]
    public void JsonValid_True()
    {
        var r = Q("RETURN json_valid('{\"a\":1}') AS v");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(true, r.GetNext().GetValue(0));
    }

    [Fact]
    public void JsonExtract_Path()
    {
        var r = Q("RETURN json_extract('{\"a\":{\"b\":42}}', '$.a.b') AS v");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(42L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void JsonArrayLength_Works()
    {
        var r = Q("RETURN json_array_length('[1,2,3,4]') AS len");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(4L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void JsonType_Object()
    {
        var r = Q("RETURN json_type('{\"a\":1}') AS t");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("OBJECT", r.GetNext().GetValue(0));
    }
}
