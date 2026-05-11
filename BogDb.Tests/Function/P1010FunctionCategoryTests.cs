using System;
using System.Collections.Generic;
using Xunit;
using BogDb.Core.Function;
using BogDb.Core.Function.Uuid;
using BogDb.Core.Function.InternalId;
using BogDb.Core.Function.Table;
using BogDb.Core.Function.Export;
using BogDb.Core.Function.Gds;

namespace BogDb.Tests.Function;

/// <summary>
/// Tests for the 5 function categories added in P1-010:
///   uuid, internal_id, table, export, gds.
/// </summary>
public class P1010FunctionCategoryTests
{
    // ── UUID ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GenRandomUuid_ReturnsValidGuidString()
    {
        var result = FunctionDispatcher.Invoke("gen_random_uuid", Array.Empty<object?>());
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result!.ToString(), out _),
            $"Expected valid GUID, got: {result}");
    }

    [Fact]
    public void Uuid_AliasWorks()
    {
        var result = FunctionDispatcher.Invoke("uuid", Array.Empty<object?>());
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result!.ToString(), out _));
    }

    [Fact]
    public void GenRandomUuid_ProducesUniqueValues()
    {
        var a = FunctionDispatcher.Invoke("gen_random_uuid", Array.Empty<object?>())?.ToString();
        var b = FunctionDispatcher.Invoke("gen_random_uuid", Array.Empty<object?>())?.ToString();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void UuidToString_NormalisesCanonicalGuid()
    {
        var guid = "550E8400-E29B-41D4-A716-446655440000";
        var result = FunctionDispatcher.Invoke("uuid_to_string", new object?[] { guid });
        Assert.NotNull(result);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000", result!.ToString());
    }

    [Fact]
    public void StringToUuid_ParsesValidGuid()
    {
        var result = FunctionDispatcher.Invoke("string_to_uuid",
            new object?[] { "550e8400-e29b-41d4-a716-446655440000" });
        Assert.NotNull(result);
        Assert.True(Guid.TryParse(result!.ToString(), out _));
    }

    [Fact]
    public void StringToUuid_ReturnsNullForInvalidInput()
    {
        var result = FunctionDispatcher.Invoke("string_to_uuid", new object?[] { "not-a-uuid" });
        Assert.Null(result);
    }

    // ── InternalId ────────────────────────────────────────────────────────────

    [Fact]
    public void Offset_LongInput_ReturnsSameLong()
    {
        var result = FunctionDispatcher.Invoke("offset", new object?[] { 42L });
        Assert.Equal(42L, result);
    }

    [Fact]
    public void Offset_StringIdForm_ReturnsOffsetPart()
    {
        var result = FunctionDispatcher.Invoke("offset", new object?[] { "0:99" });
        Assert.Equal(99L, result);
    }

    [Fact]
    public void InternalId_LongInput_ReturnsTableColonOffset()
    {
        var result = FunctionDispatcher.Invoke("internal_id", new object?[] { 7L });
        Assert.Equal("0:7", result?.ToString());
    }

    [Fact]
    public void InternalId_DictWithId_ReturnsIdString()
    {
        var node = new Dictionary<string, object?> { ["id"] = 5L, ["name"] = "Alice" };
        var result = FunctionDispatcher.Invoke("internal_id", new object?[] { node });
        Assert.NotNull(result);
        Assert.Contains("5", result!.ToString());
    }

    [Fact]
    public void InternalIdEqual_SameOffset_ReturnsTrue()
    {
        var result = FunctionDispatcher.Invoke("internal_id_equal",
            new object?[] { "0:10", "0:10" });
        Assert.True((bool?)result ?? false);
    }

    [Fact]
    public void InternalIdEqual_DifferentOffset_ReturnsFalse()
    {
        var result = FunctionDispatcher.Invoke("internal_id_equal",
            new object?[] { "0:10", "0:11" });
        Assert.False((bool?)result ?? true);
    }

    // ── Table ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DbVersion_ReturnsNonEmptyString()
    {
        var result = FunctionDispatcher.Invoke("db_version", Array.Empty<object?>());
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.ToString()));
    }

    [Fact]
    public void CurrentSetting_KnownKey_ReturnsValue()
    {
        var result = FunctionDispatcher.Invoke("current_setting", new object?[] { "threads" });
        Assert.NotNull(result);
        Assert.Equal("4", result!.ToString());
    }

    [Fact]
    public void CurrentSetting_UnknownKey_ReturnsNull()
    {
        var result = FunctionDispatcher.Invoke("current_setting", new object?[] { "nonexistent_key" });
        Assert.Null(result);
    }

    [Fact]
    public void SetSetting_UpdatesValue_ThenCurrentSettingReflectsIt()
    {
        FunctionDispatcher.Invoke("set_setting", new object?[] { "threads", "8" });
        var result = FunctionDispatcher.Invoke("current_setting", new object?[] { "threads" });
        Assert.Equal("8", result?.ToString());
        // Restore
        FunctionDispatcher.Invoke("set_setting", new object?[] { "threads", "4" });
    }

    [Fact]
    public void ShowFunctionsContains_KnownFunction_ReturnsTrue()
    {
        var result = FunctionDispatcher.Invoke("show_functions_contains", new object?[] { "db_version" });
        Assert.True((bool?)result ?? false);
    }

    [Fact]
    public void ShowFunctionsContains_UnknownFunction_ReturnsFalse()
    {
        var result = FunctionDispatcher.Invoke("show_functions_contains",
            new object?[] { "this_function_does_not_exist_xyz" });
        Assert.False((bool?)result ?? true);
    }

    // ── Export ────────────────────────────────────────────────────────────────

    [Fact]
    public void CsvEscape_ValueWithComma_IsQuoted()
    {
        var result = FunctionDispatcher.Invoke("csv_escape", new object?[] { "hello, world", "," });
        Assert.NotNull(result);
        var s = result!.ToString()!;
        Assert.StartsWith("\"", s);
        Assert.EndsWith("\"", s);
    }

    [Fact]
    public void CsvEscape_PlainValue_NotQuoted()
    {
        var result = FunctionDispatcher.Invoke("csv_escape", new object?[] { "hello", "," });
        Assert.Equal("hello", result?.ToString());
    }

    [Fact]
    public void CsvQuote_AlwaysWrapsInDoubleQuotes()
    {
        var result = FunctionDispatcher.Invoke("csv_quote", new object?[] { "value" });
        Assert.Equal("\"value\"", result?.ToString());
    }

    [Fact]
    public void CsvQuote_InternalQuotesAreEscaped()
    {
        var result = FunctionDispatcher.Invoke("csv_quote", new object?[] { "say \"hello\"" });
        Assert.Equal("\"say \"\"hello\"\"\"", result?.ToString());
    }

    [Fact]
    public void FormatCsvRow_MultipleValues_JoinedWithComma()
    {
        var result = FunctionDispatcher.Invoke("format_csv_row", new object?[] { "a", "b", "c" });
        Assert.Equal("a,b,c", result?.ToString());
    }

    [Fact]
    public void ExportCsv_ReturnsPath()
    {
        var result = FunctionDispatcher.Invoke("export_csv", new object?[] { "/tmp/out.csv" });
        Assert.Equal("/tmp/out.csv", result?.ToString());
    }

    // ── GDS ───────────────────────────────────────────────────────────────────

    [Fact]
    public void GdsVersion_ReturnsNonEmptyString()
    {
        var result = FunctionDispatcher.Invoke("gds_version", Array.Empty<object?>());
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.ToString()));
    }

    [Fact]
    public void NodeDegree_ReturnsNonNegativeLong()
    {
        var result = FunctionDispatcher.Invoke("node_degree", new object?[] { 1L });
        Assert.NotNull(result);
        Assert.True(Convert.ToInt64(result) >= 0);
    }

    [Fact]
    public void HasPath_StubReturnsBool()
    {
        var result = FunctionDispatcher.Invoke("has_path", new object?[] { 1L, 2L });
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void PageRankScore_ReturnsDouble()
    {
        var result = FunctionDispatcher.Invoke("pagerank_score", new object?[] { 1L });
        Assert.IsType<double>(result);
    }

    [Fact]
    public void GraphDensity_ReturnsDouble()
    {
        var result = FunctionDispatcher.Invoke("graph_density", Array.Empty<object?>());
        Assert.IsType<double>(result);
    }
}
