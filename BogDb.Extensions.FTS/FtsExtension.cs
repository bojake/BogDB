using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Extension;
using BogDb.Core.Main;

namespace BogDb.Extensions.FTS;

/// <summary>
/// Full-text search extension — C++ parity with bogdb-master/extension/fts.
/// Provides CREATE_FTS_INDEX, DROP_FTS_INDEX, QUERY_FTS_INDEX table functions,
/// and STEM/TOKENIZE scalar functions.
/// </summary>
public class FtsExtension : IExtension
{
    public string Name => "fts";

    /// <summary>Registry of all active FTS indexes, keyed by index name.</summary>
    public Dictionary<string, FtsIndex> Indexes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Load(BogDatabase database)
    {
        // ── Table functions ──────────────────────────────────────────────
        var createFts = new CreateFtsIndexTableFunction(this, database);
        database.StandaloneTableFunctionRegistry.Register(createFts);

        var dropFts = new DropFtsIndexTableFunction(this);
        database.StandaloneTableFunctionRegistry.Register(dropFts);

        var queryFts = new QueryFtsIndexTableFunction(this, database);
        database.StandaloneTableFunctionRegistry.Register(queryFts);

        // ── Scalar functions ─────────────────────────────────────────────
        database.ScalarFunctionRegistry.Register("stem", args =>
        {
            if (args.Length == 0 || args[0] is null) return null;
            return (object?)PorterStemmer.Stem(args[0]!.ToString()!.ToLowerInvariant());
        });

        database.ScalarFunctionRegistry.Register("tokenize", args =>
        {
            if (args.Length == 0 || args[0] is null) return null;
            var tokenizer = new FtsTokenizer(enableStemming: false);
            var tokens = tokenizer.Tokenize(args[0]!.ToString()!);
            return (object?)new List<object?>(tokens.Cast<object?>());
        });
    }
}

// ── CREATE_FTS_INDEX table function ──────────────────────────────────────────

internal sealed class CreateFtsIndexTableFunction : ITableFunction
{
    private readonly FtsExtension _ext;
    private readonly BogDatabase _db;

    public CreateFtsIndexTableFunction(FtsExtension ext, BogDatabase db)
    {
        _ext = ext;
        _db = db;
    }

    public string Name => "CREATE_FTS_INDEX";

    public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
    {
        // Expected: CREATE_FTS_INDEX('table_name', 'index_name', ['prop1', 'prop2', ...])
        if (args.Count < 3)
        {
            yield return new Dictionary<string, object?>
                { ["result"] = "Error: CREATE_FTS_INDEX requires (table_name, index_name, properties)" };
            yield break;
        }

        var tableName  = args[0]?.ToString() ?? "";
        var indexName  = args[1]?.ToString() ?? "";
        var propsRaw   = args[2];

        // Parse properties
        var props = new List<string>();
        if (propsRaw is IEnumerable<object?> list)
        {
            foreach (var p in list)
                if (p != null) props.Add(p.ToString()!);
        }
        else if (propsRaw is string propStr)
        {
            props.AddRange(propStr.Split(',').Select(s => s.Trim()));
        }

        if (props.Count == 0)
        {
            yield return new Dictionary<string, object?>
                { ["result"] = "Error: no properties specified for FTS index" };
            yield break;
        }

        var tokenizer = new FtsTokenizer();
        var index = new FtsIndex(indexName, tableName, props, tokenizer);

        // Scan the table and populate the index
        using var conn = new BogConnection(_db);
        var propExpressions = string.Join(", ", props.Select(p => $"n.{p}"));
        var query = $"MATCH (n:{tableName}) RETURN id(n) AS _id, {propExpressions}";
        var result = conn.Query(query);

        if (!result.IsSuccess)
        {
            yield return new Dictionary<string, object?> { ["result"] = $"Error: {result.ErrorMessage}" };
            yield break;
        }

        int docCount = 0;
        while (result.HasNext())
        {
            var row = result.GetNext();
            var idVal = row.GetValue(0);
            long docId = Convert.ToInt64(idVal);

            var textParts = new List<string>();
            for (int i = 1; i <= props.Count; i++)
            {
                var val = row.GetValue(i);
                if (val != null) textParts.Add(val.ToString()!);
            }
            var text = string.Join(" ", textParts);
            index.AddDocument(docId, text);
            docCount++;
        }

        _ext.Indexes[indexName] = index;
        yield return new Dictionary<string, object?>
        {
            ["result"] = $"FTS index '{indexName}' created on {tableName}({string.Join(", ", props)}) with {docCount} documents, {index.TermCount} unique terms"
        };
    }
}

// ── DROP_FTS_INDEX table function ────────────────────────────────────────────

internal sealed class DropFtsIndexTableFunction : ITableFunction
{
    private readonly FtsExtension _ext;

    public DropFtsIndexTableFunction(FtsExtension ext) { _ext = ext; }

    public string Name => "DROP_FTS_INDEX";

    public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
    {
        if (args.Count < 1 || args[0] is null)
        {
            yield return new Dictionary<string, object?>
                { ["result"] = "Error: DROP_FTS_INDEX requires (index_name)" };
            yield break;
        }

        var indexName = args[0]!.ToString()!;
        yield return new Dictionary<string, object?>
        {
            ["result"] = _ext.Indexes.Remove(indexName)
                ? $"FTS index '{indexName}' dropped"
                : $"Error: FTS index '{indexName}' not found"
        };
    }
}

// ── QUERY_FTS_INDEX table function ───────────────────────────────────────────

internal sealed class QueryFtsIndexTableFunction : ITableFunction
{
    private readonly FtsExtension _ext;
    private readonly BogDatabase _db;

    public QueryFtsIndexTableFunction(FtsExtension ext, BogDatabase db)
    {
        _ext = ext;
        _db = db;
    }

    public string Name => "QUERY_FTS_INDEX";

    public IReadOnlyList<(string Name, string Type)>? Schema =>
        new[] { ("node_offset", "INT64"), ("score", "DOUBLE") };

    public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
    {
        // Expected: QUERY_FTS_INDEX('index_name', 'search query')
        if (args.Count < 2 || args[0] is null || args[1] is null)
            yield break;

        var indexName = args[0]!.ToString()!;
        var queryText = args[1]!.ToString()!;

        if (!_ext.Indexes.TryGetValue(indexName, out var index))
            yield break;

        // Parse optional args (top_k from 3rd positional arg)
        int topK = 10;
        bool conjunctive = false;
        if (args.Count > 2 && args[2] != null)
        {
            try { topK = Convert.ToInt32(args[2]); } catch { /* ignore */ }
        }
        if (args.Count > 3 && args[3] != null)
        {
            var mode = args[3]?.ToString()?.ToLowerInvariant();
            conjunctive = mode == "conjunctive" || mode == "true";
        }

        var results = index.Query(queryText, topK, conjunctive);
        foreach (var (docId, score) in results)
        {
            yield return new Dictionary<string, object?>
            {
                ["node_offset"] = docId,
                ["score"] = Math.Round(score, 6)
            };
        }
    }
}
