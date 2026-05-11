using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class BogConnectionWriteTransactionTests
{
    [Fact]
    public void ExecuteWriteTransaction_CommitsMultipleDirectWrites_AsOneUnit()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bogdb-write-batch-{Guid.NewGuid():N}");
        try
        {
            using (var db = BogDatabase.Open(dbPath))
            using (var conn = new BogConnection(db))
            {
                conn.BeginWriteTransaction();
                conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING,
                    ["name"] = LogicalTypeID.STRING
                });
                conn.EnsureRelTable("KNOWS", "Person", "Person", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING
                });
                conn.Commit();

                conn.ExecuteWriteTransaction(() =>
                {
                    conn.UpsertNodeById("Person", "p1", new Dictionary<string, object>
                    {
                        ["id"] = "p1",
                        ["name"] = "Ada"
                    });
                    conn.UpsertNodeById("Person", "p2", new Dictionary<string, object>
                    {
                        ["id"] = "p2",
                        ["name"] = "Grace"
                    });
                    conn.UpsertRelationshipById("KNOWS", "p1", "p2", new Dictionary<string, object>
                    {
                        ["id"] = "edge:p1:knows:p2"
                    });
                });
            }

            using var reopened = BogDatabase.Open(dbPath);
            using var verifyConn = new BogConnection(reopened);
            var rows = verifyConn.Query("MATCH (p:Person) RETURN p.id AS id, p.name AS name");
            Assert.True(rows.IsSuccess);
            Assert.Equal(2UL, rows.GetNumTuples());

            var rels = verifyConn.Query("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id AS fromId, b.id AS toId");
            Assert.True(rels.IsSuccess);
            Assert.Equal(1UL, rels.GetNumTuples());
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, recursive: true);
        }
    }
}
