using System.Text.Json.Nodes;
using System.Text.Json;
using Xunit;
using BogDb.Core.Common;
using BogDb.Core.Main;
using System.Collections.Generic;

namespace BogDb.Tests.Processor
{
    /// <summary>
    /// Tests for TypeCoercionHelper.Normalize() and its integration into
    /// ExpressionExecutionHelper.CompareValues() and PhysicalProjection output.
    ///
    /// C++ parity: equivalent to type promotion semantics in bogdb-cpp expression evaluation.
    /// </summary>
    public class TypeCoercionTests
    {
        // ── Normalize() unit tests ────────────────────────────────────────────

        [Fact]
        public void Normalize_JsonNode_Integer_ReturnsLong()
        {
            var node = JsonNode.Parse("30")!;
            var result = TypeCoercionHelper.Normalize(node);
            Assert.IsType<long>(result);
            Assert.Equal(30L, (long)result);
        }

        [Fact]
        public void Normalize_JsonNode_Double_ReturnsDouble()
        {
            var node = JsonNode.Parse("3.14")!;
            var result = TypeCoercionHelper.Normalize(node);
            Assert.IsType<double>(result);
            Assert.Equal(3.14, (double)result, precision: 5);
        }

        [Fact]
        public void Normalize_JsonNode_String_ReturnsString()
        {
            var node = JsonNode.Parse("\"Alice\"")!;
            var result = TypeCoercionHelper.Normalize(node);
            Assert.IsType<string>(result);
            Assert.Equal("Alice", (string)result);
        }

        [Fact]
        public void Normalize_JsonNode_Bool_ReturnsBool()
        {
            var node = JsonNode.Parse("true")!;
            var result = TypeCoercionHelper.Normalize(node);
            Assert.IsType<bool>(result);
            Assert.True((bool)result);
        }

        [Fact]
        public void Normalize_JsonNode_Null_ReturnsNull()
        {
            var result = TypeCoercionHelper.Normalize(null);
            Assert.Null(result);
        }

        [Fact]
        public void Normalize_Int32_ReturnsLong()
        {
            var result = TypeCoercionHelper.Normalize((int)30);
            Assert.IsType<long>(result);
            Assert.Equal(30L, (long)result);
        }

        [Fact]
        public void Normalize_Float_ReturnsDouble()
        {
            var result = TypeCoercionHelper.Normalize(3.14f);
            Assert.IsType<double>(result);
        }

        [Fact]
        public void Normalize_Long_Passthrough()
        {
            var result = TypeCoercionHelper.Normalize(42L);
            Assert.IsType<long>(result);
            Assert.Equal(42L, (long)result);
        }

        [Fact]
        public void Normalize_String_Passthrough()
        {
            var result = TypeCoercionHelper.Normalize("hello");
            Assert.Equal("hello", result);
        }

        // ── TypeCoercionHelper typed helpers ──────────────────────────────────

        [Fact]
        public void ToInt64_JsonNodeInteger_ReturnsLong()
        {
            var node = JsonNode.Parse("99")!;
            Assert.Equal(99L, TypeCoercionHelper.ToInt64(node));
        }

        [Fact]
        public void ToBogDbString_JsonNodeString_ReturnsString()
        {
            var node = JsonNode.Parse("\"Bob\"")!;
            Assert.Equal("Bob", TypeCoercionHelper.ToBogDbString(node));
        }

        [Fact]
        public void ToBool_JsonNodeBoolFalse_ReturnsFalse()
        {
            var node = JsonNode.Parse("false")!;
            Assert.False(TypeCoercionHelper.ToBool(node));
        }

        [Fact]
        public void ToBogDbString_ListContainingIntervals_FormatsRecursively()
        {
            var value = new List<object?>
            {
                BogDbInterval.FromDays(1),
                BogDbInterval.FromHours(25),
                "tag"
            };

            Assert.Equal("[P1D,P1DT1H,\"tag\"]", TypeCoercionHelper.ToBogDbString(value));
        }

        [Fact]
        public void ToBogDbString_MapContainingIntervals_FormatsRecursively()
        {
            var value = new List<KeyValuePair<object?, object?>>
            {
                new("day", BogDbInterval.FromDays(1)),
                new("hour", BogDbInterval.FromHours(25))
            };

            Assert.Equal("{\"day\":P1D,\"hour\":P1DT1H}", TypeCoercionHelper.ToBogDbString(value));
        }

        // ── CompareValues via EvaluatePredicate ───────────────────────────────

        [Fact]
        public void CompareValues_JsonNodeInt_EqualLong_ReturnsTrue()
        {
            // Simulate: property bag stores a JsonNode int (scan_json_array output)
            // WHERE n.age = 30 must coerce JsonNode(30) → long before comparison
            var db = BogDatabase.Open(":memory:");
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
                { { "age", BogDb.Core.Common.LogicalTypeID.INT64 } });
            conn.Commit();

            // Inject a JsonNode value directly into the property bag
            db.NodeTables["Person"].Data[1L] = new Dictionary<string, object>
                { ["age"] = JsonNode.Parse("30")! };

            var result = conn.Query("MATCH (n:Person) WHERE n.age = 30 RETURN n.age");
            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.True(result.HasNext(), "Expected matching row — JsonNode coercion in CompareValues failed");
        }

        [Fact]
        public void CompareValues_Int32Property_LongLiteral_GreaterThan()
        {
            // int property value vs long literal in WHERE n.age > 25
            var db = BogDatabase.Open(":memory:");
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
                { { "age", BogDb.Core.Common.LogicalTypeID.INT64 } });
            conn.Commit();

            // Inject int (narrow type) directly — simulating pre-coercion storage
            db.NodeTables["Person"].Data[1L] = new Dictionary<string, object>
                { ["age"] = (int)30 };

            var result = conn.Query("MATCH (n:Person) WHERE n.age > 25 RETURN n.age");
            Assert.True(result.IsSuccess);
            Assert.True(result.HasNext(), "Expected row where int(30) > long(25) — coercion must normalise int→long");
            Assert.Equal(30L, result.GetNext().GetInt64(0));
        }

        [Fact]
        public void CompareValues_UInt32Property_LongLiteral_GreaterThan()
        {
            var db = BogDatabase.Open(":memory:");
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Event", new Dictionary<string, BogDb.Core.Common.LogicalTypeID>
                { { "u32", BogDb.Core.Common.LogicalTypeID.UINT32 } });
            conn.Commit();

            db.NodeTables["Event"].Data[1L] = new Dictionary<string, object>
                { ["u32"] = (uint)30 };

            var result = conn.Query("MATCH (e:Event) WHERE e.u32 > 25 RETURN e.u32");
            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.True(result.HasNext(), "Expected row where uint(30) > long(25) - mixed numeric comparison should bind and execute");
            Assert.Equal("30", result.GetNext().GetValue(0)?.ToString());
        }

        [Fact]
        public void BogRow_GetInt64_ReturnsLongForJsonNodeValue()
        {
            // GetInt64 now uses ToInt64() which coerces JsonNode
            var node = JsonNode.Parse("42")!;
            var row = new BogDb.Core.Main.QueryResult.BogRow(new object[] { node });
            Assert.Equal(42L, row.GetInt64(0));
        }

        [Fact]
        public void BogRow_GetString_ReturnsStringForJsonNodeValue()
        {
            var node = JsonNode.Parse("\"Carol\"")!;
            var row = new BogDb.Core.Main.QueryResult.BogRow(new object[] { node });
            Assert.Equal("Carol", row.GetString(0));
        }
    }
}
