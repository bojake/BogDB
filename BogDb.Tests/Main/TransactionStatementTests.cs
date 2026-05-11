using Xunit;
using BogDb.Core.Main;

namespace BogDb.Tests.Main;

public class TransactionStatementTests
{
    [Fact]
    public void Query_BeginAndCommitTransaction_TransitionsClientContextState()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var beginResult = connection.Query("BEGIN TRANSACTION");
        Assert.True(beginResult.IsSuccess);
        Assert.NotNull(connection.ClientContext.ActiveTransaction);

        var commitResult = connection.Query("COMMIT");
        Assert.True(commitResult.IsSuccess);
        Assert.Null(connection.ClientContext.ActiveTransaction);
    }

    [Fact]
    public void Query_BeginReadOnlyAndRollback_TransitionsClientContextState()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        var beginResult = connection.Query("BEGIN TRANSACTION READ ONLY");
        Assert.True(beginResult.IsSuccess);
        Assert.NotNull(connection.ClientContext.ActiveTransaction);

        var rollbackResult = connection.Query("ROLLBACK");
        Assert.True(rollbackResult.IsSuccess);
        Assert.Null(connection.ClientContext.ActiveTransaction);
    }

    [Fact]
    public void ReadOnlyDatabase_RejectsWriteTransactionAndMutatingQuery()
    {
        using var database = BogDatabase.Open(":memory:", new BogDatabaseOptions().WithReadOnly());
        using var connection = new BogConnection(database);

        var beginException = Assert.Throws<InvalidOperationException>(() => connection.BeginWriteTransaction());
        Assert.Contains("read-only", beginException.Message, System.StringComparison.OrdinalIgnoreCase);

        var result = connection.Query("CREATE (:Person {id:'p1'})");
        Assert.False(result.IsSuccess);
        Assert.Contains("read-only", result.ErrorMessage, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Query_Commit_FinalizesNodeTableInsertVersions()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);
        database.NodeTables["Person"] = new NodeTableData();
        var table = database.NodeTables["Person"];

        Assert.True(connection.Query("BEGIN TRANSACTION").IsSuccess);
        var writer = connection.ClientContext.ActiveTransaction!;
        table.Upsert(writer, "n1", new System.Collections.Generic.Dictionary<string, object> { ["name"] = "Alice" });

        Assert.True(connection.Query("COMMIT").IsSuccess);
        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY, id: 100, startTS: writer.CommitTS);
        Assert.Single(table.EnumerateRows(reader));
    }

    [Fact]
    public void Query_Commit_FinalizesNodeTableDeleteVersions()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);
        database.NodeTables["Person"] = new NodeTableData();
        var table = database.NodeTables["Person"];
        table.Upsert("n1", new System.Collections.Generic.Dictionary<string, object> { ["name"] = "Alice" });

        Assert.True(connection.Query("BEGIN TRANSACTION").IsSuccess);
        var writer = connection.ClientContext.ActiveTransaction!;
        Assert.True(table.Remove(writer, "n1"));
        Assert.True(connection.Query("COMMIT").IsSuccess);

        var reader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY, id: 101, startTS: writer.CommitTS);
        Assert.Empty(table.EnumerateRows(reader));
    }

    [Fact]
    public void Query_Rollback_RevertsNodePropertyUpdates()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        connection.BeginWriteTransaction();
        connection.EnsureNodeTable("Person", new System.Collections.Generic.Dictionary<string, BogDb.Core.Common.LogicalTypeID>
        {
            ["id"] = BogDb.Core.Common.LogicalTypeID.INT64,
            ["name"] = BogDb.Core.Common.LogicalTypeID.STRING
        });
        connection.UpsertNode("Person", 1L, new System.Collections.Generic.Dictionary<string, object>
        {
            ["id"] = 1L,
            ["name"] = "Bob"
        });
        connection.Commit();

        Assert.True(connection.Query("BEGIN TRANSACTION").IsSuccess);
        Assert.True(connection.Query("MATCH (p:Person) WHERE p.id = 1 SET p.name = 'Bobby'").IsSuccess);
        Assert.True(connection.Query("ROLLBACK").IsSuccess);

        var result = connection.Query("MATCH (p:Person) RETURN p.name");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("Bob", result.GetNext().GetString(0));
    }

    [Fact]
    public void Query_AutoCommit_Set_PersistsNodePropertyUpdate()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        connection.BeginWriteTransaction();
        connection.EnsureNodeTable("Person", new System.Collections.Generic.Dictionary<string, BogDb.Core.Common.LogicalTypeID>
        {
            ["id"] = BogDb.Core.Common.LogicalTypeID.INT64,
            ["name"] = BogDb.Core.Common.LogicalTypeID.STRING
        });
        connection.UpsertNode("Person", 1L, new System.Collections.Generic.Dictionary<string, object>
        {
            ["id"] = 1L,
            ["name"] = "Bob"
        });
        connection.Commit();

        var updateResult = connection.Query("MATCH (p:Person) WHERE p.id = 1 SET p.name = 'Bobby' RETURN p.name");
        Assert.True(updateResult.IsSuccess, updateResult.ErrorMessage);
        Assert.True(updateResult.HasNext());
        Assert.Equal("Bobby", updateResult.GetNext().GetString(0));

        var result = connection.Query("MATCH (p:Person) RETURN p.name");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.HasNext());
        Assert.Equal("Bobby", result.GetNext().GetString(0));
    }

    [Fact]
    public void Query_AutoCommit_Delete_RemovesNode()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);

        connection.BeginWriteTransaction();
        connection.EnsureNodeTable("Person", new System.Collections.Generic.Dictionary<string, BogDb.Core.Common.LogicalTypeID>
        {
            ["id"] = BogDb.Core.Common.LogicalTypeID.INT64,
            ["name"] = BogDb.Core.Common.LogicalTypeID.STRING
        });
        connection.UpsertNode("Person", 1L, new System.Collections.Generic.Dictionary<string, object>
        {
            ["id"] = 1L,
            ["name"] = "Alice"
        });
        connection.UpsertNode("Person", 2L, new System.Collections.Generic.Dictionary<string, object>
        {
            ["id"] = 2L,
            ["name"] = "Bob"
        });
        connection.Commit();

        var deleteResult = connection.Query("MATCH (p:Person) WHERE p.id = 2 DELETE p");
        Assert.True(deleteResult.IsSuccess, deleteResult.ErrorMessage);

        var result = connection.Query("MATCH (p:Person) RETURN p.id ORDER BY p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        Assert.Equal(1L, result.GetNext().GetInt64(0));
    }

    [Fact]
    public void UpsertNodeById_AutoCommitUpdate_PreservesOlderReaderSnapshot()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);
        database.NodeTables["Person"] = new NodeTableData();
        var table = database.NodeTables["Person"];

        connection.BeginWriteTransaction();
        connection.UpsertNode("Person", "n1", new System.Collections.Generic.Dictionary<string, object>
        {
            ["name"] = "Alice"
        });
        var seedWriter = connection.ClientContext.ActiveTransaction!;
        connection.Commit();

        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 102,
            startTS: seedWriter.CommitTS);

        connection.UpsertNodeById("Person", "n1", new System.Collections.Generic.Dictionary<string, object>
        {
            ["name"] = "Alicia"
        });

        Assert.True(table.TryGetProperties(oldReader, "n1", out var oldProps));
        Assert.Equal("Alice", oldProps!["name"]);

        var freshReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 103,
            startTS: seedWriter.CommitTS + 100);
        Assert.True(table.TryGetProperties(freshReader, "n1", out var newProps));
        Assert.Equal("Alicia", newProps!["name"]);
    }

    [Fact]
    public void UpsertRelationshipById_AutoCommitUpdate_PreservesOlderReaderSnapshot()
    {
        using var database = BogDatabase.Open(":memory:");
        using var connection = new BogConnection(database);
        database.RelTables["KNOWS"] = new RelTableData();
        var table = database.RelTables["KNOWS"];
        var key = new EdgeKey("a", "b");

        connection.BeginWriteTransaction();
        connection.UpsertRelationship("KNOWS", "a", "b", new System.Collections.Generic.Dictionary<string, object>
        {
            ["weight"] = 1L
        });
        var seedWriter = connection.ClientContext.ActiveTransaction!;
        connection.Commit();

        var oldReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 104,
            startTS: seedWriter.CommitTS);

        connection.UpsertRelationshipById("KNOWS", "a", "b", new System.Collections.Generic.Dictionary<string, object>
        {
            ["weight"] = 2L
        });

        Assert.True(table.TryGetProperties(oldReader, key, out var oldProps));
        Assert.Equal(1L, oldProps!["weight"]);

        var freshReader = new BogDb.Core.Transaction.Transaction(
            BogDb.Core.Transaction.TransactionType.READ_ONLY,
            id: 105,
            startTS: seedWriter.CommitTS + 100);
        Assert.True(table.TryGetProperties(freshReader, key, out var newProps));
        Assert.Equal(2L, newProps!["weight"]);
    }
}
