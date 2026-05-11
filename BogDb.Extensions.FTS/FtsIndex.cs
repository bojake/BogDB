using System;
using System.Collections.Generic;
using System.Linq;

namespace BogDb.Extensions.FTS;

/// <summary>
/// In-memory inverted index with BM25 scoring.
/// C++ parity: extension/fts/src/index/fts_index.cpp
/// </summary>
public sealed class FtsIndex
{
    /// <summary>Name of this index (used in CALL/DROP).</summary>
    public string Name { get; }

    /// <summary>Table name this index covers.</summary>
    public string TableName { get; }

    /// <summary>Property names indexed.</summary>
    public IReadOnlyList<string> Properties { get; }

    // BM25 parameters
    private const double K1 = 1.2;
    private const double B  = 0.75;

    // Inverted index: term → list of (docId, termFrequency)
    private readonly Dictionary<string, List<(long docId, int tf)>> _postings = new();

    // Positional index: (docId, term) → list of positions (for phrase queries)
    private readonly Dictionary<(long docId, string term), List<int>> _positions = new();

    // Per-document: docId → document length (total tokens)
    private readonly Dictionary<long, int> _docLengths = new();

    // Total documents indexed
    private int _totalDocs;
    private double _avgDocLength;

    private readonly FtsTokenizer _tokenizer;

    public FtsIndex(string name, string tableName, IReadOnlyList<string> properties, FtsTokenizer? tokenizer = null)
    {
        Name = name;
        TableName = tableName;
        Properties = properties;
        _tokenizer = tokenizer ?? new FtsTokenizer();
    }

    /// <summary>
    /// Adds a document to the index.
    /// </summary>
    /// <param name="docId">Internal BogDb row ID (offset).</param>
    /// <param name="text">Concatenated text of all indexed properties.</param>
    public void AddDocument(long docId, string text)
    {
        var tokens = _tokenizer.Tokenize(text);
        _docLengths[docId] = tokens.Count;

        // Count term frequencies and record positions for this doc
        var termCounts = new Dictionary<string, int>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            termCounts.TryGetValue(token, out var c);
            termCounts[token] = c + 1;

            // Record positional data for phrase queries
            var posKey = (docId, token);
            if (!_positions.TryGetValue(posKey, out var posList))
            {
                posList = new List<int>();
                _positions[posKey] = posList;
            }
            posList.Add(i);
        }

        // Add to postings
        foreach (var (term, tf) in termCounts)
        {
            if (!_postings.TryGetValue(term, out var list))
            {
                list = new List<(long, int)>();
                _postings[term] = list;
            }
            list.Add((docId, tf));
        }

        _totalDocs++;
        _avgDocLength = _docLengths.Values.Average();
    }

    /// <summary>
    /// Removes a document from the index.
    /// </summary>
    public void RemoveDocument(long docId)
    {
        foreach (var list in _postings.Values)
            list.RemoveAll(e => e.docId == docId);

        // Clean up positional index
        var keysToRemove = _positions.Keys.Where(k => k.docId == docId).ToList();
        foreach (var key in keysToRemove)
            _positions.Remove(key);

        _docLengths.Remove(docId);
        _totalDocs = _docLengths.Count;
        _avgDocLength = _totalDocs > 0 ? _docLengths.Values.Average() : 0;
    }

    /// <summary>
    /// Queries the index using BM25 scoring.
    /// Returns (docId, score) pairs ranked by relevance.
    /// Supports phrase queries: wrap terms in double quotes for exact phrase matching.
    /// Example: 'quick "brown fox" lazy' matches docs with "brown fox" as a phrase.
    /// </summary>
    /// <param name="queryText">The search query text.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="conjunctive">If true, all terms must match (AND); false = any term (OR).</param>
    public List<(long docId, double score)> Query(string queryText, int topK = 10, bool conjunctive = false)
    {
        if (_totalDocs == 0) return new List<(long, double)>();

        // Extract phrases (quoted strings) and individual terms
        var (phrases, plainTerms) = ExtractPhrases(queryText);
        var queryTerms = _tokenizer.Tokenize(string.Join(" ", plainTerms));
        if (queryTerms.Count == 0 && phrases.Count == 0) return new List<(long, double)>();

        var scores = new Dictionary<long, double>();
        var termMatchCounts = new Dictionary<long, int>();

        // Score individual terms with BM25
        foreach (var term in queryTerms.Distinct())
        {
            if (!_postings.TryGetValue(term, out var postings)) continue;

            // IDF: inverse document frequency
            double df = postings.Count;
            double idf = Math.Log((_totalDocs - df + 0.5) / (df + 0.5) + 1.0);

            foreach (var (docId, tf) in postings)
            {
                int docLen = _docLengths[docId];
                // BM25 TF normalization
                double tfNorm = (tf * (K1 + 1)) / (tf + K1 * (1 - B + B * docLen / _avgDocLength));
                double termScore = idf * tfNorm;

                scores.TryGetValue(docId, out var existing);
                scores[docId] = existing + termScore;

                termMatchCounts.TryGetValue(docId, out var matchCount);
                termMatchCounts[docId] = matchCount + 1;
            }
        }

        // Apply phrase constraints — boost docs that contain exact phrase matches
        if (phrases.Count > 0)
        {
            var phraseMatchedDocs = new HashSet<long>();
            foreach (var phrase in phrases)
            {
                var phraseTokens = _tokenizer.Tokenize(phrase);
                if (phraseTokens.Count == 0) continue;

                foreach (var docId in _docLengths.Keys)
                {
                    if (MatchesPhrase(docId, phraseTokens))
                    {
                        phraseMatchedDocs.Add(docId);
                        // Boost phrase matches with a significant score bonus
                        scores.TryGetValue(docId, out var existing);
                        scores[docId] = existing + phraseTokens.Count * 2.0;

                        termMatchCounts.TryGetValue(docId, out var mc);
                        termMatchCounts[docId] = mc + phraseTokens.Count;
                    }
                }
            }

            // In conjunctive mode, require phrase match
            if (conjunctive && phraseMatchedDocs.Count > 0)
            {
                scores = scores
                    .Where(kv => phraseMatchedDocs.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
        }

        IEnumerable<KeyValuePair<long, double>> filtered = scores;
        if (conjunctive && phrases.Count == 0)
        {
            var uniqueTermCount = queryTerms.Distinct().Count();
            filtered = scores.Where(kv =>
                termMatchCounts.TryGetValue(kv.Key, out var mc) && mc >= uniqueTermCount);
        }

        return filtered
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Checks if a document contains a phrase (consecutive tokens at adjacent positions).
    /// </summary>
    private bool MatchesPhrase(long docId, List<string> phraseTokens)
    {
        if (phraseTokens.Count == 0) return true;

        // Get positions for the first token
        var firstKey = (docId, phraseTokens[0]);
        if (!_positions.TryGetValue(firstKey, out var startPositions))
            return false;

        foreach (var startPos in startPositions)
        {
            var matched = true;
            for (var i = 1; i < phraseTokens.Count; i++)
            {
                var nextKey = (docId, phraseTokens[i]);
                if (!_positions.TryGetValue(nextKey, out var nextPositions) ||
                    !nextPositions.Contains(startPos + i))
                {
                    matched = false;
                    break;
                }
            }
            if (matched) return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts quoted phrases and plain terms from a query string.
    /// Example: 'hello "world peace" goodbye' → phrases=["world peace"], plain=["hello", "goodbye"]
    /// </summary>
    private static (List<string> Phrases, List<string> PlainTerms) ExtractPhrases(string queryText)
    {
        var phrases = new List<string>();
        var plain = new List<string>();
        var i = 0;

        while (i < queryText.Length)
        {
            if (queryText[i] == '"')
            {
                var end = queryText.IndexOf('"', i + 1);
                if (end > i + 1)
                {
                    phrases.Add(queryText.Substring(i + 1, end - i - 1));
                    i = end + 1;
                    continue;
                }
            }

            // Collect non-quote characters as plain text
            var start = i;
            while (i < queryText.Length && queryText[i] != '"')
                i++;
            var segment = queryText.Substring(start, i - start).Trim();
            if (segment.Length > 0)
                plain.Add(segment);
        }

        return (phrases, plain);
    }

    /// <summary>Returns the number of indexed documents.</summary>
    public int DocumentCount => _totalDocs;

    /// <summary>Returns the number of unique terms in the index.</summary>
    public int TermCount => _postings.Count;
}
