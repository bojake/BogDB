using System;
using System.Collections.Generic;
using System.IO;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public class ProgrammaticFacadeTests
{
    [Fact]
    public void MinimalEmbeddedSurface_CanCreateAndUpsertNode()
    {
        // Arrange
        var dbPath = Path.Combine(Path.GetTempPath(), $"embedded_test_db_{Guid.NewGuid():N}");
        try
        {
            using var db = BogDatabase.Open(dbPath);
            using var conn = new BogConnection(db);

            // Act
            conn.BeginWriteTransaction();

            // 1. Create table schema
            var schema = new Dictionary<string, LogicalTypeID>
            {
                {"name", LogicalTypeID.STRING},
                {"age", LogicalTypeID.INT32}
            };
            conn.EnsureNodeTable("Person", schema);

            // 2. Insert mapped dictionary
            var props = new Dictionary<string, object>
            {
                {"name", "Cypher Bob"},
                {"age", 45}
            };
            conn.UpsertNode("Person", 100L, props);
            conn.Commit();

            // Assert (Read via isolated read paths)
            var result = conn.ReadNode("Person", 100L);
            Assert.NotNull(result);
            Assert.Equal("Cypher Bob", result["name"]);
            Assert.Equal(45L, (long)result["age"]);
        }
        finally
        {
            try
            {
                if (Directory.Exists(dbPath))
                    Directory.Delete(dbPath, recursive: true);
            }
            catch
            {
                // Ignore teardown races on Windows file handles.
            }
        }
    }
}
