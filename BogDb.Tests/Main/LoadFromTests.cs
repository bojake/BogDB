// BogDb.Tests/Main/LoadFromTests.cs
// Focused unit tests for G-013 Tier D: LOAD FROM CSV file reader.
//
// Engine contract (confirmed live from golden snapshot, March 27 2026):
//   - Syntax:    LOAD FROM 'file.csv' RETURN *
//   - Optional:  LOAD WITH HEADERS (col TYPE, ...) FROM 'file.csv' [WHERE ...] RETURN ...
//   - Semantics: reading clause (like MATCH), returns one row per CSV data row
//   - Column names: derived from CSV header row (id, name, age for persons.csv)
//   - Column types:
//       Without WITH HEADERS: engine infers types (confirmed: count(*) returns string)
//       With WITH HEADERS:    types are explicit and typed access works
//   - WHERE: supported as post-scan filter (same grammar position as MATCH ... WHERE)
//   - RETURN: required (LOAD FROM is a reading clause, not a standalone statement)
//
// Status: ALL TESTS ARE LIVE. LOAD FROM WITH HEADERS is fully implemented.
//   Confirmed from golden: n=4 for full scan, n=2 for filtered, round-trip works.
//
// Type notes confirmed from golden snapshot:
//   - count(*) returns its value as a string in row serialization ("4", "2")
//   - WITH HEADERS (id INT64, ...) → typed INT64 access via GetInt64()
//   - round-trip LOAD FROM (no headers) → count(*) returns string "4"
//
// AMBIGUITY NOTE (for engine implementer):
//   out_persons.csv headers are "p.id,p.name,p.age" (dotted column names from COPY TO).
//   Round-trip tests assert only on count(*) to avoid dotted-header expression ambiguity.
//   See tracker for the outstanding question on dotted-header column names.

using System;
using System.Collections.Generic;
using System.IO;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class LoadFromTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string TempCsvPath() =>
        Path.Combine(Path.GetTempPath(), $"bogdb_load_from_{Guid.NewGuid():N}.csv")
            .Replace('\\', '/');

    private static BogDatabase OpenDb() => BogDatabase.Open(":memory:");

    /// <summary>
    /// Writes a fresh persons.csv to a temp path.
    /// Header: id,name,age (4 rows, matches parity/query-golden/fixtures/persons.csv).
    /// </summary>
    private static string WritePersonsCsv()
    {
        var path = TempCsvPath();
        File.WriteAllText(path,
            "id,name,age\n" +
            "1,Alice,30\n" +
            "2,Bob,25\n" +
            "3,Charlie,35\n" +
            "4,Diana,28\n");
        return path;
    }

    // ── Basic LOAD FROM ───────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_DeclaredCsv_ReturnsAllRows()
    {
        // LOAD FROM is a reading clause — RETURN * emits one row per CSV data row.
        var csv = WritePersonsCsv();
        try
        {
            using var db   = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query(
                $"LOAD FROM '{csv}' RETURN *");

            Assert.True(result.IsSuccess, $"LOAD FROM failed: {result.ErrorMessage}");
            Assert.Equal(4UL, result.GetNumTuples());
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void LoadFrom_WithHeaders_ReturnsCorrectRowCount()
    {
        // WITH HEADERS pins column types; engine must return 4 rows.
        var csv = WritePersonsCsv();
        try
        {
            using var db   = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query(
                $"LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{csv}' RETURN *");

            Assert.True(result.IsSuccess, $"LOAD FROM (typed) failed: {result.ErrorMessage}");
            Assert.Equal(4UL, result.GetNumTuples());
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void LoadFrom_WithHeaders_ColumnNamesMatchCsvHeader()
    {
        // Column names in the result should match the CSV header (id, name, age).
        var csv = WritePersonsCsv();
        try
        {
            using var db   = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query(
                $"LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{csv}' RETURN id, name, age ORDER BY id");

            Assert.True(result.IsSuccess, $"LOAD FROM column names failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());
            var row = result.GetNext();
            Assert.Equal(1L,      row.GetInt64(0));
            Assert.Equal("Alice", row.GetString(1));
            Assert.Equal(30L,     row.GetInt64(2));
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void LoadFrom_LiteralWindowsStyleCsvPath_ReturnsAllRows_ThroughPlannerPath()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"bogdb_load_from_{Guid.NewGuid():N}.csv");
        File.WriteAllText(csv,
            "id,name,age\n" +
            "1,Alice,30\n" +
            "2,Bob,25\n");

        try
        {
            using var db = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query($"LOAD FROM '{csv}' RETURN *");

            Assert.True(result.IsSuccess, $"Literal CSV LOAD FROM failed: {result.ErrorMessage}");
            Assert.Equal(2UL, result.GetNumTuples());
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void LoadFrom_LiteralWindowsStyleCsvPath_WithHeaders_ReturnsTypedRows()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"bogdb_load_from_{Guid.NewGuid():N}.csv");
        File.WriteAllText(csv,
            "id,name,age\n" +
            "1,Alice,30\n" +
            "2,Bob,25\n");

        try
        {
            using var db = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query(
                $"LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{csv}' RETURN id, name, age ORDER BY id");

            Assert.True(result.IsSuccess, $"Typed literal CSV LOAD FROM failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());
            var row = result.GetNext();
            Assert.Equal(1L, row.GetInt64(0));
            Assert.Equal("Alice", row.GetString(1));
            Assert.Equal(30L, row.GetInt64(2));
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void LoadFrom_LiteralWindowsStyleCsvPath_CountProjection_Works()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"bogdb_load_from_{Guid.NewGuid():N}.csv");
        File.WriteAllText(csv,
            "id,name,age\n" +
            "1,Alice,30\n" +
            "2,Bob,25\n");

        try
        {
            using var db = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query($"LOAD FROM '{csv}' RETURN count(*) AS n");

            Assert.True(result.IsSuccess, $"Count literal CSV LOAD FROM failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());
            Assert.Equal(2L, result.GetNext().GetInt64(0));
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void LoadFrom_ParameterizedCsv_ReturnsAllRows_ThroughPlannerPath()
    {
        var csv = WritePersonsCsv();
        try
        {
            using var db = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query(
                "LOAD FROM $path RETURN *",
                new Dictionary<string, object?> { ["path"] = csv });

            Assert.True(result.IsSuccess, $"Parameterized LOAD FROM failed: {result.ErrorMessage}");
            Assert.Equal(4UL, result.GetNumTuples());
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void LoadFrom_ParameterizedCsvWithHeaders_ReturnsTypedRows_ThroughPlannerPath()
    {
        var csv = WritePersonsCsv();
        try
        {
            using var db = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query(
                "LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM $path RETURN id, name, age ORDER BY id",
                new Dictionary<string, object?> { ["path"] = csv });

            Assert.True(result.IsSuccess, $"Parameterized typed LOAD FROM failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());
            var row = result.GetNext();
            Assert.Equal(1L, row.GetInt64(0));
            Assert.Equal("Alice", row.GetString(1));
            Assert.Equal(30L, row.GetInt64(2));
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void LoadFrom_WithHeaders_AllRowValues()
    {
        // Verify all 4 rows and their values in id order.
        var csv = WritePersonsCsv();
        try
        {
            using var db   = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query(
                $"LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{csv}' RETURN id, name, age ORDER BY id");

            Assert.True(result.IsSuccess, $"LOAD FROM all rows failed: {result.ErrorMessage}");

            var expected = new (long id, string name, long age)[]
            {
                (1, "Alice",   30),
                (2, "Bob",     25),
                (3, "Charlie", 35),
                (4, "Diana",   28),
            };
            foreach (var (id, name, age) in expected)
            {
                Assert.True(result.HasNext(), $"Expected row for id={id} but result is exhausted.");
                var row = result.GetNext();
                Assert.Equal(id,   row.GetInt64(0));
                Assert.Equal(name, row.GetString(1));
                Assert.Equal(age,  row.GetInt64(2));
            }
            Assert.False(result.HasNext(), "Result has more rows than expected.");
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    // ── WHERE filter ──────────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_WithWhereFilter_ReturnsSubset()
    {
        // WHERE clause filters post-scan: age > 28 → Alice (30), Charlie (35).
        var csv = WritePersonsCsv();
        try
        {
            using var db   = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query(
                $"LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{csv}' " +
                $"WHERE age > 28 " +
                $"RETURN id, name ORDER BY id");

            Assert.True(result.IsSuccess, $"LOAD FROM WHERE failed: {result.ErrorMessage}");
            Assert.Equal(2UL, result.GetNumTuples());

            Assert.True(result.HasNext());
            var r1 = result.GetNext();
            Assert.Equal(1L,      r1.GetInt64(0));
            Assert.Equal("Alice", r1.GetString(1));

            Assert.True(result.HasNext());
            var r2 = result.GetNext();
            Assert.Equal(3L,        r2.GetInt64(0));
            Assert.Equal("Charlie", r2.GetString(1));
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    [Fact]
    public void LoadFrom_WithWhereExactMatch_ReturnsSingleRow()
    {
        var csv = WritePersonsCsv();
        try
        {
            using var db   = OpenDb();
            using var conn = new BogConnection(db);

            var result = conn.Query(
                $"LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{csv}' " +
                $"WHERE id = 2 " +
                $"RETURN id, name");

            Assert.True(result.IsSuccess, $"LOAD FROM WHERE exact failed: {result.ErrorMessage}");
            Assert.Equal(1UL, result.GetNumTuples());

            Assert.True(result.HasNext());
            var row = result.GetNext();
            Assert.Equal(2L,    row.GetInt64(0));
            Assert.Equal("Bob", row.GetString(1));
        }
        finally { if (File.Exists(csv)) File.Delete(csv); }
    }

    // ── COPY TO → LOAD FROM round-trip ───────────────────────────────────────
    // Confirmed live from golden: count(*) returns 4 for full export, 2 for filtered.
    // Round-trip uses count(*) only — avoids dotted-header column name ambiguity
    // ("p.id","p.name" from COPY TO output conflict with property access syntax).

    [Fact]
    public void LoadFrom_AfterCopyTo_RoundTrip_RowCount()
    {
        var outPath = TempCsvPath();
        try
        {
            using var db   = OpenDb();
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                { "id",   LogicalTypeID.INT64  },
                { "name", LogicalTypeID.STRING },
                { "age",  LogicalTypeID.INT64  },
            });
            conn.UpsertNode("Person", 1L, new Dictionary<string, object> { { "name", "Alice"   }, { "age", 30L } });
            conn.UpsertNode("Person", 2L, new Dictionary<string, object> { { "name", "Bob"     }, { "age", 25L } });
            conn.UpsertNode("Person", 3L, new Dictionary<string, object> { { "name", "Charlie" }, { "age", 35L } });
            conn.UpsertNode("Person", 4L, new Dictionary<string, object> { { "name", "Diana"   }, { "age", 28L } });
            conn.Commit();

            // Export
            var exportResult = conn.Query(
                $"COPY (MATCH (p:Person) RETURN p.id, p.name, p.age ORDER BY p.id) TO '{outPath}'");
            Assert.True(exportResult.IsSuccess, $"COPY TO failed: {exportResult.ErrorMessage}");
            Assert.True(File.Exists(outPath));

            // Read back — count(*) only (avoids dotted column name ambiguity)
            var loadResult = conn.Query($"LOAD FROM '{outPath}' RETURN count(*) AS n");
            Assert.True(loadResult.IsSuccess, $"LOAD FROM round-trip failed: {loadResult.ErrorMessage}");
            Assert.True(loadResult.HasNext());
            // count(*) serializes as string in untyped LOAD FROM; use GetString → parse
            var countRow = loadResult.GetNext();
            Assert.Equal("4", countRow.GetString(0));
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public void LoadFrom_AfterCopyTo_FilteredRoundTrip_RowCount()
    {
        var outPath = TempCsvPath();
        try
        {
            using var db   = OpenDb();
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                { "id",   LogicalTypeID.INT64  },
                { "name", LogicalTypeID.STRING },
                { "age",  LogicalTypeID.INT64  },
            });
            conn.UpsertNode("Person", 1L, new Dictionary<string, object> { { "name", "Alice"   }, { "age", 30L } });
            conn.UpsertNode("Person", 2L, new Dictionary<string, object> { { "name", "Bob"     }, { "age", 25L } });
            conn.UpsertNode("Person", 3L, new Dictionary<string, object> { { "name", "Charlie" }, { "age", 35L } });
            conn.UpsertNode("Person", 4L, new Dictionary<string, object> { { "name", "Diana"   }, { "age", 28L } });
            conn.Commit();

            conn.Query(
                $"COPY (MATCH (p:Person) WHERE p.age > 28 RETURN p.id, p.name ORDER BY p.id) TO '{outPath}'");
            Assert.True(File.Exists(outPath));

            var loadResult = conn.Query($"LOAD FROM '{outPath}' RETURN count(*) AS n");
            Assert.True(loadResult.IsSuccess, $"LOAD FROM filtered round-trip failed: {loadResult.ErrorMessage}");
            Assert.True(loadResult.HasNext());
            Assert.Equal("2", loadResult.GetNext().GetString(0));
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }
}
