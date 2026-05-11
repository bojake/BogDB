using System.Collections.Generic;
using BogDb.Core.Catalog;
using BogDb.Core.Common;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Main;

public class DdlQueryExecutionTests
{
    private static void ExecuteWrite(BogConnection connection, Action action)
    {
        connection.BeginWriteTransaction();
        action();
        connection.Commit();
    }

    [Fact]
    public void Query_CreateNodeTable_ExecutesThroughProgrammaticSchemaPath()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        var create = conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)");
        Assert.True(create.IsSuccess, create.ErrorMessage);
        Assert.True(conn.HasTable("Person"));

        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice",
            ["name"] = "Alice"
        }));

        var read = conn.Query("MATCH (p:Person) RETURN p.id, p.name");
        Assert.True(read.IsSuccess, read.ErrorMessage);
        Assert.Equal(1UL, read.GetNumTuples());
        var row = read.GetNext();
        Assert.Equal("alice", row.GetString(0));
        Assert.Equal("Alice", row.GetString(1));
    }

    [Fact]
    public void Query_CreateRelTable_ExecutesThroughProgrammaticSchemaPath()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)").IsSuccess);

        var create = conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)");
        Assert.True(create.IsSuccess, create.ErrorMessage);
        Assert.True(conn.HasTable("KNOWS"));

        ExecuteWrite(conn, () =>
        {
            conn.UpsertNode("Person", "alice", new Dictionary<string, object>
            {
                ["id"] = "alice",
                ["name"] = "Alice"
            });
            conn.UpsertNode("Person", "bob", new Dictionary<string, object>
            {
                ["id"] = "bob",
                ["name"] = "Bob"
            });
            conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object>
            {
                ["since"] = 2024L
            });
        });

        var read = conn.Query("MATCH (a:Person)-[r:KNOWS]->(b:Person) RETURN a.id, b.id, r.since");
        Assert.True(read.IsSuccess, read.ErrorMessage);
        Assert.Equal(1UL, read.GetNumTuples());
        var row = read.GetNext();
        Assert.Equal("alice", row.GetString(0));
        Assert.Equal("bob", row.GetString(1));
        Assert.Equal(2024L, row.GetInt64(2));
    }

    [Fact]
    public void Query_AlterTableAddProperty_ExtendsSchema()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice"
        }));

        var alter = conn.Query("ALTER TABLE Person ADD name STRING");
        Assert.True(alter.IsSuccess, alter.ErrorMessage);

        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice",
            ["name"] = "Alice"
        }));

        var result = conn.Query("MATCH (p:Person) RETURN p.name");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        Assert.Equal("Alice", result.GetNext().GetString(0));
    }

    [Fact]
    public void Query_CreateNodeTable_PreservesDeclaredArrayTypeInCatalog()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        var create = conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])");
        Assert.True(create.IsSuccess, create.ErrorMessage);

        var entry = Assert.IsType<NodeTableCatalogEntry>(
            db.Catalog.GetTableCatalogEntry(null, "Document", useInternal: false));
        var property = entry.GetProperty("embedding");
        Assert.Equal(LogicalTypeID.LIST, property.Type);
        Assert.Equal("FLOAT[]", property.DeclaredType);
        Assert.Equal(LogicalTypeID.FLOAT, property.LeafType);
        Assert.Equal(1, property.ListDepth);
    }

    [Fact]
    public void Query_CreateNode_CoercesDeclaredFloatArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
        var create = conn.Query("CREATE (:Document {id:'doc-1', embedding:[1, 2.5, 3]})");
        Assert.True(create.IsSuccess, create.ErrorMessage);

        var node = conn.ReadNode("Document", "doc-1");
        Assert.NotNull(node);
        var embedding = Assert.IsType<List<object?>>(node["embedding"]);
        Assert.Equal(3, embedding.Count);
        Assert.Equal(1.0f, Assert.IsType<float>(embedding[0]));
        Assert.Equal(2.5f, Assert.IsType<float>(embedding[1]));
        Assert.Equal(3.0f, Assert.IsType<float>(embedding[2]));
    }

    [Fact]
    public void Query_SetProperty_CoercesDeclaredFloatArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
        Assert.True(conn.Query("CREATE (:Document {id:'doc-1', embedding:[1, 2]})").IsSuccess);

        var set = conn.Query("MATCH (d:Document {id:'doc-1'}) SET d.embedding = [4, 5.5, 6] RETURN d.id");
        Assert.True(set.IsSuccess, set.ErrorMessage);

        var node = conn.ReadNode("Document", "doc-1");
        Assert.NotNull(node);
        var embedding = Assert.IsType<List<object?>>(node["embedding"]);
        Assert.Equal(3, embedding.Count);
        Assert.Equal(4.0f, Assert.IsType<float>(embedding[0]));
        Assert.Equal(5.5f, Assert.IsType<float>(embedding[1]));
        Assert.Equal(6.0f, Assert.IsType<float>(embedding[2]));
    }

    [Fact]
    public void Query_CreateNode_CoercesDeclaredInt64ArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Metric(id STRING, readings INT64[])").IsSuccess);
        var create = conn.Query("CREATE (:Metric {id:'m-1', readings:[1, 2.0, 3]})");
        Assert.True(create.IsSuccess, create.ErrorMessage);

        var node = conn.ReadNode("Metric", "m-1");
        Assert.NotNull(node);
        var readings = Assert.IsType<List<object?>>(node["readings"]);
        Assert.Equal(3, readings.Count);
        Assert.Equal(1L, Assert.IsType<long>(readings[0]));
        Assert.Equal(2L, Assert.IsType<long>(readings[1]));
        Assert.Equal(3L, Assert.IsType<long>(readings[2]));
    }

    [Fact]
    public void Query_SetProperty_CoercesDeclaredStringArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Article(id STRING, tags STRING[])").IsSuccess);
        Assert.True(conn.Query("CREATE (:Article {id:'a-1', tags:['seed']})").IsSuccess);

        var set = conn.Query("MATCH (a:Article {id:'a-1'}) SET a.tags = ['graph', 42, true] RETURN a.id");
        Assert.True(set.IsSuccess, set.ErrorMessage);

        var node = conn.ReadNode("Article", "a-1");
        Assert.NotNull(node);
        var tags = Assert.IsType<List<object?>>(node["tags"]);
        Assert.Equal(3, tags.Count);
        Assert.Equal("graph", Assert.IsType<string>(tags[0]));
        Assert.Equal("42", Assert.IsType<string>(tags[1]));
        Assert.Equal("True", Assert.IsType<string>(tags[2]));
    }

    [Fact]
    public void Query_CreateNode_CoercesDeclaredNestedFloatArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Tensor(id STRING, embedding FLOAT[][])").IsSuccess);
        var create = conn.Query("CREATE (:Tensor {id:'t-1', embedding:[[1, 2.5], [3, 4]]})");
        Assert.True(create.IsSuccess, create.ErrorMessage);

        var node = conn.ReadNode("Tensor", "t-1");
        Assert.NotNull(node);
        var embedding = Assert.IsType<List<object?>>(node["embedding"]);
        Assert.Equal(2, embedding.Count);
        var first = Assert.IsType<List<object?>>(embedding[0]);
        var second = Assert.IsType<List<object?>>(embedding[1]);
        Assert.Equal(1.0f, Assert.IsType<float>(first[0]));
        Assert.Equal(2.5f, Assert.IsType<float>(first[1]));
        Assert.Equal(3.0f, Assert.IsType<float>(second[0]));
        Assert.Equal(4.0f, Assert.IsType<float>(second[1]));
    }

    [Fact]
    public void Query_SetProperty_RejectsScalarForDeclaredFloatArray()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
        Assert.True(conn.Query("CREATE (:Document {id:'doc-1', embedding:[1, 2]})").IsSuccess);

        var set = conn.Query("MATCH (d:Document {id:'doc-1'}) SET d.embedding = 'oops' RETURN d.id");
        Assert.False(set.IsSuccess);
        Assert.NotEmpty(set.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public void Query_MergeNode_CoercesDeclaredFloatArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);

        var merge = conn.Query("MERGE (:Document {id:'doc-1', embedding:[1, 2.5, 3]})");
        Assert.True(merge.IsSuccess, merge.ErrorMessage);

        var node = conn.ReadNode("Document", "doc-1");
        Assert.NotNull(node);
        var embedding = Assert.IsType<List<object?>>(node["embedding"]);
        Assert.Equal(3, embedding.Count);
        Assert.Equal(1.0f, Assert.IsType<float>(embedding[0]));
        Assert.Equal(2.5f, Assert.IsType<float>(embedding[1]));
        Assert.Equal(3.0f, Assert.IsType<float>(embedding[2]));
    }

    [Fact]
    public void Query_MergeNode_OnMatchSetPropertyBag_CoercesDeclaredFloatArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
        Assert.True(conn.Query("CREATE (:Document {id:'doc-1', embedding:[1, 2]})").IsSuccess);

        var merge = conn.Query("MERGE (d:Document {id:'doc-1'}) ON MATCH SET d = {embedding:[4, 5.5, 6]}");
        Assert.True(merge.IsSuccess, merge.ErrorMessage);

        var node = conn.ReadNode("Document", "doc-1");
        Assert.NotNull(node);
        var embedding = Assert.IsType<List<object?>>(node["embedding"]);
        Assert.Equal(3, embedding.Count);
        Assert.Equal(4.0f, Assert.IsType<float>(embedding[0]));
        Assert.Equal(5.5f, Assert.IsType<float>(embedding[1]));
        Assert.Equal(6.0f, Assert.IsType<float>(embedding[2]));
    }

    [Fact]
    public void Query_MergeNode_OnMatchSetPropertyBag_CoercesDeclaredNestedFloatArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Tensor(id STRING, embedding FLOAT[][])").IsSuccess);
        Assert.True(conn.Query("CREATE (:Tensor {id:'t-1', embedding:[[1, 2]]})").IsSuccess);

        var merge = conn.Query("MERGE (t:Tensor {id:'t-1'}) ON MATCH SET t = {embedding:[[4, 5.5], [6, 7]]}");
        Assert.True(merge.IsSuccess, merge.ErrorMessage);

        var node = conn.ReadNode("Tensor", "t-1");
        Assert.NotNull(node);
        var embedding = Assert.IsType<List<object?>>(node["embedding"]);
        var first = Assert.IsType<List<object?>>(embedding[0]);
        var second = Assert.IsType<List<object?>>(embedding[1]);
        Assert.Equal(4.0f, Assert.IsType<float>(first[0]));
        Assert.Equal(5.5f, Assert.IsType<float>(first[1]));
        Assert.Equal(6.0f, Assert.IsType<float>(second[0]));
        Assert.Equal(7.0f, Assert.IsType<float>(second[1]));
    }

    [Fact]
    public void Query_MergeRelationship_CoercesDeclaredFloatArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE LINKS(FROM Document TO Document, weights FLOAT[])").IsSuccess);
        Assert.True(conn.Query("CREATE (:Document {id:'doc-1'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Document {id:'doc-2'})").IsSuccess);

        var merge = conn.Query(
            "MATCH (a:Document {id:'doc-1'}), (b:Document {id:'doc-2'}) " +
            "MERGE (a)-[r:LINKS {weights:[1, 2.5, 3]}]->(b)");
        Assert.True(merge.IsSuccess, merge.ErrorMessage);

        var read = conn.Query(
            "MATCH (:Document {id:'doc-1'})-[r:LINKS]->(:Document {id:'doc-2'}) RETURN r.weights");
        Assert.True(read.IsSuccess, read.ErrorMessage);
        Assert.Equal(1UL, read.GetNumTuples());
        var embedding = Assert.IsType<List<object?>>(read.GetNext().GetValue(0));
        Assert.Equal(3, embedding.Count);
        Assert.Equal(1.0f, Assert.IsType<float>(embedding[0]));
        Assert.Equal(2.5f, Assert.IsType<float>(embedding[1]));
        Assert.Equal(3.0f, Assert.IsType<float>(embedding[2]));
    }

    [Fact]
    public void ProgrammaticUpsert_CoercesDeclaredFloatArrayElements()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
        ExecuteWrite(conn, () => conn.UpsertNode("Document", "doc-1", new Dictionary<string, object>
        {
            ["id"] = "doc-1",
            ["embedding"] = new List<object?> { 1, 2.5d, 3L }
        }));

        var node = conn.ReadNode("Document", "doc-1");
        Assert.NotNull(node);
        var embedding = Assert.IsType<List<object?>>(node["embedding"]);
        Assert.Equal(1.0f, Assert.IsType<float>(embedding[0]));
        Assert.Equal(2.5f, Assert.IsType<float>(embedding[1]));
        Assert.Equal(3.0f, Assert.IsType<float>(embedding[2]));
    }

    [Fact]
    public void ProgrammaticUpsert_RejectsScalarForDeclaredFloatArray()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExecuteWrite(conn, () => conn.UpsertNode("Document", "doc-1", new Dictionary<string, object>
            {
                ["id"] = "doc-1",
                ["embedding"] = "oops"
            })));

        Assert.Contains("embedding", ex.Message);
        Assert.Contains("FLOAT[]", ex.Message);
    }

    [Fact]
    public void DirectReadNode_NormalizesDeclaredFloatArrayElements_FromRawStorageValues()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);

        conn.BeginWriteTransaction();
        var tx = conn.ClientContext.ActiveTransaction!;
        db.NodeTables["Document"].Upsert(tx, "doc-1", new Dictionary<string, object>
        {
            ["id"] = "doc-1",
            ["embedding"] = new List<object?> { 1L, 2L, 3L }
        });

        var node = conn.ReadNode("Document", "doc-1");
        Assert.NotNull(node);
        var embedding = Assert.IsType<List<object?>>(node!["embedding"]);
        Assert.Equal(1.0f, Assert.IsType<float>(embedding[0]));
        Assert.Equal(2.0f, Assert.IsType<float>(embedding[1]));
        Assert.Equal(3.0f, Assert.IsType<float>(embedding[2]));
        conn.Rollback();
    }

    [Fact]
    public void DirectGetNodeById_NormalizesDeclaredFloatArrayElements_FromRawStorageValues()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);

        conn.BeginWriteTransaction();
        var tx = conn.ClientContext.ActiveTransaction!;
        db.NodeTables["Document"].Upsert(tx, "doc-1", new Dictionary<string, object>
        {
            ["id"] = "doc-1",
            ["embedding"] = new List<object?> { 4L, 5L, 6L }
        });

        var node = conn.GetNodeById("doc-1", out var tableName);
        Assert.Equal("Document", tableName);
        Assert.NotNull(node);
        var embedding = Assert.IsType<List<object?>>(node!["embedding"]);
        Assert.Equal(4.0f, Assert.IsType<float>(embedding[0]));
        Assert.Equal(5.0f, Assert.IsType<float>(embedding[1]));
        Assert.Equal(6.0f, Assert.IsType<float>(embedding[2]));
        conn.Rollback();
    }

    [Fact]
    public void Query_NodeScan_NormalizesDeclaredFloatArrayElements_FromRawStorageValues()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);

        conn.BeginWriteTransaction();
        var tx = conn.ClientContext.ActiveTransaction!;
        db.NodeTables["Document"].Upsert(tx, "doc-1", new Dictionary<string, object>
        {
            ["id"] = "doc-1",
            ["embedding"] = new List<object?> { 7L, 8L, 9L }
        });
        conn.Commit();

        var result = conn.Query("MATCH (d:Document {id:'doc-1'}) RETURN d.embedding");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        var embedding = Assert.IsType<List<object?>>(result.GetNext().GetValue(0));
        Assert.Equal(7.0f, Assert.IsType<float>(embedding[0]));
        Assert.Equal(8.0f, Assert.IsType<float>(embedding[1]));
        Assert.Equal(9.0f, Assert.IsType<float>(embedding[2]));
    }

    [Fact]
    public void Query_NodeScan_NormalizesDeclaredNestedFloatArrayElements_FromRawStorageValues()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Tensor(id STRING, embedding FLOAT[][])").IsSuccess);

        conn.BeginWriteTransaction();
        var tx = conn.ClientContext.ActiveTransaction!;
        db.NodeTables["Tensor"].Upsert(tx, "t-1", new Dictionary<string, object>
        {
            ["id"] = "t-1",
            ["embedding"] = new List<object?>
            {
                new List<object?> { 1L, 2L },
                new List<object?> { 3L, 4L }
            }
        });
        conn.Commit();

        var result = conn.Query("MATCH (t:Tensor {id:'t-1'}) RETURN t.embedding");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        var embedding = Assert.IsType<List<object?>>(result.GetNext().GetValue(0));
        var first = Assert.IsType<List<object?>>(embedding[0]);
        var second = Assert.IsType<List<object?>>(embedding[1]);
        Assert.Equal(1.0f, Assert.IsType<float>(first[0]));
        Assert.Equal(2.0f, Assert.IsType<float>(first[1]));
        Assert.Equal(3.0f, Assert.IsType<float>(second[0]));
        Assert.Equal(4.0f, Assert.IsType<float>(second[1]));
    }

    [Fact]
    public void Query_RelTraversal_NormalizesDeclaredFloatArrayElements_FromRawStorageValues()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE LINKS(FROM Document TO Document, weights FLOAT[])").IsSuccess);
        Assert.True(conn.Query("CREATE (:Document {id:'doc-1'})").IsSuccess);
        Assert.True(conn.Query("CREATE (:Document {id:'doc-2'})").IsSuccess);

        conn.BeginWriteTransaction();
        var tx = conn.ClientContext.ActiveTransaction!;
        db.RelTables["LINKS"].Upsert(tx, new EdgeKey("doc-1", "doc-2"), new Dictionary<string, object>
        {
            ["weights"] = new List<object?> { 10L, 11L, 12L }
        });
        conn.Commit();

        var result = conn.Query(
            "MATCH (:Document {id:'doc-1'})-[r:LINKS]->(:Document {id:'doc-2'}) RETURN r.weights");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        var weights = Assert.IsType<List<object?>>(result.GetNext().GetValue(0));
        Assert.Equal(10.0f, Assert.IsType<float>(weights[0]));
        Assert.Equal(11.0f, Assert.IsType<float>(weights[1]));
        Assert.Equal(12.0f, Assert.IsType<float>(weights[2]));
    }

    [Fact]
    public void Query_AlterTableAddPropertyDefault_BackfillsExistingRows_AndAppliesToFutureWrites()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice" }));

        var alter = conn.Query("ALTER TABLE Person ADD age INT64 DEFAULT 42");
        Assert.True(alter.IsSuccess, alter.ErrorMessage);

        var existing = conn.Query("MATCH (p:Person) RETURN p.age");
        Assert.True(existing.IsSuccess, existing.ErrorMessage);
        Assert.Equal(1UL, existing.GetNumTuples());
        Assert.Equal(42L, existing.GetNext().GetInt64(0));

        ExecuteWrite(conn, () => conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob" }));
        var future = conn.Query("MATCH (p:Person) RETURN p.id, p.age ORDER BY p.id");
        Assert.True(future.IsSuccess, future.ErrorMessage);
        Assert.Equal(2UL, future.GetNumTuples());
        var first = future.GetNext();
        var second = future.GetNext();
        Assert.Equal("alice", first.GetString(0));
        Assert.Equal(42L, first.GetInt64(1));
        Assert.Equal("bob", second.GetString(0));
        Assert.Equal(42L, second.GetInt64(1));
    }

    [Fact]
    public void Query_DropTable_RemovesNodeTableAndAssociatedIndexMetadata()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)").IsSuccess);
        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice",
            ["name"] = "Alice"
        }));
        conn.CreateIndex("Person", "name");

        var drop = conn.Query("DROP TABLE Person");
        Assert.True(drop.IsSuccess, drop.ErrorMessage);
        Assert.False(conn.HasTable("Person"));

        var read = conn.Query("MATCH (p:Person) RETURN p.id");
        Assert.False(read.IsSuccess);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, nick STRING)").IsSuccess);
        conn.CreateIndex("Person", "nick");
        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice",
            ["nick"] = "ally"
        }));

        var recreated = conn.Query("MATCH (p:Person) RETURN p.nick");
        Assert.True(recreated.IsSuccess, recreated.ErrorMessage);
        Assert.Equal(1UL, recreated.GetNumTuples());
        Assert.Equal("ally", recreated.GetNext().GetString(0));
    }

    [Fact]
    public void Query_DropTable_RejectsNodeTableReferencedByRelationshipTable()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)").IsSuccess);

        var drop = conn.Query("DROP TABLE Person");
        Assert.False(drop.IsSuccess);
        Assert.Contains("still references it", drop.ErrorMessage);
        Assert.True(conn.HasTable("Person"));
        Assert.True(conn.HasTable("KNOWS"));
    }

    [Fact]
    public void Query_DropTable_RemovesRelationshipTable()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)").IsSuccess);

        var drop = conn.Query("DROP TABLE KNOWS");
        Assert.True(drop.IsSuccess, drop.ErrorMessage);
        Assert.False(conn.HasTable("KNOWS"));
        Assert.True(conn.HasTable("Person"));
    }

    [Fact]
    public void Query_AlterTableDropProperty_RemovesPropertyAndIndexMetadata()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)").IsSuccess);
        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice",
            ["name"] = "Alice"
        }));
        conn.CreateIndex("Person", "name");

        var alter = conn.Query("ALTER TABLE Person DROP name");
        Assert.True(alter.IsSuccess, alter.ErrorMessage);

        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice"
        }));

        var result = conn.Query("MATCH (p:Person) RETURN p.id");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1UL, result.GetNumTuples());
        Assert.Equal("alice", result.GetNext().GetString(0));
    }

    [Fact]
    public void Query_AlterTableRenameProperty_ReturnsExplicitUnsupportedError()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)").IsSuccess);

        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice",
            ["name"] = "Alice"
        }));
        conn.CreateIndex("Person", "name");

        var result = conn.Query("ALTER TABLE Person RENAME name TO nick");
        Assert.True(result.IsSuccess, result.ErrorMessage);

        var query = conn.Query("MATCH (p:Person) RETURN p.nick");
        Assert.True(query.IsSuccess, query.ErrorMessage);
        Assert.Equal(1UL, query.GetNumTuples());
        Assert.Equal("Alice", query.GetNext().GetString(0));
    }

    [Fact]
    public void Query_AlterTableRenameTable_MovesRuntimeAndCatalogState()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING, name STRING)").IsSuccess);
        ExecuteWrite(conn, () => conn.UpsertNode("Person", "alice", new Dictionary<string, object>
        {
            ["id"] = "alice",
            ["name"] = "Alice"
        }));
        conn.CreateIndex("Person", "name");

        var rename = conn.Query("ALTER TABLE Person RENAME TO Human");
        Assert.True(rename.IsSuccess, rename.ErrorMessage);
        Assert.False(conn.HasTable("Person"));
        Assert.True(conn.HasTable("Human"));

        var query = conn.Query("MATCH (h:Human) RETURN h.name");
        Assert.True(query.IsSuccess, query.ErrorMessage);
        Assert.Equal(1UL, query.GetNumTuples());
        Assert.Equal("Alice", query.GetNext().GetString(0));
    }

    [Fact]
    public void Query_AlterTableRenameTable_UpdatesRelationshipEndpoints()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)").IsSuccess);
        ExecuteWrite(conn, () =>
        {
            conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice" });
            conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob" });
            conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object> { ["since"] = 2024L });
        });

        var rename = conn.Query("ALTER TABLE Person RENAME TO Human");
        Assert.True(rename.IsSuccess, rename.ErrorMessage);

        var query = conn.Query("MATCH (a:Human)-[r:KNOWS]->(b:Human) RETURN a.id, b.id, r.since");
        Assert.True(query.IsSuccess, query.ErrorMessage);
        Assert.Equal(1UL, query.GetNumTuples());
        var row = query.GetNext();
        Assert.Equal("alice", row.GetString(0));
        Assert.Equal("bob", row.GetString(1));
        Assert.Equal(2024L, row.GetInt64(2));
    }

    [Fact]
    public void Query_AlterTableConnectionChange_AddsConnectionMetadata_AndSupportsMixedEndpointTraversal()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE NODE TABLE Company(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)").IsSuccess);
        ExecuteWrite(conn, () =>
        {
            conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice" });
            conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob" });
            conn.UpsertNode("Company", "acme", new Dictionary<string, object> { ["id"] = "acme" });
            conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object> { ["since"] = 2020L });
        });

        var result = conn.Query("ALTER TABLE KNOWS ADD FROM Person TO Company");
        Assert.True(result.IsSuccess, result.ErrorMessage);

        ExecuteWrite(conn, () => conn.UpsertRelationship("KNOWS", "alice", "acme", new Dictionary<string, object> { ["since"] = 2024L }));

        var relEntry = Assert.IsType<BogDb.Core.Catalog.RelGroupCatalogEntry>(
            db.Catalog.GetTableCatalogEntry(null, "KNOWS", useInternal: false));
        Assert.Equal(2, relEntry.GetConnections().Count);

        var query = conn.Query("MATCH (p:Person)-[r:KNOWS]->(c:Company) RETURN p.id, c.id, r.since");
        Assert.True(query.IsSuccess, query.ErrorMessage);
        Assert.Equal(1UL, query.GetNumTuples());
        var row = query.GetNext();
        Assert.Equal("alice", row.GetString(0));
        Assert.Equal("acme", row.GetString(1));
        Assert.Equal(2024L, row.GetInt64(2));
    }

    [Fact]
    public void Query_AlterTableConnectionChange_DropRemovesMatchingEdges_AndReleasesNodeReference()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE NODE TABLE Company(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)").IsSuccess);
        Assert.True(conn.Query("ALTER TABLE KNOWS ADD FROM Person TO Company").IsSuccess);

        ExecuteWrite(conn, () =>
        {
            conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice" });
            conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob" });
            conn.UpsertNode("Company", "acme", new Dictionary<string, object> { ["id"] = "acme" });
            conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object> { ["since"] = 2020L });
            conn.UpsertRelationship("KNOWS", "alice", "acme", new Dictionary<string, object> { ["since"] = 2024L });
        });

        var drop = conn.Query("ALTER TABLE KNOWS DROP FROM Person TO Company");
        Assert.True(drop.IsSuccess, drop.ErrorMessage);

        var personCompany = conn.Query("MATCH (p:Person)-[r:KNOWS]->(c:Company) RETURN COUNT(*)");
        Assert.True(personCompany.IsSuccess, personCompany.ErrorMessage);
        Assert.Equal(0L, personCompany.GetNext().GetInt64(0));

        var personPerson = conn.Query("MATCH (p:Person)-[r:KNOWS]->(q:Person) RETURN COUNT(*)");
        Assert.True(personPerson.IsSuccess, personPerson.ErrorMessage);
        Assert.Equal(1L, personPerson.GetNext().GetInt64(0));

        var dropCompany = conn.Query("DROP TABLE Company");
        Assert.True(dropCompany.IsSuccess, dropCompany.ErrorMessage);
    }

    [Fact]
    public void Query_AlterTableConnectionChange_HonorsIfExistsAndIfNotExists()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE User(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE NODE TABLE Celebrity(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE Follows(FROM User TO User)").IsSuccess);

        Assert.True(conn.Query("ALTER TABLE Follows ADD FROM User TO Celebrity").IsSuccess);
        Assert.True(conn.Query("ALTER TABLE Follows ADD IF NOT EXISTS FROM User TO Celebrity").IsSuccess);
        Assert.True(conn.Query("ALTER TABLE Follows DROP FROM User TO Celebrity").IsSuccess);
        Assert.True(conn.Query("ALTER TABLE Follows DROP IF EXISTS FROM User TO Celebrity").IsSuccess);
    }

    [Fact]
    public void Query_AlterTableConnectionChange_RecursiveTraversal_UsesMixedEndpointConnections()
    {
        using var db = BogDatabase.CreateInMemory();
        using var conn = new BogConnection(db);

        Assert.True(conn.Query("CREATE NODE TABLE Person(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE NODE TABLE Company(id STRING)").IsSuccess);
        Assert.True(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person, since INT64)").IsSuccess);
        Assert.True(conn.Query("ALTER TABLE KNOWS ADD FROM Person TO Company").IsSuccess);

        ExecuteWrite(conn, () =>
        {
            conn.UpsertNode("Person", "alice", new Dictionary<string, object> { ["id"] = "alice" });
            conn.UpsertNode("Person", "bob", new Dictionary<string, object> { ["id"] = "bob" });
            conn.UpsertNode("Company", "acme", new Dictionary<string, object> { ["id"] = "acme" });
            conn.UpsertRelationship("KNOWS", "alice", "bob", new Dictionary<string, object> { ["since"] = 2020L });
            conn.UpsertRelationship("KNOWS", "bob", "acme", new Dictionary<string, object> { ["since"] = 2024L });
        });

        var query = conn.Query(
            "MATCH (p:Person)-[r:KNOWS*1..2]->(c:Company) WHERE p.id = 'alice' RETURN c.id");
        Assert.True(query.IsSuccess, query.ErrorMessage);
        Assert.Equal(1UL, query.GetNumTuples());
        Assert.Equal("acme", query.GetNext().GetString(0));
    }
}
