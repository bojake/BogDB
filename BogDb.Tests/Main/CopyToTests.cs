// BogDb.Tests/Main/CopyToTests.cs
// Focused unit tests for G-013 Tier C: COPY TO CSV export.
//
// Engine contract (confirmed from CopyTo.cs implementation):
//   - Syntax:   COPY (MATCH ... RETURN ...) TO '/path/out.csv'
//   - Output:   UTF-8 CSV with a header row (column names from RETURN projection,
//               e.g. "p.id,p.name" — the RETURN item column name, not just the alias)
//   - Values:   comma-separated; integers/numbers via InvariantCulture
//   - Escaping: fields containing comma, double-quote, or newline are RFC 4180 quoted
//   - Nulls:    serialized as empty string
//   - Return:   COPY TO itself returns IsSuccess=true with 0 result tuples (sink op)
//
// Status: tests are written against confirmed landed Tier C implementation.
// all 10 tests should be green once Tier C dispatch is fully wired end-to-end.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BogDb.Core.Common;
using BogDb.Core.Common.FileSystem;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class CopyToTests
{
    private sealed class InMemoryWritableFileSystem : VirtualFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public string? ReadAllText(string path)
            => _files.TryGetValue(path, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

        public override BogDb.Core.Common.FileSystem.FileInfo OpenFile(string path, FileFlags flags)
        {
            if (flags.HasFlag(FileFlags.Read) && !flags.HasFlag(FileFlags.Write))
            {
                if (!_files.TryGetValue(path, out var bytes))
                    throw new FileNotFoundException($"File '{path}' not found.", path);
                return new InMemoryFileInfo(path, bytes, updated => _files[path] = updated);
            }

            var initial = _files.TryGetValue(path, out var existing) ? existing : Array.Empty<byte>();
            return new InMemoryFileInfo(path, initial, updated => _files[path] = updated);
        }

        public override void CreateDirectory(string path)
        {
            _directories.Add(path);
        }

        public override void RemoveFileIfExists(string path)
        {
            _files.Remove(path);
        }

        public override bool FileExists(string path)
            => _files.ContainsKey(path);

        public override IReadOnlyList<string> GetPathsInDirectory(string path)
        {
            var prefix = path.EndsWith("/") ? path : path + "/";
            var matches = new List<string>();
            foreach (var filePath in _files.Keys)
            {
                if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    matches.Add(filePath);
            }
            return matches;
        }

        public override string JoinPath(string baseDir, string name)
            => baseDir.EndsWith("/") ? baseDir + name : baseDir + "/" + name;

        private sealed class InMemoryFileInfo : BogDb.Core.Common.FileSystem.FileInfo
        {
            private byte[] _content;
            private readonly Action<byte[]> _onDispose;

            public InMemoryFileInfo(string path, byte[] content, Action<byte[]> onDispose)
                : base(path)
            {
                _content = content.ToArray();
                _onDispose = onDispose;
                FileSize = _content.Length;
            }

            public override void Read(Span<byte> buffer, long offset)
            {
                _content.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
            }

            public override void Write(ReadOnlySpan<byte> buffer, long offset)
            {
                var requiredLength = checked((int)(offset + buffer.Length));
                if (requiredLength > _content.Length)
                    Array.Resize(ref _content, requiredLength);
                buffer.CopyTo(_content.AsSpan((int)offset, buffer.Length));
                FileSize = _content.Length;
            }

            public override void Sync()
            {
            }

            public override void Truncate(long size)
            {
                Array.Resize(ref _content, checked((int)size));
                FileSize = size;
            }

            public override long GetFileSize()
                => FileSize;

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _onDispose(_content);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string TempCsvPath() =>
        Path.Combine(Path.GetTempPath(), $"bogdb_copy_to_{Guid.NewGuid():N}.csv")
            .Replace('\\', '/');

    private static BogDatabase OpenDb() => BogDatabase.Open(":memory:");

    /// <summary>Sets up an in-memory Person graph with 4 nodes.</summary>
    private static BogConnection SetupPersonGraph(BogDatabase db)
    {
        var conn = new BogConnection(db);
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
        return conn;
    }

    // ── Original test from engine worker (kept intact) ────────────────────────

    [Fact]
    public void CopyTo_NodeQuery_WritesCsvWithHeaderAndRows()
    {
        var exportPath = Path.Combine(Path.GetTempPath(), $"bogdb-copy-to-{Guid.NewGuid():N}.csv").Replace("\\", "/");

        try
        {
            using var db = BogDatabase.Open(":memory:");
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                { "id", LogicalTypeID.INT64 },
                { "name", LogicalTypeID.STRING },
            });
            conn.UpsertNode("Person", 1L, new Dictionary<string, object> { { "name", "Alice" } });
            conn.UpsertNode("Person", 2L, new Dictionary<string, object> { { "name", "Bob" } });
            conn.Commit();

            var result = conn.Query($"COPY (MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id) TO '{exportPath}'");
            Assert.True(result.IsSuccess, $"COPY TO failed: {result.ErrorMessage}");

            Assert.True(File.Exists(exportPath), "Expected COPY TO to create an output file.");
            var lines = File.ReadAllLines(exportPath);
            Assert.Equal(3, lines.Length);
            Assert.Equal("p.id,p.name", lines[0]);
            Assert.Equal("1,Alice", lines[1]);
            Assert.Equal("2,Bob", lines[2]);
        }
        finally
        {
            if (File.Exists(exportPath))
                File.Delete(exportPath);
        }
    }

    // ── Extended Tier C coverage ──────────────────────────────────────────────

    [Fact]
    public void CopyTo_StatementReturnsNoResultTuples()
    {
        // COPY TO is a sink — caller gets IsSuccess=true but 0 result rows.
        var outPath = TempCsvPath();
        try
        {
            using var db   = OpenDb();
            using var conn = SetupPersonGraph(db);

            var result = conn.Query(
                $"COPY (MATCH (p:Person) RETURN p.id) TO '{outPath}'");

            Assert.True(result.IsSuccess, $"COPY TO failed: {result.ErrorMessage}");
            Assert.Equal(0UL, result.GetNumTuples());
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public void CopyTo_RowCountMatchesSourceTable()
    {
        var outPath = TempCsvPath();
        try
        {
            using var db   = OpenDb();
            using var conn = SetupPersonGraph(db);

            conn.Query($"COPY (MATCH (p:Person) RETURN p.id, p.name, p.age ORDER BY p.id) TO '{outPath}'");

            var lines = File.ReadAllLines(outPath);
            Assert.Equal(5, lines.Length); // header + 4 data rows
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public void CopyTo_AllThreeColumns_CellValuesAreCorrect()
    {
        var outPath = TempCsvPath();
        try
        {
            using var db   = OpenDb();
            using var conn = SetupPersonGraph(db);

            conn.Query($"COPY (MATCH (p:Person) RETURN p.id, p.name, p.age ORDER BY p.id) TO '{outPath}'");

            var lines = File.ReadAllLines(outPath);
            Assert.Equal("p.id,p.name,p.age", lines[0]);
            Assert.Equal("1,Alice,30",         lines[1]);
            Assert.Equal("2,Bob,25",           lines[2]);
            Assert.Equal("3,Charlie,35",       lines[3]);
            Assert.Equal("4,Diana,28",         lines[4]);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public void CopyTo_FilteredQuery_ExportsSubset()
    {
        var outPath = TempCsvPath();
        try
        {
            using var db   = OpenDb();
            using var conn = SetupPersonGraph(db);

            conn.Query(
                $"COPY (MATCH (p:Person) WHERE p.age > 28 RETURN p.id, p.name ORDER BY p.id) TO '{outPath}'");

            var lines = File.ReadAllLines(outPath);
            Assert.Equal(3, lines.Length);           // header + Alice + Charlie
            Assert.Equal("p.id,p.name", lines[0]);
            Assert.Equal("1,Alice",     lines[1]);
            Assert.Equal("3,Charlie",   lines[2]);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public void CopyTo_EmptyResultSet_WritesHeaderOnly()
    {
        var outPath = TempCsvPath();
        try
        {
            using var db   = OpenDb();
            using var conn = SetupPersonGraph(db);

            conn.Query(
                $"COPY (MATCH (p:Person) WHERE p.age > 999 RETURN p.id, p.name) TO '{outPath}'");

            Assert.True(File.Exists(outPath));
            var lines = File.ReadAllLines(outPath);
            Assert.Single(lines);
            Assert.Equal("p.id,p.name", lines[0]);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public void CopyTo_StringWithComma_IsQuotedPerRfc4180()
    {
        var outPath = TempCsvPath();
        try
        {
            using var db   = OpenDb();
            using var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Item", new Dictionary<string, LogicalTypeID>
            {
                { "id",    LogicalTypeID.INT64  },
                { "label", LogicalTypeID.STRING },
            });
            conn.UpsertNode("Item", 1L, new Dictionary<string, object> { { "label", "foo,bar" } });
            conn.Commit();

            var result = conn.Query(
                $"COPY (MATCH (i:Item) RETURN i.id, i.label ORDER BY i.id) TO '{outPath}'");

            Assert.True(result.IsSuccess, $"COPY TO failed: {result.ErrorMessage}");
            var lines = File.ReadAllLines(outPath);
            Assert.Equal("i.id,i.label",   lines[0]);
            Assert.Equal("1,\"foo,bar\"",  lines[1]);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public void CopyTo_OutputDirectoryCreatedIfAbsent()
    {
        // CopyTo.cs calls Directory.CreateDirectory — verify nested dirs are made.
        var guidRoot = Path.Combine(Path.GetTempPath(), $"bogdb_test_{Guid.NewGuid():N}");
        var subDir   = Path.Combine(guidRoot, "deep", "sub");
        var outPath  = Path.Combine(subDir, "out.csv").Replace('\\', '/');
        try
        {
            using var db   = OpenDb();
            using var conn = SetupPersonGraph(db);

            var result = conn.Query(
                $"COPY (MATCH (p:Person) RETURN p.id ORDER BY p.id) TO '{outPath}'");

            Assert.True(result.IsSuccess, $"COPY TO failed: {result.ErrorMessage}");
            Assert.True(File.Exists(outPath), "File not created inside nested output directory.");
        }
        finally
        {
            // Delete only the guid-named temp root we created, not /tmp itself.
            if (Directory.Exists(guidRoot)) Directory.Delete(guidRoot, recursive: true);
        }
    }

    [Fact]
    public void CopyTo_AggregationQuery_ExportsAggregatedResults()
    {
        // COPY TO wraps any regular query, including aggregations.
        var outPath = TempCsvPath();
        try
        {
            using var db   = OpenDb();
            using var conn = SetupPersonGraph(db);

            conn.Query(
                $"COPY (MATCH (p:Person) RETURN count(p) AS total) TO '{outPath}'");

            var lines = File.ReadAllLines(outPath);
            Assert.Equal(2, lines.Length);  // header + 1 aggregate row
            Assert.Equal("total", lines[0]);
            Assert.Equal("4",     lines[1]);
        }
        finally { if (File.Exists(outPath)) File.Delete(outPath); }
    }

    [Fact]
    public void CopyTo_UsesRegisteredWritableFileSystem()
    {
        using var db = OpenDb();
        using var conn = SetupPersonGraph(db);
        var fileSystem = new InMemoryWritableFileSystem();
        db.RegisterFileSystem("mem", fileSystem);

        var result = conn.Query(
            "COPY (MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id) TO 'mem://exports/people.csv'");

        Assert.True(result.IsSuccess, $"COPY TO failed: {result.ErrorMessage}");
        var content = fileSystem.ReadAllText("mem://exports/people.csv");
        Assert.NotNull(content);
        var normalized = content!.Replace("\r\n", "\n");
        Assert.Equal("p.id,p.name\n1,Alice\n2,Bob\n3,Charlie\n4,Diana\n", normalized);
    }
}
