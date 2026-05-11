using BogDb.Core.Main;
using BogDb.Core.Main.QueryResult;
using BogDb.Extensions.FTS;
using Xunit;

namespace BogDb.Tests.Extension;

/// <summary>
/// Tests for the FTS (full-text search) extension.
/// </summary>
[Trait("Category", "FtsExtension")]
public class FtsExtensionTests
{
    // ── FtsTokenizer unit tests ──────────────────────────────────────────

    [Fact]
    public void Tokenizer_SplitsAndLowercases()
    {
        var t = new FtsTokenizer(enableStemming: false);
        var tokens = t.Tokenize("Hello World");
        Assert.Equal(2, tokens.Count);
        Assert.Equal("hello", tokens[0]);
        Assert.Equal("world", tokens[1]);
    }

    [Fact]
    public void Tokenizer_RemovesStopWords()
    {
        var t = new FtsTokenizer(enableStemming: false);
        var tokens = t.Tokenize("the cat is on the mat");
        Assert.DoesNotContain("the", tokens);
        Assert.DoesNotContain("is", tokens);
        Assert.DoesNotContain("on", tokens);
        Assert.Contains("cat", tokens);
        Assert.Contains("mat", tokens);
    }

    [Fact]
    public void Tokenizer_StemsWords()
    {
        var t = new FtsTokenizer(enableStemming: true);
        var tokens = t.Tokenize("running jumps easily");
        // "running" → "run", "jumps" → "jump", "easily" → "easili"
        Assert.Contains("run", tokens);
        Assert.Contains("jump", tokens);
    }

    // ── FtsIndex unit tests ──────────────────────────────────────────────

    [Fact]
    public void FtsIndex_AddAndQuery()
    {
        var idx = new FtsIndex("test", "docs", new[] { "content" });
        idx.AddDocument(0, "the quick brown fox jumps over the lazy dog");
        idx.AddDocument(1, "a fast brown cat leaps over a sleeping hound");
        idx.AddDocument(2, "graph databases handle relationships efficiently");

        var results = idx.Query("brown fox", topK: 10);
        Assert.NotEmpty(results);
        // Doc 0 should rank first (both terms match)
        Assert.Equal(0, results[0].docId);
    }

    [Fact]
    public void FtsIndex_ConjunctiveMode()
    {
        var idx = new FtsIndex("test", "docs", new[] { "content" });
        idx.AddDocument(0, "apple orange banana");
        idx.AddDocument(1, "apple grape");
        idx.AddDocument(2, "orange grape cherry");

        // Conjunctive: both "apple" AND "orange" must match
        var results = idx.Query("apple orange", topK: 10, conjunctive: true);
        Assert.Single(results);
        Assert.Equal(0, results[0].docId);
    }

    [Fact]
    public void FtsIndex_BM25Scoring()
    {
        var idx = new FtsIndex("test", "docs", new[] { "content" });
        // Doc with repeated term should rank higher
        idx.AddDocument(0, "database optimization database tuning database performance");
        idx.AddDocument(1, "general article about software engineering");
        idx.AddDocument(2, "database tutorial for beginners");

        var results = idx.Query("database", topK: 3);
        Assert.True(results.Count >= 2);
        // Doc 0 has "database" 3 times, should rank first
        Assert.Equal(0, results[0].docId);
        Assert.True(results[0].score > results[1].score);
    }

    // ── Scalar functions ─────────────────────────────────────────────────

    [Fact]
    public void Stem_WorksViaQuery()
    {
        using var db = BogDatabase.CreateInMemory();
        new FtsExtension().Load(db);
        using var conn = new BogConnection(db);
        
        var r = conn.Query("RETURN stem('running') AS s");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        Assert.Equal("run", r.GetNext().GetValue(0));
    }

    [Fact]
    public void Tokenize_ReturnsListViaQuery()
    {
        using var db = BogDatabase.CreateInMemory();
        new FtsExtension().Load(db);
        using var conn = new BogConnection(db);
        
        var r = conn.Query("RETURN tokenize('hello world test') AS tokens");
        Assert.True(r.IsSuccess, r.ErrorMessage);
        var val = r.GetNext().GetValue(0);
        var list = Assert.IsType<System.Collections.Generic.List<object?>>(val);
        Assert.Contains("hello", list.Cast<string>());
        Assert.Contains("world", list.Cast<string>());
        Assert.Contains("test", list.Cast<string>());
    }

    // ── Integration: create + query FTS index via Cypher ─────────────────

    [Fact]
    public void CreateAndQueryFtsIndex_EndToEnd()
    {
        using var db = BogDatabase.CreateInMemory();
        var fts = new FtsExtension();
        fts.Load(db);
        using var conn = new BogConnection(db);

        // Create table with text data
        var cr = conn.Query("CREATE NODE TABLE Article(id INT64 PRIMARY KEY, title STRING, body STRING)");
        Assert.True(cr.IsSuccess, cr.ErrorMessage);

        conn.Query("CREATE (:Article {id:1, title:'Graph Databases', body:'Graph databases use nodes and edges to model relationships between entities.'})");
        conn.Query("CREATE (:Article {id:2, title:'SQL Tutorial', body:'SQL is a standard language for accessing and manipulating databases.'})");
        conn.Query("CREATE (:Article {id:3, title:'Machine Learning', body:'Machine learning algorithms build mathematical models from training data.'})");
        conn.Query("CREATE (:Article {id:4, title:'Graph Algorithms', body:'Graph algorithms like PageRank and shortest path operate on graph structures.'})");

        // Create the FTS index programmatically (matches C++ CALL CREATE_FTS_INDEX pattern)
        var index = new FtsIndex("article_idx", "Article", new[] { "title", "body" });

        // Manually populate (in practice, the table function does this)
        var scanResult = conn.Query("MATCH (a:Article) RETURN a.id AS id, a.title AS t, a.body AS b ORDER BY a.id");
        Assert.True(scanResult.IsSuccess, scanResult.ErrorMessage);
        while (scanResult.HasNext())
        {
            var row = scanResult.GetNext();
            var id = System.Convert.ToInt64(row.GetValue(0));
            var text = $"{row.GetValue(1)} {row.GetValue(2)}";
            index.AddDocument(id, text);
        }
        fts.Indexes["article_idx"] = index;

        // Query the index
        var results = index.Query("graph database");
        Assert.NotEmpty(results);
        // Article 1 (graph databases) or Article 4 (graph algorithms) should be top
        Assert.True(results[0].docId == 1 || results[0].docId == 4);
        Assert.True(results[0].score > 0);
    }

    [Fact]
    public void FtsIndex_RemoveDocument()
    {
        var idx = new FtsIndex("test", "docs", new[] { "content" });
        idx.AddDocument(0, "apple orange");
        idx.AddDocument(1, "banana grape");
        
        Assert.Equal(2, idx.DocumentCount);
        idx.RemoveDocument(0);
        Assert.Equal(1, idx.DocumentCount);

        var results = idx.Query("apple");
        Assert.Empty(results);
    }
}
