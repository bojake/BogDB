using System;
using System.IO;
using Xunit;
using BogDb.Core.Main;

namespace BogDb.Tests.Main;

public class CopyRelTests
{
    [Fact]
    public void CopyRel_ExecutesAndCreatesTraversableRelationships()
    {
        var tempFile = Path.GetTempFileName().Replace("\\", "/");
        try
        {
            File.WriteAllText(tempFile, "from_id,to_id\n1,2\n2,3\n");

            using var database = BogDatabase.Open(":memory:");
            using var connection = new BogConnection(database);

            connection.BeginWriteTransaction();
            connection.EnsureNodeTable("Person", new System.Collections.Generic.Dictionary<string, BogDb.Core.Common.LogicalTypeID>
            {
                { "id", BogDb.Core.Common.LogicalTypeID.INT64 }
            });
            connection.EnsureRelTable("KNOWS", "Person", "Person", new System.Collections.Generic.Dictionary<string, BogDb.Core.Common.LogicalTypeID>());
            connection.UpsertNode("Person", 1L, new System.Collections.Generic.Dictionary<string, object>());
            connection.UpsertNode("Person", 2L, new System.Collections.Generic.Dictionary<string, object>());
            connection.UpsertNode("Person", 3L, new System.Collections.Generic.Dictionary<string, object>());
            connection.Commit();

            var copyResult = connection.Query($"COPY KNOWS FROM '{tempFile}'");
            Assert.True(copyResult.IsSuccess);

            var queryResult = connection.Query("MATCH (a:Person)-[:KNOWS]->(b:Person) RETURN a.id, b.id ORDER BY a.id, b.id");
            Assert.True(queryResult.IsSuccess);
            Assert.Equal(2UL, queryResult.GetNumTuples());
            var row1 = queryResult.GetNext();
            var row2 = queryResult.GetNext();
            Assert.Equal(1L, row1.GetInt64(0));
            Assert.Equal(2L, row1.GetInt64(1));
            Assert.Equal(2L, row2.GetInt64(0));
            Assert.Equal(3L, row2.GetInt64(1));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
