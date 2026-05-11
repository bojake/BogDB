using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Processor;

/// <summary>
/// Validates logical-to-physical mapping for relationship SET/DELETE operations.
/// </summary>
public sealed class RelSetDeleteTests
{
    private static BogConnection CreateDb(out BogDatabase db)
    {
        db = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id", LogicalTypeID.INT64 },
            { "age", LogicalTypeID.INT64 }
        });
        conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
        {
            { "since", LogicalTypeID.INT64 },
            { "note", LogicalTypeID.STRING }
        });
        conn.Commit();

        conn.BeginWriteTransaction();
        conn.UpsertNode("Person", 1L, new Dictionary<string, object> { ["id"] = 1L, ["age"] = 25L });
        conn.UpsertNode("Person", 2L, new Dictionary<string, object> { ["id"] = 2L, ["age"] = 40L });
        conn.UpsertNode("Person", 3L, new Dictionary<string, object> { ["id"] = 3L, ["age"] = 55L });
        conn.UpsertRelationship("KNOWS", 1L, 2L, new Dictionary<string, object> { ["since"] = 2020L });
        conn.UpsertRelationship("KNOWS", 1L, 3L, new Dictionary<string, object> { ["since"] = 2010L });
        conn.Commit();

        return conn;
    }

    [Fact]
    public void SetRelProperty_UpdatesRelationshipProperty()
    {
        using var conn = CreateDb(out var db);

        var result = conn.Query(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) WHERE b.id = 2 " +
            "SET r.since = 2024 RETURN r.since");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(2024L, result.GetNext().GetInt64(0));
    }

    [Fact]
    public void SetRelProperty_Rollback_RevertsRelationshipProperty()
    {
        using var conn = CreateDb(out var db);

        Assert.True(conn.Query("BEGIN TRANSACTION").IsSuccess);
        var updateResult = conn.Query(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) WHERE b.id = 2 " +
            "SET r.since = 2024 RETURN r.since");
        Assert.True(updateResult.IsSuccess, updateResult.ErrorMessage);
        Assert.True(conn.Query("ROLLBACK").IsSuccess);

        var result = conn.Query(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) WHERE b.id = 2 RETURN r.since");

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal(2020L, result.GetNext().GetInt64(0));
    }

    [Fact]
    public void DeleteRel_RemovesEdgeFromMatchResults()
    {
        using var conn = CreateDb(out var db);

        var deleteResult = conn.Query(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) WHERE b.id = 2 DELETE r");
        Assert.True(deleteResult.IsSuccess, deleteResult.ErrorMessage);

        var countResult = conn.Query(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN count(*)");

        Assert.True(countResult.IsSuccess, countResult.ErrorMessage);
        Assert.True(countResult.HasNext());
        Assert.Equal(1L, countResult.GetNext().GetInt64(0));
    }

    [Fact]
    public void SetRelProperty_WithDuplicateSameEndpointEdges_TargetsMatchedRow()
    {
        using var conn = CreateDb(out var db);

        var mergeResult = conn.Query(
            "MATCH (a:Person {id:1}), (b:Person {id:2}) " +
            "MERGE (a)-[:KNOWS {since:2021}]->(b)");
        Assert.True(mergeResult.IsSuccess, mergeResult.ErrorMessage);

        var updateResult = conn.Query(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) WHERE a.id = 1 AND b.id = 2 AND r.since = 2021 " +
            "SET r.note = 'parallel' RETURN r.since, r.note");

        Assert.True(updateResult.IsSuccess, updateResult.ErrorMessage);
        Assert.True(updateResult.HasNext());
        var updated = updateResult.GetNext();
        Assert.Equal(2021L, updated.GetInt64(0));
        Assert.Equal("parallel", updated.GetString(1));

        var verifyResult = conn.Query(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) WHERE a.id = 1 AND b.id = 2 " +
            "RETURN r.since, coalesce(r.note, 'none') ORDER BY r.since");

        Assert.True(verifyResult.IsSuccess, verifyResult.ErrorMessage);
        Assert.True(verifyResult.HasNext());
        var first = verifyResult.GetNext();
        Assert.Equal(2020L, first.GetInt64(0));
        Assert.Equal("none", first.GetString(1));
        Assert.True(verifyResult.HasNext());
        var second = verifyResult.GetNext();
        Assert.Equal(2021L, second.GetInt64(0));
        Assert.Equal("parallel", second.GetString(1));
        Assert.False(verifyResult.HasNext());
    }

    [Fact]
    public void DeleteRel_WithDuplicateSameEndpointEdges_RemovesOnlyMatchedRow()
    {
        using var conn = CreateDb(out var db);

        var mergeResult = conn.Query(
            "MATCH (a:Person {id:1}), (b:Person {id:2}) " +
            "MERGE (a)-[:KNOWS {since:2021}]->(b)");
        Assert.True(mergeResult.IsSuccess, mergeResult.ErrorMessage);

        var deleteResult = conn.Query(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) WHERE a.id = 1 AND b.id = 2 AND r.since = 2021 DELETE r");
        Assert.True(deleteResult.IsSuccess, deleteResult.ErrorMessage);

        var verifyResult = conn.Query(
            "MATCH (a:Person)-[r:KNOWS]->(b:Person) WHERE a.id = 1 AND b.id = 2 RETURN r.since ORDER BY r.since");

        Assert.True(verifyResult.IsSuccess, verifyResult.ErrorMessage);
        Assert.True(verifyResult.HasNext());
        Assert.Equal(2020L, verifyResult.GetNext().GetInt64(0));
        Assert.False(verifyResult.HasNext());
    }
}
