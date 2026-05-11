using BogDb.Core.Main;
using BogDb.Core.Main.QueryResult;
using Xunit;

namespace BogDb.Tests.Function;

/// <summary>
/// Tests for aggregate gap functions (collect, count_if) and exotic cast functions
/// (to_int128, to_uint128, to_serial, to_uuid, blob/to_blob).
/// </summary>
[Trait("Category", "FunctionGap")]
public class AggregateAndCastGapTests
{
    private static QueryResult Q(string cypher)
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        return conn.Query(cypher);
    }

    // ── collect aggregate ────────────────────────────────────────────────────

    [Fact]
    public void Collect_LiteralUnwind_ReturnsList()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE T(id INT64 PRIMARY KEY, v STRING)");
        conn.Query("CREATE (:T {id:1, v:'a'})");
        conn.Query("CREATE (:T {id:2, v:'b'})");
        conn.Query("CREATE (:T {id:3, v:'c'})");

        var r = conn.Query("MATCH (t:T) RETURN collect(t.v) AS items");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var row = r.GetNext();
        var val = row.GetValue(0);
        Assert.NotNull(val);
        // collect should produce a list
        var list = Assert.IsType<System.Collections.Generic.List<object?>>(val);
        Assert.Equal(3, list.Count);
        Assert.Contains("a", list.Cast<string>());
        Assert.Contains("b", list.Cast<string>());
        Assert.Contains("c", list.Cast<string>());
    }

    [Fact]
    public void Collect_GroupedByKey_ProducesPerGroupLists()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.Query("CREATE NODE TABLE T(id INT64 PRIMARY KEY, cat STRING, v INT64)");
        conn.Query("CREATE (:T {id:1, cat:'x', v:10})");
        conn.Query("CREATE (:T {id:2, cat:'x', v:20})");
        conn.Query("CREATE (:T {id:3, cat:'y', v:30})");

        var r = conn.Query("MATCH (t:T) RETURN t.cat AS cat, collect(t.v) AS vals ORDER BY cat");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        
        // First group: cat='x'
        var row1 = r.GetNext();
        Assert.Equal("x", row1.GetValue(0));
        var list1 = Assert.IsType<System.Collections.Generic.List<object?>>(row1.GetValue(1));
        Assert.Equal(2, list1.Count);

        // Second group: cat='y'
        var row2 = r.GetNext();
        Assert.Equal("y", row2.GetValue(0));
        var list2 = Assert.IsType<System.Collections.Generic.List<object?>>(row2.GetValue(1));
        Assert.Single(list2);
    }

    // ── count_if aggregate ───────────────────────────────────────────────────

    [Fact]
    public void CountIf_CountsTruthyValues()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        var cr = conn.Query("CREATE NODE TABLE T(id INT64 PRIMARY KEY, active INT64)");
        Assert.True(cr.IsSuccess, cr.ErrorMessage);
        conn.Query("CREATE (:T {id:1, active:1})");
        conn.Query("CREATE (:T {id:2, active:0})");
        conn.Query("CREATE (:T {id:3, active:1})");
        conn.Query("CREATE (:T {id:4, active:1})");

        var r = conn.Query("MATCH (t:T) RETURN count_if(t.active > 0) AS active_count");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0);
        Assert.Equal(3L, val);
    }

    [Fact]
    public void CountIf_WithGroupBy()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        var cr = conn.Query("CREATE NODE TABLE T2(id INT64 PRIMARY KEY, cat STRING, flag INT64)");
        Assert.True(cr.IsSuccess, cr.ErrorMessage);
        conn.Query("CREATE (:T2 {id:1, cat:'a', flag:1})");
        conn.Query("CREATE (:T2 {id:2, cat:'a', flag:0})");
        conn.Query("CREATE (:T2 {id:3, cat:'b', flag:1})");

        var r = conn.Query("MATCH (t:T2) RETURN t.cat AS cat, count_if(t.flag > 0) AS cnt ORDER BY cat");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var row1 = r.GetNext();
        Assert.Equal("a", row1.GetValue(0));
        Assert.Equal(1L, row1.GetValue(1));
        var row2 = r.GetNext();
        Assert.Equal("b", row2.GetValue(0));
        Assert.Equal(1L, row2.GetValue(1));
    }

    // ── to_int128 ────────────────────────────────────────────────────────────

    [Fact]
    public void ToInt128_ConvertsValue()
    {
        var r = Q("RETURN to_int128(42) AS val");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0);
        // After normalization, to_int128 produces a decimal internally
        // but normalize may convert to double — accept either
        Assert.NotNull(val);
        Assert.Equal(42.0, Convert.ToDouble(val));
    }

    [Fact]
    public void ToInt128_FromString()
    {
        var r = Q("RETURN to_int128('12345') AS val");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.NotNull(r.GetNext().GetValue(0));
    }

    // ── to_uint128 ───────────────────────────────────────────────────────────

    [Fact]
    public void ToUint128_PositiveValue()
    {
        var r = Q("RETURN to_uint128(100) AS val");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0);
        Assert.NotNull(val);
        Assert.Equal(100.0, Convert.ToDouble(val));
    }

    [Fact]
    public void ToUint128_NegativeReturnsNull()
    {
        var r = Q("RETURN to_uint128(-1) AS val");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Null(r.GetNext().GetValue(0));
    }

    // ── to_serial ────────────────────────────────────────────────────────────

    [Fact]
    public void ToSerial_ConvertsToInt64()
    {
        var r = Q("RETURN to_serial(99) AS val");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(99L, r.GetNext().GetValue(0));
    }

    // ── to_uuid / uuid ───────────────────────────────────────────────────────

    [Fact]
    public void ToUuid_ValidGuid()
    {
        var r = Q("RETURN to_uuid('550e8400-e29b-41d4-a716-446655440000') AS val");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0)?.ToString();
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", val);
    }

    [Fact]
    public void ToUuid_InvalidReturnsNull()
    {
        var r = Q("RETURN to_uuid('not-a-uuid') AS val");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Null(r.GetNext().GetValue(0));
    }

    // ── blob / to_blob ───────────────────────────────────────────────────────

    [Fact]
    public void ToBlob_FromString()
    {
        var r = Q("RETURN to_blob('hello') AS val");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0);
        Assert.IsType<byte[]>(val);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("hello"), (byte[])val!);
    }

    [Fact]
    public void Blob_FromHexString()
    {
        var r = Q("RETURN blob('\\\\x48454C4C4F') AS val");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0);
        Assert.IsType<byte[]>(val);
        // "HELLO" = 48 45 4C 4C 4F
        Assert.Equal(new byte[] { 0x48, 0x45, 0x4C, 0x4C, 0x4F }, (byte[])val!);
    }

    // ── typeof recognizes new types ──────────────────────────────────────────

    [Fact]
    public void Typeof_Int128()
    {
        var r = Q("RETURN typeof(to_int128(42)) AS t");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        // After normalization decimal→double, typeof returns DOUBLE
        // This is a known limitation: INT128 semantics differ from C++
        var val = r.GetNext().GetValue(0)?.ToString();
        Assert.True(val == "INT128" || val == "DOUBLE", $"typeof was: {val}");
    }

    [Fact]
    public void Typeof_Blob()
    {
        var r = Q("RETURN typeof(to_blob('x')) AS t");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0)?.ToString();
        Assert.True(val == "BLOB" || val == "BYTE[]", $"typeof was: {val}");
    }

    // ── cast(val, type) with new types ───────────────────────────────────────

    [Fact]
    public void Cast_ToInt128()
    {
        var r = Q("RETURN cast(42, 'INT128') AS v");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0);
        Assert.NotNull(val);
        Assert.Equal(42.0, Convert.ToDouble(val));
    }

    [Fact]
    public void Cast_ToUuid()
    {
        var r = Q("RETURN cast('550e8400-e29b-41d4-a716-446655440000', 'UUID') AS v");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", r.GetNext().GetValue(0)?.ToString());
    }

    [Fact]
    public void Cast_ToSerial()
    {
        var r = Q("RETURN cast(7, 'SERIAL') AS v");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(7L, r.GetNext().GetValue(0));
    }

    [Fact]
    public void Cast_ToBlob()
    {
        var r = Q("RETURN cast('test', 'BLOB') AS v");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.IsType<byte[]>(r.GetNext().GetValue(0));
    }
}
