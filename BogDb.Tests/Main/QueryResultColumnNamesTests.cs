using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.Main;
using BogRow = BogDb.Core.Main.QueryResult.BogRow;
using BogValue = BogDb.Core.Main.QueryResult.BogValue;
using QueryResult = BogDb.Core.Main.QueryResult.QueryResult;

namespace BogDb.Tests.Main;

/// <summary>
/// Tests covering Bug 1 (GetAsDictionary uses alias names) and
/// Bug 2 (ORDER BY can reference RETURN-level aliases).
/// </summary>
public class QueryResultColumnNamesTests
{
    private static BogConnection Setup()
    {
        var db   = BogDatabase.CreateInMemory();
        var conn = new BogConnection(db);
        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("P", new Dictionary<string, LogicalTypeID>
        {
            ["name"]  = LogicalTypeID.STRING,
            ["score"] = LogicalTypeID.DOUBLE,
        });
        conn.UpsertNodeById("P", "p1", new() { ["name"] = "Alice", ["score"] = 90.0 });
        conn.UpsertNodeById("P", "p2", new() { ["name"] = "Bob",   ["score"] = 70.0 });
        conn.UpsertNodeById("P", "p3", new() { ["name"] = "Carol", ["score"] = 80.0 });
        conn.Commit();
        return conn;
    }

    // ── Bug 1: GetAsDictionary uses alias names ───────────────────────────────

    [Fact]
    public void GetAsDictionary_UsesAliasNames_NotColN()
    {
        var conn = Setup();
        var r = conn.Query("MATCH (p:P) RETURN p.name AS person, p.score AS pts ORDER BY p.name");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());
        var row  = r.GetNext();
        var dict = row.GetAsDictionary();

        // Must have alias names, NOT col_0 / col_1
        Assert.True(dict.ContainsKey("person"),    $"Expected key 'person', got: {string.Join(", ", dict.Keys)}");
        Assert.True(dict.ContainsKey("pts"),        $"Expected key 'pts', got: {string.Join(", ", dict.Keys)}");
        Assert.False(dict.ContainsKey("col_0"),    "Should not contain legacy positional key col_0");
        Assert.Equal("Alice", dict["person"].ToString());
    }

    [Fact]
    public void QueryResult_ColumnNames_ReturnsAliasNames()
    {
        var conn = Setup();
        var r = conn.Query("MATCH (p:P) RETURN p.name AS person, p.score AS pts LIMIT 1");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal(new[] { "person", "pts" }, r.ColumnNames.ToArray());
        Assert.Equal(2, r.ColumnCount);
    }

    [Fact]
    public void QueryResult_ColumnTypes_InferProjectedScalarTypes()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN 1 AS n, 'Ada' AS name, to_days(1) AS delta");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(
            new[] { LogicalTypeID.INT64, LogicalTypeID.STRING, LogicalTypeID.INTERVAL },
            result.ColumnTypes.ToArray());
    }

    [Fact]
    public void QueryResult_ColumnLogicalTypes_ExposeDescriptorMetadata()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN 1 AS n, 'Ada' AS name, [1, 2] AS nums, to_days(1) AS delta");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Collection(
            result.ColumnLogicalTypes,
            logicalType =>
            {
                Assert.Equal(LogicalTypeID.INT64, logicalType.Id);
                Assert.True(logicalType.IsScalar);
                Assert.True(logicalType.IsNumeric);
                Assert.True(logicalType.IsIntegral);
                Assert.False(logicalType.IsNested);
            },
            logicalType =>
            {
                Assert.Equal(LogicalTypeID.STRING, logicalType.Id);
                Assert.True(logicalType.IsScalar);
                Assert.False(logicalType.IsNumeric);
            },
            logicalType =>
            {
                Assert.Equal(LogicalTypeID.LIST, logicalType.Id);
                Assert.True(logicalType.IsNested);
                Assert.False(logicalType.IsScalar);
            },
            logicalType =>
            {
                Assert.Equal(LogicalTypeID.INTERVAL, logicalType.Id);
                Assert.True(logicalType.IsTemporal);
                Assert.True(logicalType.IsScalar);
            });
    }

    [Fact]
    public void QueryResult_ColumnLogicalTypes_ExposeNestedShapeMetadata()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN [interval('1 day'), interval('2 days')] AS deltas, {day: interval('1 day'), count: 2} AS payload");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Collection(
            result.ColumnLogicalTypes,
            logicalType =>
            {
                Assert.Equal(LogicalTypeID.LIST, logicalType.Id);
                Assert.NotNull(logicalType.ElementType);
                Assert.Equal(LogicalTypeID.INTERVAL, logicalType.ElementType!.Id);
            },
            logicalType =>
            {
                Assert.Equal(LogicalTypeID.STRUCT, logicalType.Id);
                Assert.NotNull(logicalType.Fields);
                var fields = logicalType.Fields!.ToDictionary(field => field.Name, field => field.LogicalType);
                Assert.Equal(LogicalTypeID.INTERVAL, fields["day"].Id);
                Assert.Equal(LogicalTypeID.INT64, fields["count"].Id);
            });
    }

    [Fact]
    public void QueryResult_Columns_ExposeStructuredMetadata()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN 1 AS n, 'Ada' AS name, to_days(1) AS delta");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Collection(
            result.Columns,
            column =>
            {
                Assert.Equal("n", column.Name);
                Assert.Equal(0, column.Ordinal);
                Assert.Equal(LogicalTypeID.INT64, column.LogicalType.Id);
            },
            column =>
            {
                Assert.Equal("name", column.Name);
                Assert.Equal(1, column.Ordinal);
                Assert.Equal(LogicalTypeID.STRING, column.LogicalType.Id);
            },
            column =>
            {
                Assert.Equal("delta", column.Name);
                Assert.Equal(2, column.Ordinal);
                Assert.Equal(LogicalTypeID.INTERVAL, column.LogicalType.Id);
                Assert.True(column.LogicalType.IsTemporal);
            });
    }

    [Fact]
    public void QueryResult_ColumnTypes_PreserveDeclaredProjectionTypes_ForEmptyResults()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("UNWIND [] AS x RETURN x AS value, interval('1 day') AS delta");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(new[] { "value", "delta" }, result.ColumnNames.ToArray());
        Assert.Equal(new[] { LogicalTypeID.ANY, LogicalTypeID.INTERVAL }, result.ColumnTypes.ToArray());
        Assert.Collection(
            result.Columns,
            column => Assert.Equal(LogicalTypeID.ANY, column.LogicalType.Id),
            column => Assert.Equal(LogicalTypeID.INTERVAL, column.LogicalType.Id));
    }

    [Fact]
    public void QueryResult_Summary_ExposesHostFacingMetadata()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN 1 AS n, 'Ada' AS name");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());

        var summary = result.Summary;
        Assert.True(summary.IsSuccess);
        Assert.Equal(string.Empty, summary.ErrorMessage);
        Assert.Equal(2, summary.ColumnCount);
        Assert.Equal(1UL, summary.RowCount);
    }

    [Fact]
    public void QueryResult_GetColumnIndex_IsCaseInsensitive()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN 1 AS n, 'Ada' AS name");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.TryGetColumnIndex("NAME", out var idx));
        Assert.Equal(1, idx);
        Assert.Equal(0, result.GetColumnIndex("n"));
    }

    [Fact]
    public void QueryResult_ResetIterator_RewindsRows()
    {
        var conn = Setup();
        var r = conn.Query("MATCH (p:P) RETURN p.name AS person ORDER BY p.name");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());
        Assert.Equal("Alice", r.GetNext().GetString(0));
        Assert.Equal("Bob", r.GetNext().GetString(0));

        r.ResetIterator();

        Assert.True(r.HasNext());
        Assert.Equal("Alice", r.GetNext().GetString(0));
    }

    [Fact]
    public void GetAsDictionary_WithAggregateAlias_UsesAliasName()
    {
        var conn = Setup();
        var r = conn.Query("MATCH (p:P) RETURN count(p) AS total");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());
        var dict = r.GetNext().GetAsDictionary();

        Assert.True(dict.ContainsKey("total"), $"Expected key 'total', got: {string.Join(", ", dict.Keys)}");
        Assert.Equal(3L, (long)dict["total"]);
    }

    [Fact]
    public void BogRow_NameBasedAccessors_UseAliasNames()
    {
        var conn = Setup();
        var r = conn.Query("MATCH (p:P) RETURN p.name AS person, p.score AS pts ORDER BY p.name LIMIT 1");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var row = r.GetNext();

        Assert.Equal(2, row.Count);
        Assert.True(row.ContainsColumn("person"));
        Assert.True(row.TryGetColumnIndex("PTS", out var ptsIndex));
        Assert.Equal(1, ptsIndex);
        Assert.Equal("Alice", row.GetString("person"));
        Assert.Equal(90.0, row.GetDouble("pts"));
        Assert.Equal("Alice", row.GetValue("PERSON"));
        Assert.True(row.TryGetValue("pts", out var ptsValue));
        Assert.Equal(90.0, Assert.IsType<double>(ptsValue));
    }

    [Fact]
    public void BogRow_NameBasedLookup_ThrowsWhenColumnMissing()
    {
        var conn = Setup();
        var r = conn.Query("MATCH (p:P) RETURN p.name AS person LIMIT 1");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var row = r.GetNext();

        Assert.False(row.ContainsColumn("missing"));
        Assert.False(row.TryGetValue("missing", out _));
        Assert.Throws<KeyNotFoundException>(() => row.GetValue("missing"));
    }

    [Fact]
    public void BogRow_NameBasedLookup_IsUnavailableWithoutColumnNames()
    {
        var row = new BogRow(new object[] { 1L, "Ada" });

        Assert.False(row.ContainsColumn("name"));
        Assert.False(row.TryGetValue("name", out _));
        Assert.Throws<KeyNotFoundException>(() => row.GetString("name"));
    }

    [Fact]
    public void BogRow_GenericTypedGetters_UseAliasNames()
    {
        var conn = Setup();
        var r = conn.Query("MATCH (p:P) RETURN p.name AS person, p.score AS pts LIMIT 1");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var row = r.GetNext();

        Assert.Equal("Alice", row.Get<string>("person"));
        Assert.Equal(90.0, row.Get<double>("pts"));
        Assert.True(row.TryGet<double>("PTS", out var pts));
        Assert.Equal(90.0, pts);
    }

    [Fact]
    public void BogRow_GetBogValue_ExposesLogicalTypeAndTypedAccess()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var r = conn.Query("RETURN to_days(1) AS delta, 42 AS n, 'Ada' AS name");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var row = r.GetNext();
        var delta = row.GetBogValue("delta");
        var number = row.GetBogValue("n");
        var name = row.GetBogValue("name");

        Assert.Equal(LogicalTypeID.INTERVAL, delta.LogicalType);
        Assert.Equal("INTERVAL", delta.Type.Name);
        Assert.True(delta.Type.IsTemporal);
        Assert.Equal("P1D", delta.ToString());
        Assert.Equal("P1D", delta.As<BogDbInterval>().ToString());
        Assert.Equal(LogicalTypeID.INT64, number.LogicalType);
        Assert.True(number.Type.IsNumeric);
        Assert.True(number.Type.IsIntegral);
        Assert.Equal(42L, number.As<long>());
        Assert.Equal(LogicalTypeID.STRING, name.LogicalType);
        Assert.True(name.Type.IsScalar);
        Assert.Equal("Ada", name.As<string>());
    }

    [Fact]
    public void BogValue_FromObject_SupportsTypeAwareInterop()
    {
        var value = BogValue.FromObject("123");

        Assert.Equal(LogicalTypeID.STRING, value.LogicalType);
        Assert.Equal("123", value.Value);
        Assert.Equal(123L, value.As<long>());
        Assert.Equal("123", value.GetString());
    }

    [Fact]
    public void BogValue_AsList_ReturnsNestedTypedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN [to_days(1), 42, 'Ada'] AS values");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var values = result.GetNext().GetBogValue("values").AsList();

        Assert.Equal(3, values.Count);
        Assert.Equal(LogicalTypeID.INTERVAL, values[0].LogicalType);
        Assert.Equal("P1D", values[0].ToString());
        Assert.Equal(42L, values[1].As<long>());
        Assert.Equal("Ada", values[2].As<string>());
    }

    [Fact]
    public void BogValue_AsDictionary_ReturnsNestedTypedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN map(['day', 'count', 'label'], [to_days(1), 42, 'Ada']) AS parts");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var parts = result.GetNext().GetBogValue("parts").AsDictionary();

        Assert.Equal(LogicalTypeID.INTERVAL, parts["day"].LogicalType);
        Assert.Equal("P1D", parts["day"].ToString());
        Assert.Equal(42L, parts["count"].As<long>());
        Assert.Equal("Ada", parts["label"].As<string>());
    }

    [Fact]
    public void BogValue_AsDictionary_SupportsNestedLists()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var result = conn.Query("RETURN map(['nested'], [[to_days(1), to_hours(25)]]) AS payload");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var payload = result.GetNext().GetBogValue("payload").AsDictionary();
        var nested = payload["nested"].AsList();

        Assert.Equal(new[] { "P1D", "P1DT1H" }, nested.Select(v => v.ToString()).ToArray());
    }

    [Fact]
    public void BogValue_Type_ExposesNestedTypeDescriptors()
    {
        var value = BogValue.FromObject(new Dictionary<string, object?>
        {
            ["deltas"] = new object[] { BogDbInterval.FromDays(1), BogDbInterval.FromDays(2) },
            ["count"] = 2L
        });

        Assert.Equal(LogicalTypeID.STRUCT, value.Type.Id);
        Assert.NotNull(value.Type.Fields);

        var fields = value.Type.Fields!.ToDictionary(field => field.Name, field => field.LogicalType);
        Assert.Equal(LogicalTypeID.LIST, fields["deltas"].Id);
        Assert.NotNull(fields["deltas"].ElementType);
        Assert.Equal(LogicalTypeID.INTERVAL, fields["deltas"].ElementType!.Id);
        Assert.Equal(LogicalTypeID.INT64, fields["count"].Id);
    }

    // ── Bug 2: ORDER BY can reference RETURN-level aliases ───────────────────

    [Fact]
    public void OrderBy_CanReference_ReturnAlias_Ascending()
    {
        // RETURN p.score AS pts ORDER BY pts  ← pts is a RETURN alias
        var conn = Setup();
        var r = conn.Query("MATCH (p:P) RETURN p.name AS person, p.score AS pts ORDER BY pts");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var rows = new List<BogRow>();
        while (r.HasNext()) rows.Add(r.GetNext());

        Assert.Equal(3, rows.Count);
        // Ascending: Bob (70), Carol (80), Alice (90)
        Assert.Equal("Bob",   rows[0].GetString(0));
        Assert.Equal("Carol", rows[1].GetString(0));
        Assert.Equal("Alice", rows[2].GetString(0));
    }

    [Fact]
    public void OrderBy_CanReference_ReturnAlias_Descending()
    {
        var conn = Setup();
        var r = conn.Query("MATCH (p:P) RETURN p.name AS person, p.score AS pts ORDER BY pts DESC");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var rows = new List<BogRow>();
        while (r.HasNext()) rows.Add(r.GetNext());

        Assert.Equal(3, rows.Count);
        // Descending: Alice (90), Carol (80), Bob (70)
        Assert.Equal("Alice", rows[0].GetString(0));
        Assert.Equal("Carol", rows[1].GetString(0));
        Assert.Equal("Bob",   rows[2].GetString(0));
    }

    [Fact]
    public void OrderBy_CanReference_ComputedReturnAlias()
    {
        // RETURN p.score * 2 AS doubled ORDER BY doubled DESC
        var conn = Setup();
        var r = conn.Query(
            "MATCH (p:P) RETURN p.name AS person, p.score * 2.0 AS doubled ORDER BY doubled DESC");

        Assert.True(r.IsSuccess, r.ErrorMessage);
        var rows = new List<BogRow>();
        while (r.HasNext()) rows.Add(r.GetNext());

        Assert.Equal(3, rows.Count);
        Assert.Equal("Alice", rows[0].GetString(0)); // 180
        Assert.Equal("Carol", rows[1].GetString(0)); // 160
        Assert.Equal("Bob",   rows[2].GetString(0)); // 140
    }

    [Fact]
    public void GetAsDictionary_ListContainingIntervals_ReturnsReadableNestedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var r = conn.Query("RETURN [to_days(1), to_hours(25), 'tag'] AS values");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());

        var dict = r.GetNext().GetAsDictionary();
        var values = Assert.IsType<List<object?>>(dict["values"]);
        Assert.Equal(new object?[] { "P1D", "P1DT1H", "tag" }, values);
    }

    [Fact]
    public void GetAsDictionary_MapContainingIntervals_ReturnsReadableNestedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var r = conn.Query("RETURN map(['day', 'hour'], [to_days(1), to_hours(25)]) AS parts");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());

        var dict = r.GetNext().GetAsDictionary();
        var parts = Assert.IsType<Dictionary<string, object?>>(dict["parts"]);
        Assert.Equal("P1D", parts["day"]);
        Assert.Equal("P1DT1H", parts["hour"]);
    }

    [Fact]
    public void GetAsDictionary_NestedMapListContainingIntervals_ReturnsReadableNestedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var r = conn.Query("RETURN map(['nested'], [[to_days(1), to_hours(25)]]) AS nested_parts");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());

        var dict = r.GetNext().GetAsDictionary();
        var parts = Assert.IsType<Dictionary<string, object?>>(dict["nested_parts"]);
        var nested = Assert.IsType<List<object?>>(parts["nested"]);
        Assert.Equal(new object?[] { "P1D", "P1DT1H" }, nested);
    }

    [Fact]
    public void BogRow_ToString_FormatsNestedIntervalValuesReadably()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var r = conn.Query("RETURN [to_days(1), to_hours(25)] AS values, map(['day'], [to_days(1)]) AS parts");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());

        var row = r.GetNext();
        Assert.Equal("[P1D,P1DT1H] | {\"day\":P1D}", row.ToString());
    }

    [Fact]
    public void GetAsDictionary_StructContainingIntervals_ReturnsReadableNestedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var r = conn.Query("RETURN struct_pack('day', to_days(1), 'hour', to_hours(25)) AS s");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());

        var dict = r.GetNext().GetAsDictionary();
        var s = Assert.IsType<Dictionary<string, object?>>(dict["s"]);
        Assert.Equal("P1D", s["day"]);
        Assert.Equal("P1DT1H", s["hour"]);
    }

    [Fact]
    public void GetAsDictionary_NestedStructContainingIntervals_ReturnsReadableNestedValues()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var r = conn.Query("RETURN struct_pack('nested', struct_pack('delta', to_days(1)), 'listy', [to_hours(25)]) AS s");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());

        var dict = r.GetNext().GetAsDictionary();
        var s = Assert.IsType<Dictionary<string, object?>>(dict["s"]);
        var nested = Assert.IsType<Dictionary<string, object?>>(s["nested"]);
        var listy = Assert.IsType<List<object?>>(s["listy"]);
        Assert.Equal("P1D", nested["delta"]);
        Assert.Equal(new object?[] { "P1DT1H" }, listy);
    }

    [Fact]
    public void QueryEngine_ListEquality_ComparesNestedValuesStructurally()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var r = conn.Query("RETURN [1, 2] = [1, 2] AS eq, [1, 2] <> [1, 3] AS ne");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());

        var row = r.GetNext();
        Assert.True(row.GetBoolean(0));
        Assert.True(row.GetBoolean(1));
    }

    [Fact]
    public void QueryEngine_StructEquality_ComparesNestedValuesStructurally()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        var r = conn.Query("RETURN struct_pack('a', 1) = struct_pack('a', 1) AS eq, struct_pack('a', 1) <> struct_pack('a', 2) AS ne");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.True(r.HasNext());

        var row = r.GetNext();
        Assert.True(row.GetBoolean(0));
        Assert.True(row.GetBoolean(1));
    }
}
