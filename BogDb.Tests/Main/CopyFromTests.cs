// BogDb.Tests/Main/CopyFromTests.cs
// Focused unit tests for G-013 Tier B: COPY FROM CSV ingestion.
//
// These tests are written against the expected post-Tier-B engine behavior:
//   - CopyNode.cs reads CSV header + data rows and inserts into the node table
//   - CopyRel.cs reads CSV header, resolves from_id/to_id via hash index, inserts rel rows
//   - BindCopyFrom() validates column names against the catalog schema
//
// Tests are structured so that:
//   1. The setup (schema + node inserts) uses the already-working programmatic path
//   2. The COPY FROM statement is issued via conn.Query()
//   3. Readback queries validate the loaded data
//
// Current status: tests will FAIL until the engine Tier B implementation lands.
// They are written to be run as-is and flip green when the engine work is complete.
// Do NOT skip them — allow them to fail as a clear signal of engine readiness.

using System.Collections.Generic;
using System.IO;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public sealed class CopyFromTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string WriteTempCsv(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path.Replace('\\', '/');
    }

    private static BogDatabase OpenDb() => BogDatabase.Open(":memory:");

    // ── Node table COPY FROM ──────────────────────────────────────────────────

    [Fact]
    public void CopyFrom_NodeTable_LoadsAllRows()
    {
        // Arrange
        var csvPath = WriteTempCsv("id,name,age\n1,Alice,30\n2,Bob,25\n3,Charlie,35\n");

        using var db   = OpenDb();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id",   LogicalTypeID.INT64  },
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  },
        });
        conn.Commit();

        // Act — COPY FROM (Tier B engine path)
        var copyResult = conn.Query($"COPY Person FROM '{csvPath}'");

        try
        {
            // Assert — COPY reported success
            Assert.True(copyResult.IsSuccess, $"COPY FROM failed: {copyResult.ErrorMessage}");

            // Assert — row count
            var countResult = conn.Query("MATCH (p:Person) RETURN count(p) AS n");
            Assert.True(countResult.IsSuccess);
            Assert.Equal(3L, countResult.GetNext().GetInt64(0));

            // Assert — primary key values present
            var idResult = conn.Query("MATCH (p:Person) RETURN p.id ORDER BY p.id");
            Assert.True(idResult.IsSuccess);
            Assert.Equal(1L, idResult.GetNext().GetInt64(0));
            Assert.Equal(2L, idResult.GetNext().GetInt64(0));
            Assert.Equal(3L, idResult.GetNext().GetInt64(0));
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    [Fact]
    public void CopyFrom_NodeTable_StringColumnLoadedCorrectly()
    {
        var csvPath = WriteTempCsv("id,name,age\n1,Alice,30\n2,Bob,25\n");

        using var db   = OpenDb();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id",   LogicalTypeID.INT64  },
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  },
        });
        conn.Commit();

        conn.Query($"COPY Person FROM '{csvPath}'");

        try
        {
            var result = conn.Query("MATCH (p:Person) RETURN p.name ORDER BY p.id");
            Assert.True(result.IsSuccess);
            Assert.Equal("Alice", result.GetNext().GetString(0));
            Assert.Equal("Bob",   result.GetNext().GetString(0));
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    [Fact]
    public void CopyFrom_NodeTable_IntegerColumnLoadedCorrectly()
    {
        var csvPath = WriteTempCsv("id,name,age\n10,Zara,22\n20,Uma,45\n");

        using var db   = OpenDb();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id",   LogicalTypeID.INT64  },
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  },
        });
        conn.Commit();

        conn.Query($"COPY Person FROM '{csvPath}'");

        try
        {
            var result = conn.Query("MATCH (p:Person) RETURN p.age ORDER BY p.id");
            Assert.True(result.IsSuccess);
            Assert.Equal(22L, result.GetNext().GetInt64(0));
            Assert.Equal(45L, result.GetNext().GetInt64(0));
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    [Fact]
    public void CopyFrom_NodeTable_FilterWorksOverLoadedData()
    {
        var csvPath = WriteTempCsv("id,name,age\n1,Alice,30\n2,Bob,25\n3,Charlie,35\n4,Diana,28\n");

        using var db   = OpenDb();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id",   LogicalTypeID.INT64  },
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  },
        });
        conn.Commit();

        conn.Query($"COPY Person FROM '{csvPath}'");

        try
        {
            // Only persons with age > 28 should appear: Alice (30), Charlie (35)
            var result = conn.Query(
                "MATCH (p:Person) WHERE p.age > 28 RETURN p.name ORDER BY p.name");
            Assert.True(result.IsSuccess);
            Assert.Equal("Alice",   result.GetNext().GetString(0));
            Assert.Equal("Charlie", result.GetNext().GetString(0));
            Assert.False(result.HasNext());
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    // ── Relationship table COPY FROM ──────────────────────────────────────────

    [Fact]
    public void CopyFrom_RelTable_LoadsAllEdges()
    {
        // persons.csv: id,name,age
        // knows.csv:   from_id,to_id,since
        var personCsv = WriteTempCsv("id,name,age\n1,Alice,30\n2,Bob,25\n3,Charlie,35\n");
        var knowsCsv  = WriteTempCsv("from_id,to_id,since\n1,2,2020\n2,3,2021\n");

        using var db   = OpenDb();
        using var conn = new BogConnection(db);

        // Set up schema and load nodes first (programmatic, confirmed working)
        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id",   LogicalTypeID.INT64  },
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  },
        });
        conn.EnsureRelTable("Knows", "Person", "Person", new Dictionary<string, LogicalTypeID>
        {
            { "since", LogicalTypeID.INT64 },
        });
        conn.Commit();

        // Load nodes via COPY (Tier B)
        var nodeResult = conn.Query($"COPY Person FROM '{personCsv}'");

        try
        {
            Assert.True(nodeResult.IsSuccess, $"Node COPY failed: {nodeResult.ErrorMessage}");

            // Load relationships via COPY (Tier B)
            var relResult = conn.Query($"COPY Knows FROM '{knowsCsv}'");
            Assert.True(relResult.IsSuccess, $"Rel COPY failed: {relResult.ErrorMessage}");

            // Verify edge count
            var countResult = conn.Query("MATCH ()-[k:Knows]->() RETURN count(k) AS n");
            Assert.True(countResult.IsSuccess);
            Assert.Equal(2L, countResult.GetNext().GetInt64(0));
        }
        finally
        {
            File.Delete(personCsv);
            File.Delete(knowsCsv);
        }
    }

    [Fact]
    public void CopyFrom_RelTable_TraversalReturnsCorrectEndpoints()
    {
        var personCsv = WriteTempCsv("id,name,age\n1,Alice,30\n2,Bob,25\n3,Charlie,35\n");
        var knowsCsv  = WriteTempCsv("from_id,to_id,since\n1,2,2020\n2,3,2021\n");

        using var db   = OpenDb();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id",   LogicalTypeID.INT64  },
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  },
        });
        conn.EnsureRelTable("Knows", "Person", "Person", new Dictionary<string, LogicalTypeID>
        {
            { "since", LogicalTypeID.INT64 },
        });
        conn.Commit();

        conn.Query($"COPY Person FROM '{personCsv}'");
        conn.Query($"COPY Knows FROM '{knowsCsv}'");

        try
        {
            var result = conn.Query(
                "MATCH (a:Person)-[:Knows]->(b:Person) RETURN a.id, b.id ORDER BY a.id");
            Assert.True(result.IsSuccess);

            // 1→2
            var row1 = result.GetNext();
            Assert.Equal(1L, row1.GetInt64(0));
            Assert.Equal(2L, row1.GetInt64(1));

            // 2→3
            var row2 = result.GetNext();
            Assert.Equal(2L, row2.GetInt64(0));
            Assert.Equal(3L, row2.GetInt64(1));

            Assert.False(result.HasNext());
        }
        finally
        {
            File.Delete(personCsv);
            File.Delete(knowsCsv);
        }
    }

    [Fact]
    public void CopyFrom_RelTable_PropertyLoadedCorrectly()
    {
        var personCsv = WriteTempCsv("id,name,age\n1,Alice,30\n2,Bob,25\n3,Charlie,35\n");
        var knowsCsv  = WriteTempCsv("from_id,to_id,since\n1,2,2020\n2,3,2021\n");

        using var db   = OpenDb();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id",   LogicalTypeID.INT64  },
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  },
        });
        conn.EnsureRelTable("Knows", "Person", "Person", new Dictionary<string, LogicalTypeID>
        {
            { "since", LogicalTypeID.INT64 },
        });
        conn.Commit();

        conn.Query($"COPY Person FROM '{personCsv}'");
        conn.Query($"COPY Knows FROM '{knowsCsv}'");

        try
        {
            var result = conn.Query(
                "MATCH (a:Person)-[k:Knows]->(b:Person) RETURN a.id, b.id, k.since ORDER BY a.id");
            Assert.True(result.IsSuccess);

            var row1 = result.GetNext();
            Assert.Equal(1L,    row1.GetInt64(0)); // a.id
            Assert.Equal(2L,    row1.GetInt64(1)); // b.id
            Assert.Equal(2020L, row1.GetInt64(2)); // k.since

            var row2 = result.GetNext();
            Assert.Equal(2L,    row2.GetInt64(0));
            Assert.Equal(3L,    row2.GetInt64(1));
            Assert.Equal(2021L, row2.GetInt64(2));
        }
        finally
        {
            File.Delete(personCsv);
            File.Delete(knowsCsv);
        }
    }

    [Fact]
    public void CopyFrom_RelTable_FilterOnRelProperty_Works()
    {
        var personCsv = WriteTempCsv("id,name,age\n1,Alice,30\n2,Bob,25\n3,Charlie,35\n4,Diana,28\n");
        var knowsCsv  = WriteTempCsv("from_id,to_id,since\n1,2,2020\n2,3,2021\n3,4,2022\n1,4,2019\n");

        using var db   = OpenDb();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id",   LogicalTypeID.INT64  },
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  },
        });
        conn.EnsureRelTable("Knows", "Person", "Person", new Dictionary<string, LogicalTypeID>
        {
            { "since", LogicalTypeID.INT64 },
        });
        conn.Commit();

        conn.Query($"COPY Person FROM '{personCsv}'");
        conn.Query($"COPY Knows FROM '{knowsCsv}'");

        try
        {
            // Only edges with since >= 2021: (2,3,2021), (3,4,2022)
            var result = conn.Query(
                "MATCH (a:Person)-[k:Knows]->(b:Person) WHERE k.since >= 2021 " +
                "RETURN a.id, b.id ORDER BY a.id, b.id");
            Assert.True(result.IsSuccess);

            var row1 = result.GetNext();
            Assert.Equal(2L, row1.GetInt64(0));
            Assert.Equal(3L, row1.GetInt64(1));

            var row2 = result.GetNext();
            Assert.Equal(3L, row2.GetInt64(0));
            Assert.Equal(4L, row2.GetInt64(1));

            Assert.False(result.HasNext());
        }
        finally
        {
            File.Delete(personCsv);
            File.Delete(knowsCsv);
        }
    }

    // ── Stable error surface ──────────────────────────────────────────────────

    [Fact]
    public void CopyFrom_NonexistentFile_ReturnsError()
    {
        // This error surface is stable regardless of Tier B completion:
        // the engine must return an error (not throw) when the file does not exist.
        using var db   = OpenDb();
        using var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Person", new Dictionary<string, LogicalTypeID>
        {
            { "id",   LogicalTypeID.INT64  },
            { "name", LogicalTypeID.STRING },
            { "age",  LogicalTypeID.INT64  },
        });
        conn.Commit();

        var result = conn.Query("COPY Person FROM '/tmp/this_file_does_not_exist_bogdb_test.csv'");

        // Either IsSuccess=false OR an exception was caught and surfaced as an error message.
        // The golden harness accepts either form; we just require the engine does not crash.
        Assert.False(result.IsSuccess,
            "Expected COPY FROM a nonexistent file to fail, but it reported success.");
    }
}
