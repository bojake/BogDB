using System;
using System.Collections.Generic;
using System.IO;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Function;

/// <summary>
/// Tests for the hardened UtilityFunctions, GdsFunctions, and ExportFunctions
/// covering gaps identified in the audit (nvl2, equal_null, sha1, crc32,
/// bool_xor, version sync, window stubs, node_degree, has_path, graph_density,
/// k_hop_count, csv_header, write_csv_line, path validation).
/// </summary>
public class UtilityGdsExportFunctionTests
{
    private static object? Invoke(string fn, params object?[] args)
        => Core.Function.FunctionDispatcher.Invoke(fn, args);

    // ─────────────────────────────────────────────────────────────────────────
    // UtilityFunctions
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Nvl2_ValNotNull_ReturnsA()
    {
        // nvl2("x", "a", "b") → "a"
        Assert.Equal("a", Invoke("nvl2", "x", "a", "b"));
    }

    [Fact]
    public void Nvl2_ValNull_ReturnsB()
    {
        // nvl2(null, "a", "b") → "b"
        Assert.Equal("b", Invoke("nvl2", null, "a", "b"));
    }

    [Fact]
    public void EqualNull_BothNull_ReturnsTrue()
    {
        Assert.Equal(true, Invoke("equal_null", null, null));
    }

    [Fact]
    public void EqualNull_NullAndValue_ReturnsFalse()
    {
        Assert.Equal(false, Invoke("equal_null", null, "x"));
    }

    [Fact]
    public void EqualNull_SameValues_ReturnsTrue()
    {
        Assert.Equal(true, Invoke("equal_null", 42L, 42L));
    }

    [Fact]
    public void Sha1_ProducesCorrectLength()
    {
        var hash = Invoke("sha1", "abc")?.ToString();
        Assert.NotNull(hash);
        Assert.Equal(40, hash!.Length); // SHA-1 = 160 bits = 40 hex chars
    }

    [Fact]
    public void Crc32_KnownValue_MatchesExpected()
    {
        // CRC-32 of empty string = 0
        var result = Invoke("crc32", "");
        Assert.Equal(0L, result);
    }

    [Fact]
    public void Crc32_NonEmpty_ReturnsNonZero()
    {
        var result = Invoke("crc32", "BogDb");
        Assert.NotNull(result);
        Assert.NotEqual(0L, result);
    }

    [Fact]
    public void BoolXor_TrueFalse_ReturnsTrue()
    {
        Assert.Equal(true, Invoke("bool_xor", true, false));
    }

    [Fact]
    public void BoolXor_TrueTrue_ReturnsFalse()
    {
        Assert.Equal(false, Invoke("bool_xor", true, true));
    }

    [Fact]
    public void WindowStubs_DoNotThrow()
    {
        // These should return null, not throw "function not found"
        Assert.Null(Invoke("row_number"));
        Assert.Null(Invoke("rank"));
        Assert.Null(Invoke("dense_rank"));
        Assert.Null(Invoke("percent_rank"));
        Assert.Null(Invoke("cume_dist"));
    }

    [Fact]
    public void Error_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Invoke("error", "boom"));
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public void VersionFunction_MatchesTableFunctionsVersion()
    {
        var v = Invoke("version")?.ToString();
        Assert.NotNull(v);
        Assert.Equal(Core.Function.Table.TableFunctions.BogDbNgVersion, v);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GdsFunctions — requires database context wired in
    // ─────────────────────────────────────────────────────────────────────────

    private static BogDatabase MakeGraphDb()
    {
        var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person");
        conn.EnsureRelTable("KNOWS", "Person", "Person");
        conn.UpsertNodeById("Person", "alice", new Dictionary<string, object> { ["name"] = "Alice" });
        conn.UpsertNodeById("Person", "bob",   new Dictionary<string, object> { ["name"] = "Bob" });
        conn.Commit();
        return db;
    }

    [Fact]
    public void GdsVersion_ReturnsNonEmpty()
    {
        var v = Invoke("gds_version")?.ToString();
        Assert.NotNull(v);
        Assert.NotEmpty(v!);
    }

    [Fact]
    public void GraphDensity_IsolatedGraph_ReturnsZeroOrPositive()
    {
        using var db = MakeGraphDb();
        Core.Function.Gds.GdsFunctions.SetDatabaseContext(db);
        var density = Invoke("graph_density");
        Assert.IsType<double>(density);
        Assert.True((double)density! >= 0.0);
    }

    [Fact]
    public void NodeDegree_ValidNode_ReturnsNonNegative()
    {
        using var db = MakeGraphDb();
        Core.Function.Gds.GdsFunctions.SetDatabaseContext(db);
        // Node at offset 0 in table 0
        var deg = Invoke("node_degree", "0:0");
        Assert.NotNull(deg);
        Assert.True(Convert.ToInt64(deg) >= 0);
    }

    [Fact]
    public void HasPath_SameNode_ReturnsTrue()
    {
        using var db = MakeGraphDb();
        Core.Function.Gds.GdsFunctions.SetDatabaseContext(db);
        var result = Invoke("has_path", "0:0", "0:0");
        Assert.Equal(true, result);
    }

    [Fact]
    public void HasPath_UnreachableNode_ReturnsFalse()
    {
        using var db = MakeGraphDb();
        Core.Function.Gds.GdsFunctions.SetDatabaseContext(db);
        // maxHops=1, no KNOWS edge exists — isolated nodes
        var result = Invoke("has_path", "0:0", "0:1", 1L);
        Assert.Equal(false, result);
    }

    [Fact]
    public void KHopCount_NoNeighbors_ReturnsZero()
    {
        using var db = MakeGraphDb();
        Core.Function.Gds.GdsFunctions.SetDatabaseContext(db);
        var count = Invoke("k_hop_count", "0:0", 1L);
        Assert.Equal(0L, count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ExportFunctions
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CsvHeader_ProducesCommaSeparated()
    {
        var header = Invoke("csv_header", "name", "age", "city")?.ToString();
        Assert.Equal("name,age,city", header);
    }

    [Fact]
    public void CsvHeader_WithSpecialChars_QuotesThem()
    {
        // "dept,name" contains a comma — should be quoted
        var header = Invoke("csv_header", "dept,name")?.ToString();
        Assert.Equal("\"dept,name\"", header);
    }

    [Fact]
    public void ExportCsv_InvalidDirectory_ReturnsError()
    {
        var result = Invoke("export_csv", "/nonexistent_dir_xyz/out.csv")?.ToString();
        Assert.NotNull(result);
        Assert.StartsWith("[error:", result);
    }

    [Fact]
    public void ExportCsv_ValidTempPath_ReturnsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bogdb_test_{Guid.NewGuid():N}.csv");
        try
        {
            var result = Invoke("export_csv", path)?.ToString();
            Assert.Equal(path, result);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WriteCsvLine_AppendsToNewFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bogdb_csv_{Guid.NewGuid():N}.csv");
        try
        {
            var result = Invoke("write_csv_line", path, "Alice", "30", "NYC")?.ToString();
            Assert.Equal(path, result);
            var content = File.ReadAllText(path);
            Assert.Contains("Alice,30,NYC", content);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FormatCsvRow_EscapesCommaInValue()
    {
        // "Smith, John" contains a comma → should be quoted
        var row = Invoke("format_csv_row", "Smith, John", "42")?.ToString();
        Assert.Equal("\"Smith, John\",42", row);
    }
}
