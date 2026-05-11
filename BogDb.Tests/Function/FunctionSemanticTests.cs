using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Main;
using BogDb.Core.Extension;
using Xunit;

namespace BogDb.Tests.Function;

/// <summary>
/// P1-055: Semantic-correctness tests for P1-050 through P1-054 hardening.
/// </summary>
public class FunctionSemanticTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static object? Invoke(string name, params object?[] args)
        => Core.Function.FunctionDispatcher.Invoke(name, args);

    private static BogDatabase MakeDb()
    {
        var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person");
        conn.EnsureNodeTable("City");
        conn.EnsureRelTable("KNOWS", "Person", "Person");
        conn.Commit();
        return db;
    }

    // ── P1-050: list_sort uses IComparable, not ToString ──────────────────────

    [Fact]
    public void ListSort_NumericList_SortsNumerically()
    {
        // [10, 2, 9] sorted by ToString = {"10","2","9"} but numerically = {2,9,10}
        var result = Invoke("list_sort", new List<object?> { 10L, 2L, 9L }) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(new long[] { 2, 9, 10 }, result!.Cast<long>().ToArray());
    }

    [Fact]
    public void ListReverseSort_NumericList_SortsDescNumerically()
    {
        var result = Invoke("list_reverse_sort", new List<object?> { 10L, 2L, 9L }) as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(new long[] { 10, 9, 2 }, result!.Cast<long>().ToArray());
    }

    [Fact]
    public void ListSort_MixedNullsLast_NullFirst()
    {
        // BogDb: NULL < non-null in sort order
        var result = Invoke("list_sort", new List<object?> { 5L, null, 1L }) as List<object?>;
        Assert.NotNull(result);
        Assert.Null(result![0]);
        Assert.Equal(1L, result[1]);
        Assert.Equal(5L, result[2]);
    }

    [Fact]
    public void ListTransform_AppliesUpperToEachElement()
    {
        var list = new List<object?> { "hello", "world" };
        var result = Invoke("list_transform", list, "toupper") as List<object?>;
        Assert.NotNull(result);
        Assert.Equal(new[] { "HELLO", "WORLD" }, result!.Cast<string>().ToArray());
    }

    // ── P1-051: printf / format real % substitution ───────────────────────────

    [Fact]
    public void Printf_StringSubstitution_Works()
    {
        var result = Invoke("printf", "Hello, %s!", "World");
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void Printf_IntegerSubstitution_Works()
    {
        var result = Invoke("printf", "Value: %d", 42L);
        Assert.Equal("Value: 42", result);
    }

    [Fact]
    public void Printf_FloatSubstitution_Works()
    {
        var result = Invoke("printf", "Pi: %.2g", 3.14);   // %g uses G format
        // Just verify it contains 3.14 somehow
        Assert.Contains("3.14", result?.ToString() ?? "");
    }

    [Fact]
    public void Base64_RoundTrip()
    {
        var encoded = Invoke("base64_encode", "Hello BogDb");
        var decoded  = Invoke("base64_decode", encoded);
        Assert.Equal("Hello BogDb", decoded);
    }

    [Fact]
    public void ToHex_FromHex_RoundTrip()
    {
        var hex = Invoke("to_hex", 255L);
        Assert.Equal("ff", hex);
        var back = Invoke("from_hex", hex);
        Assert.Equal(255L, back);
    }

    [Fact]
    public void BitLength_AsciiString_IsEightTimesOctetLength()
    {
        var bits   = Invoke("bit_length", "abc");
        var octets = Invoke("octet_length", "abc");
        Assert.Equal(24L, bits);
        Assert.Equal(3L, octets);
    }

    // ── P1-052: MapFunctions additions ────────────────────────────────────────

    private static Dictionary<string, object?> MakeMap(params (string k, object? v)[] pairs)
        => pairs.ToDictionary(p => p.k, p => p.v);

    [Fact]
    public void MapContains_ExistingKey_ReturnsTrue()
    {
        var map = MakeMap(("a", 1L), ("b", 2L));
        Assert.Equal(true, Invoke("map_contains", map, "a"));
    }

    [Fact]
    public void MapContains_MissingKey_ReturnsFalse()
    {
        var map = MakeMap(("a", 1L));
        Assert.Equal(false, Invoke("map_contains", map, "z"));
    }

    [Fact]
    public void MapSize_ReturnsEntryCount()
    {
        var map = MakeMap(("x", 1L), ("y", 2L), ("z", 3L));
        Assert.Equal(3L, Invoke("map_size", map));
    }

    [Fact]
    public void MapEntries_ReturnsKeyValueStructList()
    {
        var map = MakeMap(("a", 10L));
        var entries = Invoke("map_entries", map) as List<object?>;
        Assert.NotNull(entries);
        Assert.Single(entries!);
        var entry = entries[0] as Dictionary<string, object?>;
        Assert.NotNull(entry);
        Assert.Equal("a", entry!["key"]?.ToString());
        Assert.Equal(10L, entry["value"]);
    }

    // ── P1-053: catalog-wired TableFunctions ──────────────────────────────────

    [Fact]
    public void ShowTablesCount_ReturnsNodePlusRelTableCount()
    {
        using var db = MakeDb(); // Person, City nodes + KNOWS rel = 3 tables
        Core.Function.Table.TableFunctions.SetCatalogContext(db);
        var count = Invoke("show_tables_count");
        Assert.Equal(3L, count);
    }

    [Fact]
    public void TableType_NodeTable_ReturnsNode()
    {
        using var db = MakeDb();
        Core.Function.Table.TableFunctions.SetCatalogContext(db);
        Assert.Equal("NODE", Invoke("table_type", "Person"));
    }

    [Fact]
    public void TableType_RelTable_ReturnsRel()
    {
        using var db = MakeDb();
        Core.Function.Table.TableFunctions.SetCatalogContext(db);
        Assert.Equal("REL", Invoke("table_type", "KNOWS"));
    }

    // ── P1-054: CALL ITableFunction dispatch ──────────────────────────────────

    private sealed class GreetTableFunction : ITableFunction
    {
        public string Name => "greet";
        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string, string)> { ("greeting", "STRING") };
        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            var name = args.Count > 0 ? args[0]?.ToString() ?? "World" : "World";
            yield return new Dictionary<string, object?> { ["greeting"] = $"Hello, {name}!" };
        }
    }

    [Fact]
    public void CallTableFunction_Dispatch_ReturnsRows()
    {
        using var db = MakeDb();
        db.FunctionRegistry.Register(new GreetTableFunction());
        using var conn = new BogConnection(db);
        var result = conn.Query("CALL greet('BogDb') RETURN *");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext();
        Assert.Equal("Hello, BogDb!", row.GetValue(0));
    }
}
