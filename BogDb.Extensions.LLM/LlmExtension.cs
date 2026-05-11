using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BogDb.Core.Catalog;
using BogDb.Core.Extension;
using BogDb.Core.Main;

namespace BogDb.Extensions.LLM
{
    public class LlmExtension : IExtension
    {
        private readonly Func<BogDatabase, ILlmProvider>? _providerFactory;

        public LlmExtension(Func<BogDatabase, ILlmProvider>? providerFactory = null)
        {
            _providerFactory = providerFactory;
        }

        public string Name => "llm";

        public void Load(BogDatabase database)
        {
            database.AddExtensionOption("llm_provider", BogDb.Core.Common.LogicalTypeID.STRING, "ollama");
            database.AddExtensionOption("llm_api_key", BogDb.Core.Common.LogicalTypeID.STRING, null, isConfidential: true);
            database.AddExtensionOption("llm_model", BogDb.Core.Common.LogicalTypeID.STRING, "nomic-embed-text");
            database.AddExtensionOption("llm_base_url", BogDb.Core.Common.LogicalTypeID.STRING, "http://localhost:11434");

            var embedTexts = new LlmEmbedTextsTableFunction(database, _providerFactory);
            database.FunctionRegistry.Register(embedTexts);
            database.StandaloneTableFunctionRegistry.Register(embedTexts);
            var rankTexts = new LlmRankTextsTableFunction(database, _providerFactory);
            database.FunctionRegistry.Register(rankTexts);
            database.StandaloneTableFunctionRegistry.Register(rankTexts);
            var rankNodes = new LlmRankNodesTableFunction(database, _providerFactory);
            database.FunctionRegistry.Register(rankNodes);
            database.StandaloneTableFunctionRegistry.Register(rankNodes);
            var embedNodes = new LlmEmbedNodesTableFunction(database, _providerFactory);
            database.FunctionRegistry.Register(embedNodes);
            database.StandaloneTableFunctionRegistry.Register(embedNodes);
            var searchNodes = new LlmSearchNodesTableFunction(database, _providerFactory);
            database.FunctionRegistry.Register(searchNodes);
            database.StandaloneTableFunctionRegistry.Register(searchNodes);
            var searchJsonArray = new LlmSearchJsonArrayTableFunction(database, _providerFactory);
            database.FunctionRegistry.Register(searchJsonArray);
            database.StandaloneTableFunctionRegistry.Register(searchJsonArray);
            var embedJsonArray = new LlmEmbedJsonArrayTableFunction(database, _providerFactory);
            database.FunctionRegistry.Register(embedJsonArray);
            database.StandaloneTableFunctionRegistry.Register(embedJsonArray);
            var ingestJsonArray = new LlmIngestJsonArrayToNodesTableFunction(database, _providerFactory);
            database.FunctionRegistry.Register(ingestJsonArray);
            database.StandaloneTableFunctionRegistry.Register(ingestJsonArray);
            database.ScalarFunctionRegistry.Register(
                "llm_embed",
                args => LlmScalarFunctions.LlmEmbed(args, database, _providerFactory));
            database.ScalarFunctionRegistry.Register(
                "llm_embed_batch",
                args => LlmScalarFunctions.LlmEmbedBatch(args, database, _providerFactory));
            database.ScalarFunctionRegistry.Register(
                "llm_similarity",
                args => LlmScalarFunctions.LlmSimilarity(args, database, _providerFactory));
        }
    }

    internal static class LlmScalarFunctions
    {
        public static object? LlmEmbed(
            object?[] args,
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            if (args.Length != 1 || args[0] is not string text)
                throw new ArgumentException("llm_embed requires a single string argument.");

            var provider = providerFactory != null
                ? providerFactory(database)
                : ResolveProvider(database);

            var embedding = provider.GenerateEmbeddingAsync(text).GetAwaiter().GetResult();
            var values = new List<object?>(embedding.Length);
            foreach (var value in embedding)
                values.Add(value);
            return values;
        }

        public static object? LlmEmbedBatch(
            object?[] args,
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            if (args.Length != 1 || args[0] is not IEnumerable batch || args[0] is string)
                throw new ArgumentException("llm_embed_batch requires a single list argument.");

            var provider = ResolveProvider(database, providerFactory);

            var embeddings = new List<object?>();
            foreach (var text in ToTextBatch(batch, "llm_embed_batch"))
            {
                var embedding = provider.GenerateEmbeddingAsync(text).GetAwaiter().GetResult();
                var values = new List<object?>(embedding.Length);
                foreach (var value in embedding)
                    values.Add(value);
                embeddings.Add(values);
            }

            return embeddings;
        }

        public static object? LlmSimilarity(
            object?[] args,
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            if (args.Length != 2)
                throw new ArgumentException("llm_similarity requires exactly two arguments.");

            var provider = ResolveProvider(database, providerFactory);
            var left = ResolveEmbedding(args[0], provider, "llm_similarity");
            var right = ResolveEmbedding(args[1], provider, "llm_similarity");
            return CosineSimilarity(left, right);
        }

        private static ILlmProvider ResolveProvider(BogDatabase database)
            => ResolveProvider(database, providerFactory: null);

        internal static ILlmProvider ResolveProvider(
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            if (providerFactory != null)
                return providerFactory(database);

            var providerName = database.GetExtensionOptionValue("llm_provider")?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(providerName))
                throw new InvalidOperationException("LLM provider option 'llm_provider' is not set.");

            var apiKey = database.GetExtensionOptionValue("llm_api_key")?.ToString();
            var model = database.GetExtensionOptionValue("llm_model")?.ToString();
            var baseUrl = database.GetExtensionOptionValue("llm_base_url")?.ToString();

            return providerName.ToLowerInvariant() switch
            {
                "openai" => new OpenAiProvider(
                    RequireApiKey(providerName, apiKey),
                    string.IsNullOrWhiteSpace(model) ? "text-embedding-ada-002" : model),
                "anthropic" => new AnthropicProvider(
                    RequireApiKey(providerName, apiKey),
                    string.IsNullOrWhiteSpace(model) ? "voyage-2" : model),
                "cohere" => new CohereProvider(
                    RequireApiKey(providerName, apiKey),
                    string.IsNullOrWhiteSpace(model) ? "embed-english-v3.0" : model),
                "ollama" => new OllamaProvider(
                    string.IsNullOrWhiteSpace(model) ? "nomic-embed-text" : model,
                    string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl),
                "lmstudio" => new LmStudioProvider(
                    string.IsNullOrWhiteSpace(model) ? "local-model" : model,
                    string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:1234/v1" : baseUrl),
                _ => throw new InvalidOperationException(
                    $"Unsupported LLM provider '{providerName}'.")
            };
        }

        internal static IEnumerable<string> ToTextBatch(IEnumerable batch, string functionName)
        {
            foreach (var item in batch)
            {
                if (item is not string text)
                    throw new ArgumentException($"{functionName} requires a list of strings.");
                yield return text;
            }
        }

        private static string RequireApiKey(string providerName, string? apiKey)
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                return apiKey;

            throw new InvalidOperationException(
                $"LLM provider '{providerName}' requires option 'llm_api_key' to be set.");
        }

        internal static List<object?> ToEmbeddingList(float[] embedding)
        {
            var values = new List<object?>(embedding.Length);
            foreach (var value in embedding)
                values.Add(value);
            return values;
        }

        internal static float[] ResolveEmbedding(object? value, ILlmProvider provider, string functionName)
        {
            if (value is string text)
                return provider.GenerateEmbeddingAsync(text).GetAwaiter().GetResult();

            if (value is IEnumerable enumerable)
                return ToEmbeddingArray(enumerable, functionName);

            throw new ArgumentException($"{functionName} requires string or embedding-list arguments.");
        }

        internal static float[] ToEmbeddingArray(IEnumerable values, string functionName)
        {
            var embedding = new List<float>();
            foreach (var value in values)
            {
                switch (value)
                {
                    case float single:
                        embedding.Add(single);
                        break;
                    case double dbl:
                        embedding.Add((float)dbl);
                        break;
                    case int integer:
                        embedding.Add(integer);
                        break;
                    case long longInteger:
                        embedding.Add(longInteger);
                        break;
                    case decimal dec:
                        embedding.Add((float)dec);
                        break;
                    default:
                        throw new ArgumentException($"{functionName} embedding lists must contain numeric values.");
                }
            }

            return embedding.ToArray();
        }

        internal static double CosineSimilarity(float[] left, float[] right)
        {
            if (left.Length != right.Length)
                throw new ArgumentException("Embeddings must have the same dimension.");

            var dot = 0d;
            var normLeft = 0d;
            var normRight = 0d;
            for (var i = 0; i < left.Length; i++)
            {
                dot += left[i] * right[i];
                normLeft += left[i] * left[i];
                normRight += right[i] * right[i];
            }

            if (normLeft == 0d || normRight == 0d)
                return 0d;

            return dot / (Math.Sqrt(normLeft) * Math.Sqrt(normRight));
        }
    }

    internal static class LlmSchemaValidation
    {
        public static void ValidateEmbeddingProperty(
            NodeTableCatalogEntry entry,
            string propertyName,
            string tableName,
            string functionName)
        {
            var property = entry.GetProperty(propertyName);
            if (property.Type != BogDb.Core.Common.LogicalTypeID.LIST ||
                property.LeafType != BogDb.Core.Common.LogicalTypeID.FLOAT ||
                property.ListDepth != 1)
            {
                throw new ArgumentException(
                    $"{functionName} requires property '{propertyName}' on table '{tableName}' to be declared as FLOAT[].");
            }
        }
    }

    internal sealed class LlmEmbedTextsTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly Func<BogDatabase, ILlmProvider>? _providerFactory;

        public LlmEmbedTextsTableFunction(
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            _database = database;
            _providerFactory = providerFactory;
        }

        public string Name => "llm_embed_texts";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("index", "INT64"),
                ("text", "STRING"),
                ("embedding", "LIST")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            if (args.Count == 0)
                throw new ArgumentException("llm_embed_texts requires one or more string arguments.");

            var provider = LlmScalarFunctions.ResolveProvider(_database, _providerFactory);
            var index = 0L;
            foreach (var text in ToInputTexts(args))
            {
                var embedding = provider.GenerateEmbeddingAsync(text).GetAwaiter().GetResult();
                var values = new List<object?>(embedding.Length);
                foreach (var value in embedding)
                    values.Add(value);

                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["index"] = index++,
                    ["text"] = text,
                    ["embedding"] = values
                };
            }
        }

        private static IEnumerable<string> ToInputTexts(IReadOnlyList<object?> args)
        {
            if (args.Count == 1 && args[0] is IEnumerable batch && args[0] is not string)
            {
                foreach (var text in LlmScalarFunctions.ToTextBatch(batch, "llm_embed_texts"))
                    yield return text;
                yield break;
            }

            foreach (var arg in args)
            {
                if (arg is not string text)
                    throw new ArgumentException("llm_embed_texts requires string arguments.");
                yield return text;
            }
        }
    }

    internal sealed class LlmRankTextsTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly Func<BogDatabase, ILlmProvider>? _providerFactory;

        public LlmRankTextsTableFunction(
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            _database = database;
            _providerFactory = providerFactory;
        }

        public string Name => "llm_rank_texts";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("rank", "INT64"),
                ("text", "STRING"),
                ("score", "DOUBLE"),
                ("embedding", "LIST")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            if (args.Count < 2)
                throw new ArgumentException("llm_rank_texts requires a query string and one or more candidate strings.");

            if (args[0] is not string queryText)
                throw new ArgumentException("llm_rank_texts requires the first argument to be a query string.");

            var provider = LlmScalarFunctions.ResolveProvider(_database, _providerFactory);
            var queryEmbedding = provider.GenerateEmbeddingAsync(queryText).GetAwaiter().GetResult();
            var candidates = new List<(string Text, double Score, List<object?> Embedding)>();

            foreach (var candidate in ToCandidateTexts(args))
            {
                var embedding = provider.GenerateEmbeddingAsync(candidate).GetAwaiter().GetResult();
                candidates.Add((
                    candidate,
                    LlmScalarFunctions.CosineSimilarity(queryEmbedding, embedding),
                    LlmScalarFunctions.ToEmbeddingList(embedding)));
            }

            var rank = 1L;
            foreach (var candidate in candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Text, StringComparer.Ordinal))
            {
                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rank"] = rank++,
                    ["text"] = candidate.Text,
                    ["score"] = candidate.Score,
                    ["embedding"] = candidate.Embedding
                };
            }
        }

        private static IEnumerable<string> ToCandidateTexts(IReadOnlyList<object?> args)
        {
            if (args.Count == 2 && args[1] is IEnumerable batch && args[1] is not string)
            {
                foreach (var text in LlmScalarFunctions.ToTextBatch(batch, "llm_rank_texts"))
                    yield return text;
                yield break;
            }

            for (var i = 1; i < args.Count; i++)
            {
                if (args[i] is not string text)
                    throw new ArgumentException("llm_rank_texts requires candidate string arguments.");
                yield return text;
            }
        }
    }

    internal sealed class LlmRankNodesTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly Func<BogDatabase, ILlmProvider>? _providerFactory;

        public LlmRankNodesTableFunction(
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            _database = database;
            _providerFactory = providerFactory;
        }

        public string Name => "llm_rank_nodes";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("rank", "INT64"),
                ("id", "ANY"),
                ("text", "STRING"),
                ("score", "DOUBLE"),
                ("embedding", "LIST")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            if (args.Count < 2 || args.Count > 5)
                throw new ArgumentException(
                    "llm_rank_nodes requires table name, query text, and optional embedding property, text property, and limit.");

            if (args[0] is not string tableName || string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("llm_rank_nodes requires the first argument to be a node table name.");
            if (args[1] is not string queryText)
                throw new ArgumentException("llm_rank_nodes requires the second argument to be a query string.");

            var embeddingProperty = args.Count >= 3 && args[2] is string embeddingName && !string.IsNullOrWhiteSpace(embeddingName)
                ? embeddingName
                : "embedding";
            var textProperty = args.Count >= 4 && args[3] is string textName && !string.IsNullOrWhiteSpace(textName)
                ? textName
                : "body";
            var limit = args.Count >= 5
                ? ParseLimit(args[4])
                : int.MaxValue;

            if (_database.Catalog.GetTableCatalogEntry(null, tableName, useInternal: false) is not BogDb.Core.Catalog.NodeTableCatalogEntry entry)
                throw new ArgumentException($"llm_rank_nodes requires an existing node table: {tableName}");
            if (!entry.ContainsProperty(embeddingProperty))
                throw new ArgumentException($"Embedding property '{embeddingProperty}' does not exist on table '{tableName}'.");
            if (!entry.ContainsProperty(textProperty))
                throw new ArgumentException($"Text property '{textProperty}' does not exist on table '{tableName}'.");
            LlmSchemaValidation.ValidateEmbeddingProperty(entry, embeddingProperty, tableName, "llm_rank_nodes");

            var primaryKey = ResolvePrimaryKeyName(entry);
            var provider = LlmScalarFunctions.ResolveProvider(_database, _providerFactory);
            var queryEmbedding = provider.GenerateEmbeddingAsync(queryText).GetAwaiter().GetResult();
            var candidates = new List<(object? Id, string Text, double Score, List<object?> Embedding)>();

            foreach (KeyValuePair<object, Dictionary<string, object>> row in _database.EnumerateNodeRows(tableName))
            {
                var nodeId = row.Key;
                var properties = row.Value;
                if (!properties.TryGetValue(embeddingProperty, out var embeddingValue) || embeddingValue is null)
                    continue;
                if (!properties.TryGetValue(textProperty, out var textValue) || textValue is not string text)
                    continue;

                var embedding = LlmScalarFunctions.ResolveEmbedding(embeddingValue, provider, "llm_rank_nodes");
                var rowId = properties.TryGetValue(primaryKey, out var propertyId) ? propertyId : nodeId;
                candidates.Add((
                    rowId,
                    text,
                    LlmScalarFunctions.CosineSimilarity(queryEmbedding, embedding),
                    LlmScalarFunctions.ToEmbeddingList(embedding)));
            }

            var rank = 1L;
            foreach (var candidate in candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Text, StringComparer.Ordinal)
                .Take(limit))
            {
                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rank"] = rank++,
                    ["id"] = candidate.Id,
                    ["text"] = candidate.Text,
                    ["score"] = candidate.Score,
                    ["embedding"] = candidate.Embedding
                };
            }
        }

        private static int ParseLimit(object? value)
        {
            if (value is int limit && limit > 0)
                return limit;
            if (value is long longLimit && longLimit > 0 && longLimit <= int.MaxValue)
                return (int)longLimit;

            throw new ArgumentException("llm_rank_nodes limit must be a positive integer.");
        }

        internal static string ResolvePrimaryKeyName(BogDb.Core.Catalog.NodeTableCatalogEntry entry)
        {
            foreach (var property in entry.GetProperties())
            {
                if (entry.GetPropertyID(property.Name) == entry.PrimaryKeyPropertyID)
                    return property.Name;
            }

            return "id";
        }
    }

    internal sealed class LlmEmbedNodesTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly Func<BogDatabase, ILlmProvider>? _providerFactory;

        public LlmEmbedNodesTableFunction(
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            _database = database;
            _providerFactory = providerFactory;
        }

        public string Name => "llm_embed_nodes";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("id", "ANY"),
                ("text", "STRING"),
                ("embedding", "LIST")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            if (args.Count < 1 || args.Count > 4)
                throw new ArgumentException(
                    "llm_embed_nodes requires table name and optional text property, embedding property, and overwrite flag.");

            if (args[0] is not string tableName || string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("llm_embed_nodes requires the first argument to be a node table name.");

            var textProperty = args.Count >= 2 && args[1] is string textName && !string.IsNullOrWhiteSpace(textName)
                ? textName
                : "body";
            var embeddingProperty = args.Count >= 3 && args[2] is string embeddingName && !string.IsNullOrWhiteSpace(embeddingName)
                ? embeddingName
                : "embedding";
            var overwrite = args.Count >= 4 && ParseOverwrite(args[3]);

            if (_database.Catalog.GetTableCatalogEntry(null, tableName, useInternal: false) is not BogDb.Core.Catalog.NodeTableCatalogEntry entry)
                throw new ArgumentException($"llm_embed_nodes requires an existing node table: {tableName}");
            if (!entry.ContainsProperty(textProperty))
                throw new ArgumentException($"Text property '{textProperty}' does not exist on table '{tableName}'.");
            if (!entry.ContainsProperty(embeddingProperty))
                throw new ArgumentException($"Embedding property '{embeddingProperty}' does not exist on table '{tableName}'.");
            LlmSchemaValidation.ValidateEmbeddingProperty(entry, embeddingProperty, tableName, "llm_embed_nodes");

            var primaryKey = LlmRankNodesTableFunction.ResolvePrimaryKeyName(entry);
            var provider = LlmScalarFunctions.ResolveProvider(_database, _providerFactory);
            var updates = new List<(object Id, string Text, List<object?> Embedding, Dictionary<string, object> Properties)>();

            foreach (KeyValuePair<object, Dictionary<string, object>> row in _database.EnumerateNodeRows(tableName))
            {
                var nodeId = row.Key;
                var properties = row.Value;
                if (!properties.TryGetValue(textProperty, out var textValue) || textValue is not string text)
                    continue;
                if (!overwrite &&
                    properties.TryGetValue(embeddingProperty, out var existingEmbedding) &&
                    existingEmbedding is not null)
                {
                    continue;
                }

                var embedding = provider.GenerateEmbeddingAsync(text).GetAwaiter().GetResult();
                var updatedProperties = new Dictionary<string, object>(properties, StringComparer.OrdinalIgnoreCase)
                {
                    [embeddingProperty] = LlmScalarFunctions.ToEmbeddingList(embedding)
                };

                var rowId = properties.TryGetValue(primaryKey, out var propertyId) && propertyId is not null
                    ? propertyId
                    : nodeId;
                updates.Add((rowId, text, LlmScalarFunctions.ToEmbeddingList(embedding), updatedProperties));
            }

            if (updates.Count == 0)
                yield break;

            using var connection = new BogConnection(_database);
            connection.BeginWriteTransaction();
            try
            {
                foreach (var update in updates)
                    connection.UpsertNode(tableName, update.Id, update.Properties);

                connection.Commit();
            }
            catch
            {
                connection.Rollback();
                throw;
            }

            foreach (var update in updates)
            {
                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = update.Id,
                    ["text"] = update.Text,
                    ["embedding"] = update.Embedding
                };
            }
        }

        internal static bool ParseOverwrite(object? value)
        {
            if (value is bool boolean)
                return boolean;
            if (value is string text)
            {
                if (bool.TryParse(text, out var parsed))
                    return parsed;
                if (long.TryParse(text, out var numericText))
                    return numericText != 0;
            }
            if (value is long longValue)
                return longValue != 0;
            if (value is int intValue)
                return intValue != 0;

            throw new ArgumentException("llm_embed_nodes overwrite flag must be a boolean.");
        }
    }

    internal sealed class LlmSearchNodesTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly Func<BogDatabase, ILlmProvider>? _providerFactory;

        public LlmSearchNodesTableFunction(
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            _database = database;
            _providerFactory = providerFactory;
        }

        public string Name => "llm_search_nodes";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("rank", "INT64"),
                ("id", "ANY"),
                ("text", "STRING"),
                ("score", "DOUBLE"),
                ("embedding", "LIST")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            if (args.Count < 2 || args.Count > 6)
                throw new ArgumentException(
                    "llm_search_nodes requires table name, query text, and optional text property, embedding property, limit, and embed-missing flag.");

            if (args[0] is not string tableName || string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("llm_search_nodes requires the first argument to be a node table name.");
            if (args[1] is not string queryText)
                throw new ArgumentException("llm_search_nodes requires the second argument to be a query string.");

            var textProperty = args.Count >= 3 && args[2] is string textName && !string.IsNullOrWhiteSpace(textName)
                ? textName
                : "body";
            var embeddingProperty = args.Count >= 4 && args[3] is string embeddingName && !string.IsNullOrWhiteSpace(embeddingName)
                ? embeddingName
                : "embedding";
            var limit = args.Count >= 5
                ? ParseLimit(args[4])
                : int.MaxValue;
            var embedMissing = args.Count >= 6 && LlmEmbedNodesTableFunction.ParseOverwrite(args[5]);

            if (embedMissing)
            {
                var embedNodes = new LlmEmbedNodesTableFunction(_database, _providerFactory);
                foreach (var _ in embedNodes.Invoke(new object?[] { tableName, textProperty, embeddingProperty, false }))
                {
                }
            }

            var rankNodes = new LlmRankNodesTableFunction(_database, _providerFactory);
            foreach (var row in rankNodes.Invoke(new object?[] { tableName, queryText, embeddingProperty, textProperty, limit }))
                yield return row;
        }

        private static int ParseLimit(object? value)
        {
            if (value is int limit && limit > 0)
                return limit;
            if (value is long longLimit && longLimit > 0 && longLimit <= int.MaxValue)
                return (int)longLimit;
            if (value is string text && int.TryParse(text, out var parsed) && parsed > 0)
                return parsed;

            throw new ArgumentException("llm_search_nodes limit must be a positive integer.");
        }
    }

    internal sealed class LlmSearchJsonArrayTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly Func<BogDatabase, ILlmProvider>? _providerFactory;

        public LlmSearchJsonArrayTableFunction(
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            _database = database;
            _providerFactory = providerFactory;
        }

        public string Name => "llm_search_json_array";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("rank", "INT64"),
                ("text", "STRING"),
                ("score", "DOUBLE"),
                ("embedding", "LIST"),
                ("row_json", "STRING")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            if (args.Count < 2 || args.Count > 4)
                throw new ArgumentException(
                    "llm_search_json_array requires file path, query text, and optional text field and limit.");

            if (args[0] is not string filePath || string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("llm_search_json_array requires the first argument to be a file path.");
            if (args[1] is not string queryText)
                throw new ArgumentException("llm_search_json_array requires the second argument to be a query string.");

            var textField = args.Count >= 3 && args[2] is string fieldName && !string.IsNullOrWhiteSpace(fieldName)
                ? fieldName
                : "body";
            var limit = args.Count >= 4
                ? ParseLimit(args[3])
                : int.MaxValue;

            var provider = LlmScalarFunctions.ResolveProvider(_database, _providerFactory);
            var queryEmbedding = provider.GenerateEmbeddingAsync(queryText).GetAwaiter().GetResult();
            var candidates = new List<(string Text, double Score, List<object?> Embedding, string RowJson)>();

            foreach (var row in ReadJsonRows(filePath))
            {
                if (!row.TryGetValue(textField, out var textValue) || textValue is not string text)
                    continue;

                var embedding = provider.GenerateEmbeddingAsync(text).GetAwaiter().GetResult();
                candidates.Add((
                    text,
                    LlmScalarFunctions.CosineSimilarity(queryEmbedding, embedding),
                    LlmScalarFunctions.ToEmbeddingList(embedding),
                    JsonSerializer.Serialize(row)));
            }

            var rank = 1L;
            foreach (var candidate in candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Text, StringComparer.Ordinal)
                .Take(limit))
            {
                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rank"] = rank++,
                    ["text"] = candidate.Text,
                    ["score"] = candidate.Score,
                    ["embedding"] = candidate.Embedding,
                    ["row_json"] = candidate.RowJson
                };
            }
        }

        internal IEnumerable<Dictionary<string, object?>> ReadJsonRows(string filePath)
        {
            if (!TryOpenRead(filePath, out var stream))
                throw new FileNotFoundException($"JSON file not found: '{filePath}'", filePath);

            JsonNode? root;
            IEnumerable<Dictionary<string, object?>>? fallbackRows = null;
            try
            {
                using (stream)
                {
                    root = JsonNode.Parse(stream);
                }
            }
            catch (JsonException)
            {
                root = null;
                fallbackRows = ParseNewlineDelimited(filePath).ToList();
            }

            if (fallbackRows != null)
            {
                foreach (var row in fallbackRows)
                    yield return row;
                yield break;
            }

            if (root is JsonArray array)
            {
                foreach (var element in array)
                    yield return JsonNodeToRow(element);
                yield break;
            }

            yield return JsonNodeToRow(root);
        }

        private IEnumerable<Dictionary<string, object?>> ParseNewlineDelimited(string filePath)
        {
            using var stream = OpenReadRequired(filePath);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var node = JsonNode.Parse(line);
                yield return JsonNodeToRow(node);
            }
        }

        private Stream OpenReadRequired(string filePath)
        {
            if (TryOpenRead(filePath, out var stream))
                return stream;

            throw new FileNotFoundException($"JSON file not found: '{filePath}'", filePath);
        }

        private bool TryOpenRead(string filePath, out Stream stream)
        {
            stream = Stream.Null;
            var schemeSeparator = filePath.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator > 0)
            {
                var scheme = filePath[..schemeSeparator];
                if (_database.TryGetFileSystem(scheme, out var fileSystem))
                {
                    using var fileInfo = fileSystem.OpenFile(filePath, BogDb.Core.Common.FileSystem.FileFlags.Read);
                    var buffer = new byte[checked((int)fileInfo.GetFileSize())];
                    fileInfo.Read(buffer, 0);
                    stream = new MemoryStream(buffer, writable: false);
                    return true;
                }
            }

            if (!File.Exists(filePath))
                return false;

            stream = File.OpenRead(filePath);
            return true;
        }

        private static int ParseLimit(object? value)
        {
            if (value is int limit && limit > 0)
                return limit;
            if (value is long longLimit && longLimit > 0 && longLimit <= int.MaxValue)
                return (int)longLimit;
            if (value is string text && int.TryParse(text, out var parsed) && parsed > 0)
                return parsed;

            throw new ArgumentException("llm_search_json_array limit must be a positive integer.");
        }

        private static Dictionary<string, object?> JsonNodeToRow(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in obj)
                    row[key] = JsonValueToClr(value);
                return row;
            }

            return new Dictionary<string, object?> { ["_value"] = JsonValueToClr(node) };
        }

        private static object? JsonValueToClr(JsonNode? value) => value switch
        {
            null => null,
            JsonValue v when v.TryGetValue<bool>(out var b) => b,
            JsonValue v when v.TryGetValue<long>(out var l) => l,
            JsonValue v when v.TryGetValue<double>(out var d) => d,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonArray arr => arr.ToJsonString(),
            JsonObject obj => obj.ToJsonString(),
            _ => value.ToJsonString()
        };
    }

    internal sealed class LlmEmbedJsonArrayTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly Func<BogDatabase, ILlmProvider>? _providerFactory;

        public LlmEmbedJsonArrayTableFunction(
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            _database = database;
            _providerFactory = providerFactory;
        }

        public string Name => "llm_embed_json_array";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("index", "INT64"),
                ("text", "STRING"),
                ("embedding", "LIST"),
                ("row_json", "STRING")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            if (args.Count < 1 || args.Count > 2)
                throw new ArgumentException(
                    "llm_embed_json_array requires file path and optional text field.");

            if (args[0] is not string filePath || string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("llm_embed_json_array requires the first argument to be a file path.");

            var textField = args.Count >= 2 && args[1] is string fieldName && !string.IsNullOrWhiteSpace(fieldName)
                ? fieldName
                : "body";

            var provider = LlmScalarFunctions.ResolveProvider(_database, _providerFactory);
            var reader = new LlmSearchJsonArrayTableFunction(_database, _providerFactory);
            var index = 0L;

            foreach (var row in reader.ReadJsonRows(filePath))
            {
                if (!row.TryGetValue(textField, out var textValue) || textValue is not string text)
                    continue;

                var embedding = provider.GenerateEmbeddingAsync(text).GetAwaiter().GetResult();
                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["index"] = index++,
                    ["text"] = text,
                    ["embedding"] = LlmScalarFunctions.ToEmbeddingList(embedding),
                    ["row_json"] = JsonSerializer.Serialize(row)
                };
            }
        }
    }

    internal sealed class LlmIngestJsonArrayToNodesTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly Func<BogDatabase, ILlmProvider>? _providerFactory;

        public LlmIngestJsonArrayToNodesTableFunction(
            BogDatabase database,
            Func<BogDatabase, ILlmProvider>? providerFactory)
        {
            _database = database;
            _providerFactory = providerFactory;
        }

        public string Name => "llm_ingest_json_array_to_nodes";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("id", "ANY"),
                ("text", "STRING"),
                ("embedding", "LIST"),
                ("row_json", "STRING")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            if (args.Count < 2 || args.Count > 5)
                throw new ArgumentException(
                    "llm_ingest_json_array_to_nodes requires table name, file path, and optional id field, text field, and embedding property.");

            if (args[0] is not string tableName || string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("llm_ingest_json_array_to_nodes requires the first argument to be a node table name.");
            if (args[1] is not string filePath || string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("llm_ingest_json_array_to_nodes requires the second argument to be a file path.");

            if (_database.Catalog.GetTableCatalogEntry(null, tableName, useInternal: false) is not BogDb.Core.Catalog.NodeTableCatalogEntry entry)
                throw new ArgumentException($"llm_ingest_json_array_to_nodes requires an existing node table: {tableName}");

            var primaryKeyProperty = LlmRankNodesTableFunction.ResolvePrimaryKeyName(entry);
            var idField = args.Count >= 3 && args[2] is string idFieldName && !string.IsNullOrWhiteSpace(idFieldName)
                ? idFieldName
                : primaryKeyProperty;
            var textField = args.Count >= 4 && args[3] is string textFieldName && !string.IsNullOrWhiteSpace(textFieldName)
                ? textFieldName
                : "body";
            var embeddingProperty = args.Count >= 5 && args[4] is string embeddingPropertyName && !string.IsNullOrWhiteSpace(embeddingPropertyName)
                ? embeddingPropertyName
                : "embedding";

            if (!entry.ContainsProperty(primaryKeyProperty))
                throw new ArgumentException($"Primary key property '{primaryKeyProperty}' does not exist on table '{tableName}'.");
            if (!entry.ContainsProperty(textField))
                throw new ArgumentException($"Text property '{textField}' does not exist on table '{tableName}'.");
            if (!entry.ContainsProperty(embeddingProperty))
                throw new ArgumentException($"Embedding property '{embeddingProperty}' does not exist on table '{tableName}'.");
            LlmSchemaValidation.ValidateEmbeddingProperty(entry, embeddingProperty, tableName, "llm_ingest_json_array_to_nodes");

            var provider = LlmScalarFunctions.ResolveProvider(_database, _providerFactory);
            var reader = new LlmSearchJsonArrayTableFunction(_database, _providerFactory);
            var allowedProperties = entry.GetProperties()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ingested = new List<(object Id, string Text, List<object?> Embedding, string RowJson, Dictionary<string, object> Properties)>();

            foreach (var row in reader.ReadJsonRows(filePath))
            {
                if (!row.TryGetValue(idField, out var idValue) || idValue is null)
                    continue;
                if (!row.TryGetValue(textField, out var textValue) || textValue is not string text)
                    continue;

                var embedding = LlmScalarFunctions.ToEmbeddingList(
                    provider.GenerateEmbeddingAsync(text).GetAwaiter().GetResult());
                var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    [primaryKeyProperty] = idValue,
                    [textField] = text,
                    [embeddingProperty] = embedding
                };

                foreach (var (key, value) in row)
                {
                    if (!allowedProperties.Contains(key) || value is null || properties.ContainsKey(key))
                        continue;
                    properties[key] = value;
                }

                ingested.Add((idValue, text, embedding, JsonSerializer.Serialize(row), properties));
            }

            if (ingested.Count == 0)
                yield break;

            using var connection = new BogConnection(_database);
            connection.BeginWriteTransaction();
            try
            {
                foreach (var row in ingested)
                    connection.UpsertNode(tableName, row.Id, row.Properties);
                connection.Commit();
            }
            catch
            {
                connection.Rollback();
                throw;
            }

            foreach (var row in ingested)
            {
                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = row.Id,
                    ["text"] = row.Text,
                    ["embedding"] = row.Embedding,
                    ["row_json"] = row.RowJson
                };
            }
        }
    }
}
