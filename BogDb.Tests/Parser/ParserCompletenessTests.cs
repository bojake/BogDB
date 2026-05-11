using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Parser
{
    /// <summary>
    /// Tests specifically for parser completeness:
    /// runtime arithmetic, string functions, and UNWIND execution.
    /// C++ parity: ensures expression evaluation matches bogdb-cpp behavior.
    /// </summary>
    public class ParserCompletenessTests
    {
        // ── Test setup helper ─────────────────────────────────────────────────

        private static (BogDatabase db, BogConnection conn) CreatePersonDb(int age = 30, string name = "Alice")
        {
            var db = BogDatabase.Open(":memory:");
            var conn = new BogConnection(db);

            conn.BeginWriteTransaction();
            conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
            {
                { "name", LogicalTypeID.STRING },
                { "age",  LogicalTypeID.INT64  }
            });
            conn.Commit();

            conn.BeginWriteTransaction();
            conn.UpsertNode("Person", 1L, new Dictionary<string, object>
            {
                ["name"] = name,
                ["age"]  = (long)age
            });
            conn.Commit();

            return (db, conn);
        }


        // ── Arithmetic: literal constant-folding ──────────────────────────────

        [Fact]
        public void Return_LiteralAddition_ReturnsFoldedResult()
        {
            var db = BogDatabase.Open(":memory:");
            using var conn = new BogConnection(db);
            var result = conn.Query("RETURN 1 + 2");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            Assert.Equal(3L, result.GetNext().GetInt64(0));
        }

        [Fact]
        public void Return_LiteralArithmetic_MultiOps()
        {
            var db = BogDatabase.Open(":memory:");
            using var conn = new BogConnection(db);
            var result = conn.Query("RETURN 10 - 3");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            Assert.Equal(7L, result.GetNext().GetInt64(0));
        }

        // ── Arithmetic: property + literal at runtime ─────────────────────────

        [Fact]
        public void Return_PropertyPlusLiteral_AddsCorrectly()
        {
            var (db, conn) = CreatePersonDb(age: 25);
            using var _ = conn;
            var result = conn.Query("MATCH (n:Person) RETURN n.age + 10");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext(), "Expected at least one result row");
            Assert.Equal(35L, result.GetNext().GetInt64(0));
        }

        [Fact]
        public void Return_PropertyTimesLiteral_MultipliesCorrectly()
        {
            var (db, conn) = CreatePersonDb(age: 6);
            using var _ = conn;
            var result = conn.Query("MATCH (n:Person) RETURN n.age * 7");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            Assert.Equal(42L, result.GetNext().GetInt64(0));
        }

        [Fact]
        public void Where_ArithmeticFilter_FiltersCorrectly()
        {
            var (db, conn) = CreatePersonDb(age: 30);
            using var _ = conn;
            // age * 2 = 60 > 50 → row should be returned
            var result = conn.Query("MATCH (n:Person) WHERE n.age * 2 > 50 RETURN n.age");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext(), "Arithmetic filter should pass for 30 * 2 = 60 > 50");
            Assert.Equal(30L, result.GetNext().GetInt64(0));
        }

        [Fact]
        public void Where_ArithmeticFilter_ExcludesNonMatching()
        {
            var (db, conn) = CreatePersonDb(age: 20);
            using var _ = conn;
            // age * 2 = 40 is NOT > 50 → no rows
            var result = conn.Query("MATCH (n:Person) WHERE n.age * 2 > 50 RETURN n.age");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.False(result.HasNext(), "Arithmetic filter should exclude 20 * 2 = 40 (not > 50)");
        }

        // ── String predicates ─────────────────────────────────────────────────

        [Fact]
        public void Where_StartsWith_FiltersCorrectly()
        {
            var (db, conn) = CreatePersonDb(name: "Alice");
            using var _ = conn;
            var result = conn.Query("MATCH (n:Person) WHERE starts_with(n.name, 'Al') RETURN n.name");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext(), "starts_with('Alice', 'Al') should be true");
            Assert.Equal("Alice", result.GetNext().GetString(0));
        }

        [Fact]
        public void Where_Contains_FiltersCorrectly()
        {
            var (db, conn) = CreatePersonDb(name: "Robert");
            using var _ = conn;
            var result = conn.Query("MATCH (n:Person) WHERE contains(n.name, 'ob') RETURN n.name");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext(), "contains('Robert', 'ob') should be true");
        }

        [Fact]
        public void Where_EndsWith_Negative_ExcludesNonMatching()
        {
            var (db, conn) = CreatePersonDb(name: "Alice");
            using var _ = conn;
            // "Alice" does NOT end with "Bob"
            var result = conn.Query("MATCH (n:Person) WHERE ends_with(n.name, 'Bob') RETURN n.name");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.False(result.HasNext(), "ends_with('Alice', 'Bob') should be false");
        }

        // ── String transformation functions ───────────────────────────────────

        [Fact]
        public void Return_ToLower_ReturnsLowercaseString()
        {
            var (db, conn) = CreatePersonDb(name: "ALICE");
            using var _ = conn;
            var result = conn.Query("MATCH (n:Person) RETURN toLower(n.name)");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            Assert.Equal("alice", result.GetNext().GetString(0));
        }

        [Fact]
        public void Return_ToUpper_ReturnsUppercaseString()
        {
            var (db, conn) = CreatePersonDb(name: "alice");
            using var _ = conn;
            var result = conn.Query("MATCH (n:Person) RETURN toUpper(n.name)");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            Assert.Equal("ALICE", result.GetNext().GetString(0));
        }

        [Fact]
        public void Return_Size_ReturnsStringLength()
        {
            var (db, conn) = CreatePersonDb(name: "Alice");
            using var _ = conn;
            var result = conn.Query("MATCH (n:Person) RETURN size(n.name)");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            Assert.Equal(5L, result.GetNext().GetInt64(0));
        }
    }
}
