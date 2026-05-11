using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Tests.Extraction;

internal static class ExtractionTestGraphFactory
{
    public static BogDatabase CreateSampleDatabase()
    {
        var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.BeginWriteTransaction();

        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["name"] = LogicalTypeID.STRING,
            ["age"] = LogicalTypeID.INT64
        });
        conn.EnsureNodeTable("City", new Dictionary<string, LogicalTypeID>
        {
            ["name"] = LogicalTypeID.STRING
        });
        conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
        {
            ["since"] = LogicalTypeID.INT64
        });
        conn.EnsureRelTable("LIVES_IN", "Person", "City", new Dictionary<string, LogicalTypeID>
        {
            ["since"] = LogicalTypeID.INT64
        });

        conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30L
        });
        conn.UpsertNode("Person", "bob", new Dictionary<string, object>
        {
            ["name"] = "Bob",
            ["age"] = 28L
        });
        conn.UpsertNode("Person", "carol", new Dictionary<string, object>
        {
            ["name"] = "Carol",
            ["age"] = 26L
        });
        conn.UpsertNode("City", "seattle", new Dictionary<string, object>
        {
            ["name"] = "Seattle"
        });

        conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object>
        {
            ["since"] = 2020L
        });
        conn.UpsertRelationship("KNOWS", "bob", "carol", new Dictionary<string, object>
        {
            ["since"] = 2021L
        });
        conn.UpsertRelationship("LIVES_IN", "alice", "seattle", new Dictionary<string, object>
        {
            ["since"] = 2019L
        });

        conn.Commit();
        return db;
    }

    public static BogDatabase CreateAmbiguousEndpointDatabase()
    {
        var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);
        conn.BeginWriteTransaction();

        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            ["name"] = LogicalTypeID.STRING
        });
        conn.EnsureNodeTable("City", new Dictionary<string, LogicalTypeID>
        {
            ["name"] = LogicalTypeID.STRING
        });
        conn.EnsureRelTable("LIVES_IN", "Person", "City", new Dictionary<string, LogicalTypeID>
        {
            ["since"] = LogicalTypeID.INT64
        });

        conn.UpsertNode("Person", "shared-id", new Dictionary<string, object>
        {
            ["name"] = "Alice"
        });
        conn.UpsertNode("City", "shared-id", new Dictionary<string, object>
        {
            ["name"] = "Seattle"
        });
        conn.UpsertRelationship("LIVES_IN", "shared-id", "shared-id", new Dictionary<string, object>
        {
            ["since"] = 2019L
        });

        conn.Commit();
        return db;
    }
}
