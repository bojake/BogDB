using System;
using System.Collections.Generic;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Common;

namespace BogDb.Tests.Main;

/// <summary>
/// Tests for QueryResult error propagation.
/// </summary>
public class QueryResultErrorPropagationTests
{
    private static (BogDatabase db, BogConnection conn) Setup()
    {
        var db   = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        // Use helpers instead of Cypher CREATE to avoid syntax quirks
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["id"]  = LogicalTypeID.INT64,
            ["name"] = LogicalTypeID.STRING,
            ["age"]  = LogicalTypeID.INT64,
        });
        conn.EnsureNodeTable("Movie", new Dictionary<string, LogicalTypeID>
        {
            ["id"]    = LogicalTypeID.INT64,
            ["title"] = LogicalTypeID.STRING,
        });
        conn.EnsureRelTable("KNOWS", "Person", "Person",
            new Dictionary<string, LogicalTypeID> { ["since"] = LogicalTypeID.INT64 });

        conn.UpsertNodeById("Person", "1", new Dictionary<string, object> { ["id"]=1L, ["name"]="Alice", ["age"]=30L });
        conn.UpsertNodeById("Person", "2", new Dictionary<string, object> { ["id"]=2L, ["name"]="Bob",   ["age"]=25L });
        conn.Commit();

        return (db, conn);
    }

    // ── Syntax / parse errors ─────────────────────────────────────────────────

    [Fact]
    public void Query_InvalidSyntax_IsFailure()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        var r = conn.Query("TOTALLY INVALID CYPHER !!!");
        Assert.False(r.IsSuccess, "Expected syntax error to be a failure");
        Assert.False(string.IsNullOrWhiteSpace(r.ErrorMessage), "Expected non-empty error message");
        Assert.Equal(0UL, r.GetNumTuples());
        Assert.False(r.HasNext());
    }

    [Fact]
    public void Query_EmptyString_IsFailure()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        var r = conn.Query(string.Empty);
        Assert.False(r.IsSuccess, "Empty query should fail");
        Assert.False(string.IsNullOrWhiteSpace(r.ErrorMessage), "Expected non-empty error message");
    }

    // ── Transaction state errors ──────────────────────────────────────────────

    [Fact]
    public void BeginTransactionTwice_IsFailure()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        Assert.True(conn.Query("BEGIN TRANSACTION").IsSuccess);
        var r2 = conn.Query("BEGIN TRANSACTION");
        Assert.False(r2.IsSuccess, "Second BEGIN TRANSACTION should fail");
        Assert.Contains("already active", r2.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommitWithoutBegin_IsFailure()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        var r = conn.Query("COMMIT");
        Assert.False(r.IsSuccess, "COMMIT without BEGIN should fail");
        Assert.False(string.IsNullOrWhiteSpace(r.ErrorMessage), "Expected non-empty error message");
    }

    [Fact]
    public void RollbackWithoutBegin_IsFailure()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        var r = conn.Query("ROLLBACK");
        Assert.False(r.IsSuccess, "ROLLBACK without BEGIN should fail");
        Assert.False(string.IsNullOrWhiteSpace(r.ErrorMessage), "Expected non-empty error message");
    }

    // ── Successful queries return IsSuccess = true ────────────────────────────

    [Fact]
    public void Query_ValidMatch_IsSuccess()
    {
        var (db, conn) = Setup();
        using (db) using (conn)
        {
            var r = conn.Query("MATCH (p:Person) RETURN p.name");
            Assert.True(r.IsSuccess, $"MATCH failed: {r.ErrorMessage}");
            Assert.Equal(string.Empty, r.ErrorMessage);
            Assert.True(r.GetNumTuples() > 0, $"Expected >0 rows, got {r.GetNumTuples()}");
        }
    }

    [Fact]
    public void Query_MatchEmptyResult_IsSuccess_ZeroRows()
    {
        var (db, conn) = Setup();
        using (db) using (conn)
        {
            // Valid query, no matching rows
            var r = conn.Query("MATCH (p:Person) WHERE p.age > 999 RETURN p.name");
            Assert.True(r.IsSuccess);
            Assert.Equal(0UL, r.GetNumTuples());
        }
    }

    // ── GetNext on failed result throws ───────────────────────────────────────

    [Fact]
    public void GetNext_OnFailedResult_Throws()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        var r = conn.Query("THIS IS BROKEN");
        Assert.False(r.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => r.GetNext());
    }

    [Fact]
    public void GetNext_PastEnd_Throws()
    {
        var (db, conn) = Setup();
        using (db) using (conn)
        {
            var r = conn.Query("MATCH (p:Person) WHERE p.id = 1 RETURN p.name");
            Assert.True(r.IsSuccess);
            Assert.True(r.HasNext());
            r.GetNext(); // consume the one row
            Assert.False(r.HasNext());
            Assert.Throws<InvalidOperationException>(() => r.GetNext());
        }
    }

    // ── Error message is informative (not empty, not generic) ─────────────────

    [Fact]
    public void ErrorMessage_ContainsMeaningfulText()
    {
        using var db = BogDatabase.Open(":memory:");
        using var conn = new BogConnection(db);
        var r = conn.Query("MATCH BAD SYNTAX HERE RETURN x");
        Assert.False(r.IsSuccess);
        // Message should be non-trivial — more than a few characters
        Assert.True(r.ErrorMessage.Length > 5,
            $"Error message too short: '{r.ErrorMessage}'");
    }
}
