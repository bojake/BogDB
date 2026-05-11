using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class PreparedStatementTests
{
    [Fact]
    public void Prepare_ValidQuery_ReturnsSuccessfulPreparedStatement()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING
        });
        conn.Commit();

        using var prepared = conn.Prepare("MATCH (p:Person) WHERE p.name = $name RETURN p.name AS name");

        Assert.True(prepared.IsSuccess);
        Assert.Equal(string.Empty, prepared.ErrorMessage);
        Assert.Equal(new[] { "name" }, prepared.ParameterNames);
    }

    [Fact]
    public void Prepare_InvalidQuery_CapturesError()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare("MATCH (p:Person RETURN p");

        Assert.False(prepared.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(prepared.ErrorMessage));
    }

    [Fact]
    public void Prepare_ParameterNames_ReturnDistinctOrderedNames()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare(
            "MATCH (p:Person) WHERE p.name = $name OR p.alias = $name OR p.age > $min_age RETURN p.name AS name");

        Assert.Equal(new[] { "name", "min_age" }, prepared.ParameterNames);
    }

    [Fact]
    public void Prepare_ExposesBindingStateMetadata()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare(
            "MATCH (p:Person) WHERE p.name = $name AND p.age > $min_age RETURN p.name AS name");

        Assert.Equal(2, prepared.ParameterCount);
        Assert.False(prepared.HasBindings);
        Assert.False(prepared.AreAllParametersBound);
        Assert.True(prepared.HasParameter("name"));
        Assert.True(prepared.HasParameter("$min_age"));
        Assert.False(prepared.IsBound("name"));
        Assert.Equal(new[] { "name", "min_age" }, prepared.MissingParameterNames);

        prepared.Bind("$name", "Ada");

        Assert.True(prepared.HasBindings);
        Assert.True(prepared.IsBound("name"));
        Assert.False(prepared.IsBound("min_age"));
        Assert.Equal(new[] { "name" }, prepared.BoundParameterNames);
        Assert.Equal(new[] { "min_age" }, prepared.MissingParameterNames);

        prepared.Bind("min_age", 30L);

        Assert.True(prepared.AreAllParametersBound);
        Assert.Equal(new[] { "name", "min_age" }, prepared.BoundParameterNames);
        Assert.Empty(prepared.MissingParameterNames);
    }

    [Fact]
    public void Prepare_Parameters_ExposeStructuredMetadata()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare(
            "MATCH (p:Person) WHERE p.name = $name AND p.age > $min_age RETURN p.name AS name");

        prepared.Bind("name", "Ada");

        Assert.Collection(
            prepared.Parameters,
            parameter =>
            {
                Assert.Equal("name", parameter.Name);
                Assert.Equal(0, parameter.Ordinal);
                Assert.True(parameter.IsBound);
                Assert.Equal(LogicalTypeID.ANY, parameter.ExpectedLogicalType.Id);
                Assert.Equal(LogicalTypeID.STRING, parameter.LogicalType.Id);
                Assert.Equal("Ada", parameter.Value);
            },
            parameter =>
            {
                Assert.Equal("min_age", parameter.Name);
                Assert.Equal(1, parameter.Ordinal);
                Assert.False(parameter.IsBound);
                Assert.Equal(LogicalTypeID.ANY, parameter.ExpectedLogicalType.Id);
                Assert.Equal(LogicalTypeID.ANY, parameter.LogicalType.Id);
                Assert.Null(parameter.Value);
            });
    }

    [Fact]
    public void Prepare_ResultColumns_ExposeBoundProjectionMetadata()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare("RETURN 1 AS n, 'Ada' AS name, interval('1 day') AS delta");

        Assert.True(prepared.IsSuccess, prepared.ErrorMessage);
        Assert.Equal(3, prepared.ResultColumnCount);
        Assert.Equal(new[] { "n", "name", "delta" }, prepared.ResultColumnNames);
        Assert.Collection(
            prepared.ResultColumns,
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
            });
    }

    [Fact]
    public void Prepare_Parameters_ExposeExpectedTypes_FromBoundComparisonsAndClauses()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING,
            ["age"] = LogicalTypeID.INT64
        });
        conn.Commit();

        using var prepared = conn.Prepare(
            "MATCH (p:Person) WHERE p.name = $name AND p.age > $min_age RETURN p.name AS name LIMIT $limit");

        Assert.True(prepared.IsSuccess, prepared.ErrorMessage);
        Assert.Collection(
            prepared.Parameters,
            parameter =>
            {
                Assert.Equal("name", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
                Assert.Equal(LogicalTypeID.STRING, parameter.LogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("min_age", parameter.Name);
                Assert.Equal(LogicalTypeID.INT64, parameter.ExpectedLogicalType.Id);
                Assert.Equal(LogicalTypeID.INT64, parameter.LogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("limit", parameter.Name);
                Assert.Equal(LogicalTypeID.INT64, parameter.ExpectedLogicalType.Id);
                Assert.Equal(LogicalTypeID.INT64, parameter.LogicalType.Id);
            });
    }

    [Fact]
    public void Prepare_Parameters_ExposeExpectedTypes_FromBuiltInFunctions()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare(
            "RETURN interval($interval_text) AS delta, starts_with($prefix_source, $prefix) AS ok, sqrt($value) AS root");

        Assert.True(prepared.IsSuccess, prepared.ErrorMessage);
        Assert.Collection(
            prepared.Parameters,
            parameter =>
            {
                Assert.Equal("interval_text", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
                Assert.Equal(LogicalTypeID.STRING, parameter.LogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("prefix_source", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
                Assert.Equal(LogicalTypeID.STRING, parameter.LogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("prefix", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
                Assert.Equal(LogicalTypeID.STRING, parameter.LogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("value", parameter.Name);
                Assert.Equal(LogicalTypeID.DOUBLE, parameter.ExpectedLogicalType.Id);
                Assert.Equal(LogicalTypeID.DOUBLE, parameter.LogicalType.Id);
            });
    }

    [Fact]
    public void Prepare_Parameters_ExposeExpectedTypes_FromMixedSignatureBuiltInFunctions()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare(
            "RETURN substring($text, $start, $len) AS piece, " +
            "split_part($csv, $delim, $index) AS token, " +
            "lpad($pad_text, $width, $fill) AS padded, " +
            "regexp_extract($source, $pattern, $grp) AS match");

        Assert.True(prepared.IsSuccess, prepared.ErrorMessage);
        Assert.Collection(
            prepared.Parameters,
            parameter =>
            {
                Assert.Equal("text", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("start", parameter.Name);
                Assert.Equal(LogicalTypeID.INT64, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("len", parameter.Name);
                Assert.Equal(LogicalTypeID.INT64, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("csv", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("delim", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("index", parameter.Name);
                Assert.Equal(LogicalTypeID.INT64, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("pad_text", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("width", parameter.Name);
                Assert.Equal(LogicalTypeID.INT64, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("fill", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("source", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("pattern", parameter.Name);
                Assert.Equal(LogicalTypeID.STRING, parameter.ExpectedLogicalType.Id);
            },
            parameter =>
            {
                Assert.Equal("grp", parameter.Name);
                Assert.Equal(LogicalTypeID.INT64, parameter.ExpectedLogicalType.Id);
            });
    }

    [Fact]
    public void Prepare_Parameters_ExposeExpectedTypes_FromTimestampAndCollectionFunctions()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare(
            "RETURN timestamp_add($unit, $ts, $amount) AS shifted, " +
            "range($range_from, $range_to, $range_step) AS span, " +
            "list_slice($items, $slice_start, $slice_end) AS subset, " +
            "map($keys, $vals) AS pairs");

        Assert.True(prepared.IsSuccess, prepared.ErrorMessage);
        Assert.Collection(
            prepared.Parameters,
            p => { Assert.Equal("unit", p.Name); Assert.Equal(LogicalTypeID.STRING, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("ts", p.Name); Assert.Equal(LogicalTypeID.TIMESTAMP_TZ, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("amount", p.Name); Assert.Equal(LogicalTypeID.INT64, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("range_from", p.Name); Assert.Equal(LogicalTypeID.INT64, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("range_to", p.Name); Assert.Equal(LogicalTypeID.INT64, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("range_step", p.Name); Assert.Equal(LogicalTypeID.INT64, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("items", p.Name); Assert.Equal(LogicalTypeID.ANY, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("slice_start", p.Name); Assert.Equal(LogicalTypeID.INT64, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("slice_end", p.Name); Assert.Equal(LogicalTypeID.INT64, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("keys", p.Name); Assert.Equal(LogicalTypeID.LIST, p.ExpectedLogicalType.Id); },
            p => { Assert.Equal("vals", p.Name); Assert.Equal(LogicalTypeID.LIST, p.ExpectedLogicalType.Id); });
    }

    [Fact]
    public void Bind_RejectsClearlyIncompatibleValueForExpectedNumericParameter()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare("RETURN sqrt($value) AS root");

        var ex = Assert.Throws<InvalidOperationException>(() => prepared.Bind("value", true));

        Assert.Equal("Parameter '$value' expects DOUBLE but got BOOL.", ex.Message);
    }

    [Fact]
    public void Bind_RejectsClearlyIncompatibleValueForExpectedStringPositionParameter()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare("RETURN substring($text, $start, $len) AS piece");

        var ex = Assert.Throws<InvalidOperationException>(() => prepared.Bind("start", "oops"));

        Assert.Equal("Parameter '$start' expects INT64 but got STRING.", ex.Message);
    }

    [Fact]
    public void Bind_RejectsClearlyIncompatibleValueForExpectedTimestampParameter()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare("RETURN timestamp_add($unit, $ts, $amount) AS shifted");

        var ex = Assert.Throws<InvalidOperationException>(() => prepared.Bind("ts", 42));

        Assert.Equal("Parameter '$ts' expects TIMESTAMP_TZ but got INT64.", ex.Message);
    }

    [Fact]
    public void Bind_NormalizesCompatibleNumericValuesForExpectedNumericParameter()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);

        using var prepared = conn.Prepare("RETURN sqrt($value) AS root");

        prepared.Bind("value", 9);

        Assert.True(prepared.IsBound("value"));
        Assert.Equal(9L, prepared.Bindings["value"]);
        Assert.Equal(LogicalTypeID.INT64, prepared.Parameters.Single().LogicalType.Id);
        Assert.Equal(LogicalTypeID.DOUBLE, prepared.Parameters.Single().ExpectedLogicalType.Id);
    }

    [Fact]
    public void Execute_PreparedStatement_WithFluentBind_ReturnsRows()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING
        });
        conn.UpsertNodeById("Person", "p1", new Dictionary<string, object>
        {
            ["id"] = "p1",
            ["name"] = "Ada"
        });
        conn.UpsertNodeById("Person", "p2", new Dictionary<string, object>
        {
            ["id"] = "p2",
            ["name"] = "Grace"
        });
        conn.Commit();

        using var prepared = conn
            .Prepare("MATCH (p:Person) WHERE p.name = $name RETURN p.id AS id, p.name AS name")
            .Bind("name", "Ada");

        var result = prepared.Execute();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        var row = result.GetNext().GetAsDictionary();
        Assert.Equal("p1", row["id"]);
        Assert.Equal("Ada", row["name"]);
        Assert.False(result.HasNext());
    }

    [Fact]
    public void Execute_PreparedStatement_CanReuseWithDifferentBindings()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING
        });
        conn.UpsertNodeById("Person", "p1", new Dictionary<string, object> { ["id"] = "p1", ["name"] = "Ada" });
        conn.UpsertNodeById("Person", "p2", new Dictionary<string, object> { ["id"] = "p2", ["name"] = "Grace" });
        conn.Commit();

        using var prepared = conn.Prepare("MATCH (p:Person) WHERE p.name = $name RETURN p.id AS id");

        var first = prepared.ClearBindings().Bind("name", "Ada").Execute();
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal("p1", first.GetNext().GetAsDictionary()["id"]);

        var second = prepared.ClearBindings().Bind("$name", "Grace").Execute();
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal("p2", second.GetNext().GetAsDictionary()["id"]);
    }

    [Fact]
    public void Execute_PreparedStatement_UsesIndexedParameterLookup()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING
        });
        conn.UpsertNodeById("Person", "p1", new Dictionary<string, object> { ["id"] = "p1", ["name"] = "Ada" });
        conn.UpsertNodeById("Person", "p2", new Dictionary<string, object> { ["id"] = "p2", ["name"] = "Grace" });
        conn.CreateIndex("Person", "name");
        conn.Commit();

        using var prepared = conn.Prepare("MATCH (p:Person) WHERE p.name = $name RETURN p.id AS id");

        var first = prepared.ClearBindings().Bind("name", "Ada").Execute();
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal("p1", first.GetNext().GetAsDictionary()["id"]);

        var second = prepared.ClearBindings().Bind("name", "Grace").Execute();
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal("p2", second.GetNext().GetAsDictionary()["id"]);
    }

    [Fact]
    public void Execute_PreparedStatement_UsesIndexedCastParameterLookup()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING
        });
        conn.UpsertNodeById("Person", "p1", new Dictionary<string, object> { ["id"] = "p1", ["name"] = "Ada" });
        conn.UpsertNodeById("Person", "p2", new Dictionary<string, object> { ["id"] = "p2", ["name"] = "Grace" });
        conn.CreateIndex("Person", "name");
        conn.Commit();

        using var prepared = conn.Prepare("MATCH (p:Person) WHERE p.name = CAST($name AS STRING) RETURN p.id AS id");

        var first = prepared.ClearBindings().Bind("name", "Ada").Execute();
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal("p1", first.GetNext().GetAsDictionary()["id"]);

        var second = prepared.ClearBindings().Bind("name", "Grace").Execute();
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal("p2", second.GetNext().GetAsDictionary()["id"]);
    }

    [Fact]
    public void Execute_PreparedStatement_UsesIndexedLookup_WhenConjunctionIncludesConstantTautology()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING
        });
        conn.UpsertNodeById("Person", "p1", new Dictionary<string, object> { ["id"] = "p1", ["name"] = "Ada" });
        conn.UpsertNodeById("Person", "p2", new Dictionary<string, object> { ["id"] = "p2", ["name"] = "Grace" });
        conn.CreateIndex("Person", "name");
        conn.Commit();

        using var prepared = conn.Prepare("MATCH (p:Person) WHERE p.name = CAST($name AS STRING) AND 1 = 1 RETURN p.id AS id");

        var first = prepared.ClearBindings().Bind("name", "Ada").Execute();
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal("p1", first.GetNext().GetAsDictionary()["id"]);

        var second = prepared.ClearBindings().Bind("name", "Grace").Execute();
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal("p2", second.GetNext().GetAsDictionary()["id"]);
    }

    [Fact]
    public void Execute_PreparedStatement_UsesIndexedLookup_WhenDisjunctionIncludesConstantFalse()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING
        });
        conn.UpsertNodeById("Person", "p1", new Dictionary<string, object> { ["id"] = "p1", ["name"] = "Ada" });
        conn.UpsertNodeById("Person", "p2", new Dictionary<string, object> { ["id"] = "p2", ["name"] = "Grace" });
        conn.CreateIndex("Person", "name");
        conn.Commit();

        using var prepared = conn.Prepare("MATCH (p:Person) WHERE p.name = CAST($name AS STRING) OR 1 = 0 RETURN p.id AS id");

        var first = prepared.ClearBindings().Bind("name", "Ada").Execute();
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.Equal("p1", first.GetNext().GetAsDictionary()["id"]);

        var second = prepared.ClearBindings().Bind("name", "Grace").Execute();
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal("p2", second.GetNext().GetAsDictionary()["id"]);
    }

    [Fact]
    public void Execute_PreparedStatement_CanBindDictionaryFluently()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"] = LogicalTypeID.STRING,
            ["name"] = LogicalTypeID.STRING
        });
        conn.UpsertNodeById("Person", "p1", new Dictionary<string, object> { ["id"] = "p1", ["name"] = "Ada" });
        conn.Commit();

        using var prepared = conn.Prepare("MATCH (p:Person) WHERE p.name = $name RETURN p.id AS id");

        var result = prepared.Bind(new Dictionary<string, object?> { ["name"] = "Ada" }).Execute();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("p1", result.GetNext().GetAsDictionary()["id"]);
    }
}
