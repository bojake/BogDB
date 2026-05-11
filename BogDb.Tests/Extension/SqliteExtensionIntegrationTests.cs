using System;
using System.IO;
using System.Linq;
using Xunit;
using BogDb.Core.Main;
using BogDb.Extensions.SQLite;
using Microsoft.Data.Sqlite;

namespace BogDb.Tests.Extension
{
    /// <summary>
    /// Integration tests for the SQLite extension.
    /// Uses in-memory or temp-file SQLite databases — no external files required.
    /// </summary>
    public class SqliteExtensionIntegrationTests : IDisposable
    {
        private readonly string _dbPath;

        public SqliteExtensionIntegrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"bogdb_test_{Guid.NewGuid():N}.sqlite");
            CreateTestDatabase(_dbPath);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static void CreateTestDatabase(string path)
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE artists (id INTEGER PRIMARY KEY, name TEXT, country TEXT);
                INSERT INTO artists VALUES (1, 'AC/DC', 'Australia');
                INSERT INTO artists VALUES (2, 'Miles Davis', 'USA');
                INSERT INTO artists VALUES (3, 'Beethoven', 'Germany');

                CREATE TABLE albums (id INTEGER PRIMARY KEY, title TEXT, artist_id INTEGER);
                INSERT INTO albums VALUES (1, 'Back in Black', 1);
                INSERT INTO albums VALUES (2, 'Kind of Blue', 2);";
            cmd.ExecuteNonQuery();
        }

        private BogDatabase CreateDbWithSqlite()
        {
            var db = BogDatabase.Open(":memory:");
            new SQLiteExtension().Load(db);
            return db;
        }

        // ── registration tests ────────────────────────────────────────────────────

        [Fact]
        public void LoadExtension_RegistersScanSqliteFunction()
        {
            var db = BogDatabase.Open(":memory:");
            Assert.False(db.FunctionRegistry.Contains("scan_sqlite"),
                "Function should not be registered before Load()");

            new SQLiteExtension().Load(db);

            Assert.True(db.FunctionRegistry.Contains("scan_sqlite"),
                "scan_sqlite should be registered after SQLiteExtension.Load()");
        }

        // ── LOAD FROM query tests ─────────────────────────────────────────────────

        [Fact]
        public void LoadFrom_SQLite_ExplicitTable_ReturnsThreeRows()
        {
            using var db = CreateDbWithSqlite();
            using var conn = new BogConnection(db);

            var result = conn.Query($"LOAD FROM '{_dbPath}|artists' RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var count = 0;
            while (result.HasNext()) { result.GetNext(); count++; }
            Assert.Equal(3, count);
        }

        [Fact]
        public void LoadFrom_SQLite_ExplicitTable_FirstRowHasNameKey()
        {
            using var db = CreateDbWithSqlite();
            using var conn = new BogConnection(db);

            var result = conn.Query($"LOAD FROM '{_dbPath}|artists' RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");
            Assert.True(result.HasNext());

            var row = result.GetNext().GetAsDictionary();
            Assert.True(row.ContainsKey("name"), $"Expected 'name' key; got: {string.Join(", ", row.Keys)}");
            Assert.Equal("AC/DC", row["name"]?.ToString());
        }

        [Fact]
        public void LoadFrom_SQLite_AutoTable_ReturnFirstTableRows()
        {
            // Without |tablename — should auto-pick the first table ("albums" or "artists")
            using var db = CreateDbWithSqlite();
            using var conn = new BogConnection(db);

            var result = conn.Query($"LOAD FROM '{_dbPath}' RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var count = 0;
            while (result.HasNext()) { result.GetNext(); count++; }
            Assert.True(count > 0, "Expected at least one row from auto-picked table");
        }

        [Fact]
        public void LoadFrom_SQLite_SecondTable_ReturnsTwoRows()
        {
            using var db = CreateDbWithSqlite();
            using var conn = new BogConnection(db);

            var result = conn.Query($"LOAD FROM '{_dbPath}|albums' RETURN *");

            Assert.True(result.IsSuccess, $"Query failed: {result.ErrorMessage}");

            var count = 0;
            while (result.HasNext()) { result.GetNext(); count++; }
            Assert.Equal(2, count);
        }

        [Fact]
        public void LoadFrom_SQLite_MissingFile_ReturnsError()
        {
            using var db = CreateDbWithSqlite();
            using var conn = new BogConnection(db);

            var result = conn.Query("LOAD FROM '/nonexistent/ghost.sqlite' RETURN *");

            Assert.False(result.IsSuccess, "Expected failure for missing SQLite file");
        }

        [Fact]
        public void LoadFrom_SQLite_WithoutExtension_ReturnsRegistryError()
        {
            using var db = BogDatabase.Open(":memory:");  // NO extension loaded
            using var conn = new BogConnection(db);

            var result = conn.Query($"LOAD FROM '{_dbPath}|artists' RETURN *");

            Assert.False(result.IsSuccess, "Expected failure when scan_sqlite is not registered");
            Assert.Contains("scan_sqlite", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        // ── Direct function invocation test ───────────────────────────────────────

        [Fact]
        public void ScanSqlite_DirectInvoke_YieldsRows()
        {
            var fn = new ScanSqliteTableFunction();
            var rows = fn.Invoke(new[] { (object?)$"{_dbPath}|artists" }).ToList();

            Assert.Equal(3, rows.Count);
            Assert.True(rows[0].ContainsKey("name"));
            Assert.Equal("AC/DC", rows[0]["name"]?.ToString());
        }
    }
}
