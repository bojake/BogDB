using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using BogDb.Core.Main;
using BogDb.Extensions.Json;
using BogDb.Extensions.SQLite;

namespace BogDb.Tests.Extension
{
    /// <summary>
    /// Integration tests for the CALL fn() execution path.
    ///
    /// These test that `CALL funcName('arg') RETURN *` goes through the full
    /// Transformer → Binder → Planner → PlanMapper → PhysicalTableFunctionCall → FunctionRegistry
    /// pipeline, rather than the pre-parse LOAD FROM intercept.
    ///
    /// Syntax note: table scan functions use `CALL fn(args) RETURN *` (no YIELD clause).
    /// YIELD is only needed for GDS algorithms where output columns are explicitly named.
    ///
    /// This is the C# parallel to the C++ `CALL` table function tests in extension/*.test.
    /// </summary>
    public class CallTableFunctionIntegrationTests
    {
        // ── helpers ──────────────────────────────────────────────────────────────

        private static string ResolvePath(string relative)
        {
            var normalised = relative.Replace('\\', System.IO.Path.DirectorySeparatorChar);
            var segments = normalised.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var tail = string.Join(System.IO.Path.DirectorySeparatorChar.ToString(),
                segments.SkipWhile(s => s == ".."));

            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = System.IO.Path.Combine(dir.FullName, tail);
                if (System.IO.File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return relative;
        }

        private static string ToCypherStringLiteral(string value)
        {
            // Cypher string literals require backslashes and quotes to be escaped.
            var escaped = value.Replace("\\", "\\\\").Replace("'", "\\'");
            return $"'{escaped}'";
        }

        // ── scan_json_array via CALL ──────────────────────────────────────────────

        [Fact]
        public void Call_ScanJsonArray_VMovies_ReturnsThreeRows()
        {
            var db = BogDatabase.Open(":memory:");
            new JsonExtension().Load(db);
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/tinysnb_json/vMovies.json")
                .Replace('\\', '/');
            var pathLiteral = ToCypherStringLiteral(path);
            var result = conn.Query($"CALL scan_json_array({pathLiteral}) RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var count = 0;
            while (result.HasNext()) { result.GetNext(); count++; }
            Assert.Equal(3, count);
        }

        [Fact]
        public void Call_ScanJsonArray_VMovies_FirstRowHasNameKey()
        {
            var db = BogDatabase.Open(":memory:");
            new JsonExtension().Load(db);
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/tinysnb_json/vMovies.json")
                .Replace('\\', '/');
            var pathLiteral = ToCypherStringLiteral(path);
            var result = conn.Query($"CALL scan_json_array({pathLiteral}) RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());

            var row = result.GetNext().GetAsDictionary();
            Assert.True(row.ContainsKey("name"), $"Expected key 'name', got: {string.Join(", ", row.Keys)}");
        }

        [Fact]
        public void Call_ScanJsonArray_ArrayTest_ReturnsTwoRows()
        {
            var db = BogDatabase.Open(":memory:");
            new JsonExtension().Load(db);
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/json-misc/array-test.json")
                .Replace('\\', '/');
            var pathLiteral = ToCypherStringLiteral(path);
            var result = conn.Query($"CALL scan_json_array({pathLiteral}) RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var count = 0;
            while (result.HasNext()) { result.GetNext(); count++; }
            Assert.Equal(2, count);
        }

        [Fact]
        public void Call_ScanJsonArray_PrimTest_PreservesProjectedColumns()
        {
            var db = BogDatabase.Open(":memory:");
            new JsonExtension().Load(db);
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/json-misc/prim-test.json")
                .Replace('\\', '/');
            var pathLiteral = ToCypherStringLiteral(path);
            var result = conn.Query($"CALL scan_json_array({pathLiteral}) RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.Equal(new[] { "a", "b", "c" }, result.ColumnNames);
            Assert.True(result.HasNext());

            var row = result.GetNext().GetAsDictionary();
            Assert.Equal(1L, row["a"]);
            Assert.Equal(true, row["b"]);
            Assert.Equal(5.0, (double)row["c"], 5);
        }

        [Fact]
        public void Call_ScanJsonArray_NewlineDelimitedJson_ReturnsAllRows()
        {
            var db = BogDatabase.Open(":memory:");
            new JsonExtension().Load(db);
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/json-misc/newline-delimited.json")
                .Replace('\\', '/');
            var pathLiteral = ToCypherStringLiteral(path);
            var result = conn.Query($"CALL scan_json_array({pathLiteral}) RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.Equal(new[] { "ID", "creationDate", "locationIP", "browserUsed", "content", "length" }, result.ColumnNames);

            var count = 0;
            while (result.HasNext())
            {
                result.GetNext();
                count++;
            }

            Assert.Equal(9, count);
        }

        [Fact]
        public void Call_ScanJsonArray_AcceptsParameterArgument()
        {
            var db = BogDatabase.Open(":memory:");
            new JsonExtension().Load(db);
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/tinysnb_json/vMovies.json")
                .Replace('\\', '/');

            var result = conn.Query(
                "CALL scan_json_array($path) RETURN *",
                new Dictionary<string, object?> { ["path"] = path });

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var count = 0;
            while (result.HasNext()) { result.GetNext(); count++; }
            Assert.Equal(3, count);
        }

        [Fact]
        public void Call_UnregisteredFunction_DoesNotCrash()
        {
            // An unregistered function should not crash — the query engine returns
            // an error result gracefully, not an unhandled exception.
            var db = BogDatabase.Open(":memory:");
            using var conn = new BogConnection(db);

            var exception = Record.Exception(() =>
                conn.Query("CALL nonexistent_fn('whatever') RETURN *"));

            Assert.Null(exception);
        }

        // ── CALL vs LOAD FROM equivalence ────────────────────────────────────────

        [Fact]
        public void Call_AndLoadFrom_VMovies_ReturnSameRowCount()
        {
            // Both CALL and LOAD FROM should produce the same number of rows
            var db = BogDatabase.Open(":memory:");
            new JsonExtension().Load(db);
            using var conn = new BogConnection(db);

            var path = ResolvePath("../../../../dataset/tinysnb_json/vMovies.json")
                .Replace('\\', '/');

            var pathLiteral = ToCypherStringLiteral(path);
            var loadFromResult = conn.Query($"LOAD FROM {pathLiteral} RETURN *");
            var callResult    = conn.Query($"CALL scan_json_array({pathLiteral}) RETURN *");

            Assert.True(loadFromResult.IsSuccess, $"LOAD FROM failed: {loadFromResult.ErrorMessage}");
            Assert.True(callResult.IsSuccess,     $"CALL failed: {callResult.ErrorMessage}");

            var loadCount = 0;
            while (loadFromResult.HasNext()) { loadFromResult.GetNext(); loadCount++; }

            var callCount = 0;
            while (callResult.HasNext()) { callResult.GetNext(); callCount++; }

            Assert.Equal(loadCount, callCount);
        }
    }
}
