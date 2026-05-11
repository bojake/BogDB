using System.Text.Json;
using BogDb.Core.Common;
using BogDb.Core.Main;
using BogDb.Mcp.Server.Services;
using Xunit;

namespace BogDb.Tests.Mcp;

public sealed class McpQueryServiceTests
{
    [Fact]
    public async Task BogDbQueryToolService_ExecutesReadOnlyQuery()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bogdb-mcp-{Guid.NewGuid():N}");
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
                conn.UpsertNodeById("Person", "p1", new Dictionary<string, object>
                {
                    ["id"] = "p1",
                    ["name"] = "Ada"
                });
                conn.Commit();
            }

            var service = new BogDbQueryToolService();
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                databasePath = dbPath,
                cypher = "MATCH (p:Person) RETURN p.id AS id, p.name AS name"
            }));

            var result = await service.ExecuteAsync(arguments.RootElement, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(new[] { "id", "name" }, result.Columns);
            Assert.Single(result.Rows);
            Assert.Equal("p1", result.Rows[0]["id"]);
            Assert.Equal("Ada", result.Rows[0]["name"]);
            Assert.False(result.Truncated);
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, recursive: true);
        }
    }

    [Fact]
    public async Task BogDbQueryToolService_RejectsMutatingQuery()
    {
        var service = new BogDbQueryToolService();
        using var arguments = JsonDocument.Parse("""
        {
          "databasePath": ":memory:",
          "cypher": "CREATE (:Person {id:'p1'})"
        }
        """);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(arguments.RootElement, CancellationToken.None));
    }

    [Fact]
    public void BogDbSchemaToolService_ReturnsNodeAndRelationshipMetadata()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bogdb-mcp-{Guid.NewGuid():N}");
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
                conn.EnsureNodeTable("Company", new Dictionary<string, LogicalTypeID>
                {
                    ["id"] = LogicalTypeID.STRING
                });
                conn.EnsureRelTable("WorksAt", "Person", "Company", new Dictionary<string, LogicalTypeID>
                {
                    ["since"] = LogicalTypeID.STRING
                });
                conn.Commit();
            }

            var service = new BogDbSchemaToolService();
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                databasePath = dbPath
            }));

            var schema = service.GetSchema(arguments.RootElement);
            var json = JsonSerializer.Serialize(schema);

            Assert.Contains("\"name\":\"Person\"", json);
            Assert.Contains("\"primaryKey\":\"id\"", json);
            Assert.Contains("\"name\":\"WorksAt\"", json);
            Assert.Contains("\"from\":\"Person\"", json);
            Assert.Contains("\"to\":\"Company\"", json);
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, recursive: true);
        }
    }
}
