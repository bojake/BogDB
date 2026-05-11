using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Common;
using BogDb.Core.Processor.Operator.Persistent;



namespace BogDb.Tests.Storage
{
    /// <summary>
    /// Integration tests for the COPY → NodeTableData storage round-trip.
    ///
    /// Uses CopyNode directly (bypassing the query pipeline) to test the core fix:
    /// all CSV property columns, not just the primary key, are ingested and typed.
    ///
    /// C++ parity: equivalent to bogdb-cpp test/test_files/copy/*.test
    /// </summary>
    public class StorageRoundTripTests
    {
        // ── helpers ──────────────────────────────────────────────────────────────

        private static string ResolvePath(string relative)
        {
            var normalised = relative.Replace('\\', Path.DirectorySeparatorChar);
            var tail = string.Join(Path.DirectorySeparatorChar.ToString(),
                normalised.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                          .SkipWhile(s => s == ".."));

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, tail);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return relative;
        }

        private static string PersonCsv => ResolvePath("../../../../dataset/storage-roundtrip/person.csv");

        private static BogDb.Core.Processor.ExecutionContext MakeContext() =>
            new(new BogDb.Core.Transaction.Transaction(
                    BogDb.Core.Transaction.TransactionType.READ_ONLY), null);


        // ── ParseCell unit tests ──────────────────────────────────────────────────

        [Fact]
        public void ParseCell_Int64_ParsesCorrectly()
        {
            Assert.Equal(42L, CopyNode.ParseCell("42", LogicalTypeID.INT64));
        }

        [Fact]
        public void ParseCell_String_ReturnsRawTrimmed()
        {
            Assert.Equal("Alice", CopyNode.ParseCell("  Alice  ", LogicalTypeID.STRING));
        }

        [Fact]
        public void ParseCell_Double_ParsesInvariantCulture()
        {
            Assert.Equal(3.14, (double)CopyNode.ParseCell("3.14", LogicalTypeID.DOUBLE), precision: 5);
        }

        [Fact]
        public void ParseCell_Bool_ParsesTrue()
        {
            Assert.True((bool)CopyNode.ParseCell("True", LogicalTypeID.BOOL));
        }

        // ── CopyNode operator tests ───────────────────────────────────────────────

        private static (BogDatabase db, CopyNode node) BuildCopyNode()
        {
            var db = BogDatabase.Open(":memory:");
            db.NodeTables["Person"] = new NodeTableData();

            var propNames = new List<string> { "id", "name", "age" };
            var propTypes = new List<LogicalTypeID>
                { LogicalTypeID.INT64, LogicalTypeID.STRING, LogicalTypeID.INT64 };

            var node = new CopyNode("Person", PersonCsv, db, propNames, propTypes, id: 0);
            return (db, node);
        }

        [Fact]
        public void CopyNode_IngestsAllFourRows()
        {
            var (db, node) = BuildCopyNode();
            node.GetNextTuple(MakeContext());
            Assert.Equal(4, db.NodeTables["Person"].Data.Count);
        }

        [Fact]
        public void CopyNode_AllRowsHaveAllThreeProperties()
        {
            // Core regression test: CopyNode previously only stored the primary key.
            var (db, node) = BuildCopyNode();
            node.GetNextTuple(MakeContext());

            foreach (var kvp in db.NodeTables["Person"].Data)
            {
                Assert.True(kvp.Value.ContainsKey("id"),   $"Missing 'id'   in row {kvp.Key}");
                Assert.True(kvp.Value.ContainsKey("name"), $"Missing 'name' in row {kvp.Key}");
                Assert.True(kvp.Value.ContainsKey("age"),  $"Missing 'age'  in row {kvp.Key}");
            }
        }

        [Fact]
        public void CopyNode_NamePropertyTypedAsString()
        {
            var (db, node) = BuildCopyNode();
            node.GetNextTuple(MakeContext());
            foreach (var props in db.NodeTables["Person"].Data.Values)
                Assert.IsType<string>(props["name"]);
        }

        [Fact]
        public void CopyNode_AgePropertyTypedAsInt64()
        {
            var (db, node) = BuildCopyNode();
            node.GetNextTuple(MakeContext());
            foreach (var props in db.NodeTables["Person"].Data.Values)
                Assert.IsType<long>(props["age"]);
        }

        [Fact]
        public void CopyNode_IdPropertyTypedAsInt64()
        {
            var (db, node) = BuildCopyNode();
            node.GetNextTuple(MakeContext());
            foreach (var props in db.NodeTables["Person"].Data.Values)
                Assert.IsType<long>(props["id"]);
        }

        [Fact]
        public void CopyNode_AllExpectedNamesPresent()
        {
            // Golden-value: person.csv has Alice, Bob, Carol, Dan.
            var (db, node) = BuildCopyNode();
            node.GetNextTuple(MakeContext());

            var names = db.NodeTables["Person"].Data.Values
                .Select(p => p.TryGetValue("name", out var n) ? n?.ToString() : null)
                .OrderBy(x => x)
                .ToList();

            Assert.Equal(new[] { "Alice", "Bob", "Carol", "Dan" }, names);
        }
    }
}
