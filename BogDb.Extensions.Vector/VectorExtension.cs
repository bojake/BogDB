using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BogDb.Core.Catalog;
using BogDb.Core.Extension;
using BogDb.Core.Main;

namespace BogDb.Extensions.Vector
{
    public class VectorExtension : IExtension
    {
        public string Name => "vector";

        public void Load(BogDatabase database)
        {
            var indexRegistry = new VectorIndexRegistry();
            indexRegistry.Load(database);
            var createVectorIndex = new CreateVectorIndexTableFunction(database, indexRegistry);
            database.FunctionRegistry.Register(createVectorIndex);
            database.StandaloneTableFunctionRegistry.Register(createVectorIndex);
            var dropVectorIndex = new DropVectorIndexTableFunction(database, indexRegistry);
            database.FunctionRegistry.Register(dropVectorIndex);
            database.StandaloneTableFunctionRegistry.Register(dropVectorIndex);
            var queryVectorIndex = new QueryVectorIndexTableFunction(database, indexRegistry);
            database.FunctionRegistry.Register(queryVectorIndex);
            database.StandaloneTableFunctionRegistry.Register(queryVectorIndex);
            var showVectorIndexes = new ShowVectorIndexesTableFunction(indexRegistry);
            database.FunctionRegistry.Register(showVectorIndexes);
            database.StandaloneTableFunctionRegistry.Register(showVectorIndexes);
            var showIndexes = new ShowIndexesTableFunction(indexRegistry);
            database.FunctionRegistry.Register(showIndexes);
            database.StandaloneTableFunctionRegistry.Register(showIndexes);
            database.ScalarFunctionRegistry.Register("vector_inner_product", VectorScalarFunctions.InnerProduct);
            database.ScalarFunctionRegistry.Register("vector_dot_product", VectorScalarFunctions.InnerProduct);
            database.ScalarFunctionRegistry.Register("vector_cosine_similarity", VectorScalarFunctions.CosineSimilarity);
            database.ScalarFunctionRegistry.Register("vector_cosine_distance", VectorScalarFunctions.CosineDistance);
            database.ScalarFunctionRegistry.Register("vector_distance", VectorScalarFunctions.L2Distance);
            database.ScalarFunctionRegistry.Register("vector_l2_distance", VectorScalarFunctions.L2Distance);
            database.ScalarFunctionRegistry.Register("vector_squared_distance", VectorScalarFunctions.SquaredDistance);
            database.ScalarFunctionRegistry.Register("vector_l1_distance", VectorScalarFunctions.L1Distance);
            database.ScalarFunctionRegistry.Register("vector_normalize", VectorScalarFunctions.Normalize);
            database.ScalarFunctionRegistry.Register("array_inner_product", VectorScalarFunctions.InnerProduct);
            database.ScalarFunctionRegistry.Register("array_dot_product", VectorScalarFunctions.InnerProduct);
            database.ScalarFunctionRegistry.Register("array_cosine_similarity", VectorScalarFunctions.CosineSimilarity);
            database.ScalarFunctionRegistry.Register("array_cosine_distance", VectorScalarFunctions.CosineDistance);
            database.ScalarFunctionRegistry.Register("array_distance", VectorScalarFunctions.L2Distance);
            database.ScalarFunctionRegistry.Register("array_l2_distance", VectorScalarFunctions.L2Distance);
            database.ScalarFunctionRegistry.Register("array_squared_distance", VectorScalarFunctions.SquaredDistance);
            database.ScalarFunctionRegistry.Register("array_l1_distance", VectorScalarFunctions.L1Distance);
            database.ScalarFunctionRegistry.Register("vector_cross_product", VectorScalarFunctions.CrossProduct);
            database.ScalarFunctionRegistry.Register("array_cross_product", VectorScalarFunctions.CrossProduct);
            database.ScalarFunctionRegistry.Register("array_normalize", VectorScalarFunctions.Normalize);
        }
    }

    internal static class VectorScalarFunctions
    {
        public static object? InnerProduct(object?[] args)
        {
            var (left, right) = RequirePair(args);
            var result = 0f;
            for (var i = 0; i < left.Length; i++)
                result += left[i] * right[i];
            return result;
        }

        public static object? CosineSimilarity(object?[] args)
        {
            var (left, right) = RequirePair(args);
            return SimilaritySearch.CosineSimilarity(left, right);
        }

        public static object? CosineDistance(object?[] args)
        {
            var similarity = (float)CosineSimilarity(args)!;
            return 1f - similarity;
        }

        public static object? L2Distance(object?[] args)
        {
            var squared = (float)SquaredDistance(args)!;
            return MathF.Sqrt(squared);
        }

        public static object? SquaredDistance(object?[] args)
        {
            var (left, right) = RequirePair(args);
            var result = 0f;
            for (var i = 0; i < left.Length; i++)
            {
                var delta = left[i] - right[i];
                result += delta * delta;
            }

            return result;
        }

        public static object? L1Distance(object?[] args)
        {
            var (left, right) = RequirePair(args);
            var result = 0f;
            for (var i = 0; i < left.Length; i++)
                result += MathF.Abs(left[i] - right[i]);
            return result;
        }

        public static object? Normalize(object?[] args)
        {
            if (args.Length != 1)
                throw new ArgumentException("vector_normalize requires exactly one vector argument.");

            var vector = ToFloatArray(args[0]);
            var norm = 0f;
            for (var i = 0; i < vector.Length; i++)
                norm += vector[i] * vector[i];

            if (norm == 0f)
                return vector.Select(value => (object?)0f).ToList();

            var magnitude = MathF.Sqrt(norm);
            return vector.Select(value => (object?)(value / magnitude)).ToList();
        }

        public static object? CrossProduct(object?[] args)
        {
            var (left, right) = RequirePair(args);
            if (left.Length != 3 || right.Length != 3)
                throw new ArgumentException("Cross product requires 3-dimensional vectors.");

            return new List<object?>
            {
                (object?)(left[1] * right[2] - left[2] * right[1]),
                (object?)(left[2] * right[0] - left[0] * right[2]),
                (object?)(left[0] * right[1] - left[1] * right[0])
            };
        }

        private static (float[] Left, float[] Right) RequirePair(object?[] args)
        {
            if (args.Length != 2)
                throw new ArgumentException("Vector functions require exactly two vector arguments.");

            var left = ToFloatArray(args[0]);
            var right = ToFloatArray(args[1]);
            if (left.Length != right.Length)
                throw new ArgumentException("Vectors must have the same length.");

            return (left, right);
        }

        internal static float[] ToFloatArray(object? value)
        {
            if (value is null)
                throw new ArgumentException("Vector argument cannot be null.");
            if (value is string || value is not IEnumerable enumerable)
                throw new ArgumentException("Vector argument must be a list of numeric values.");

            var values = new List<float>();
            foreach (var item in enumerable)
            {
                if (item is null)
                    throw new ArgumentException("Vector elements cannot be null.");

                try
                {
                    values.Add(Convert.ToSingle(item));
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    throw new ArgumentException("Vector argument must be a list of numeric values.", ex);
                }
            }

            return values.ToArray();
        }
    }

    internal sealed record VectorIndexDefinition(
        string TableName,
        string IndexName,
        string PropertyName,
        string Metric,
        int Mu = 30,
        int Ml = 60,
        double Pu = 0.05d,
        double Alpha = 1.1d,
        int Efc = 200,
        bool CacheEmbeddings = true);

    internal sealed class VectorIndexRegistry
    {
        private readonly Dictionary<string, VectorIndexDefinition> _definitions =
            new(StringComparer.OrdinalIgnoreCase);

        // Live HNSW graph instances, built on create_vector_index and used during query
        private readonly Dictionary<string, HnswGraph> _hnswGraphs =
            new(StringComparer.OrdinalIgnoreCase);

        // Mapping from HNSW internal node ID → external (primary key) ID
        private readonly Dictionary<string, Dictionary<int, object?>> _nodeIdMaps =
            new(StringComparer.OrdinalIgnoreCase);

        public void Add(VectorIndexDefinition definition)
        {
            var key = BuildKey(definition.TableName, definition.IndexName);
            if (_definitions.ContainsKey(key))
                throw new ArgumentException(
                    $"Index {definition.IndexName} already exists in table {definition.TableName}.");
            _definitions[key] = definition;
        }

        public void AddOrReplace(VectorIndexDefinition definition)
            => _definitions[BuildKey(definition.TableName, definition.IndexName)] = definition;

        public VectorIndexDefinition GetRequired(string tableName, string indexName)
        {
            var key = BuildKey(tableName, indexName);
            if (_definitions.TryGetValue(key, out var definition))
                return definition;

            throw new ArgumentException($"Table {tableName} doesn't have an index with name {indexName}.");
        }

        public bool TryGet(string tableName, string indexName, out VectorIndexDefinition? definition)
            => _definitions.TryGetValue(BuildKey(tableName, indexName), out definition);

        public void Remove(string tableName, string indexName)
        {
            var key = BuildKey(tableName, indexName);
            if (!_definitions.Remove(key))
                throw new ArgumentException($"Table {tableName} doesn't have an index with name {indexName}.");
            _hnswGraphs.Remove(key);
            _nodeIdMaps.Remove(key);
        }

        public void SetGraph(string tableName, string indexName, HnswGraph graph, Dictionary<int, object?> nodeIdMap)
        {
            var key = BuildKey(tableName, indexName);
            _hnswGraphs[key] = graph;
            _nodeIdMaps[key] = nodeIdMap;
        }

        public bool TryGetGraph(string tableName, string indexName, out HnswGraph? graph, out Dictionary<int, object?>? nodeIdMap)
        {
            var key = BuildKey(tableName, indexName);
            if (_hnswGraphs.TryGetValue(key, out graph) && _nodeIdMaps.TryGetValue(key, out nodeIdMap))
                return true;
            nodeIdMap = null;
            return false;
        }

        public IEnumerable<VectorIndexDefinition> GetAll()
            => _definitions.Values
                .OrderBy(definition => definition.TableName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(definition => definition.IndexName, StringComparer.OrdinalIgnoreCase);

        public bool HasAnyForProperty(string tableName, string propertyName)
            => _definitions.Values.Any(definition =>
                string.Equals(definition.TableName, tableName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(definition.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase));

        public void Save(BogDatabase database)
        {
            var path = GetMetadataPath(database);
            if (path == null)
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(
                _definitions.Values.OrderBy(definition => definition.TableName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(definition => definition.IndexName, StringComparer.OrdinalIgnoreCase)
                    .ToList());
            File.WriteAllText(path, json);
        }

        public void Load(BogDatabase database)
        {
            var path = GetMetadataPath(database);
            if (path == null || !File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var definitions = JsonSerializer.Deserialize<List<VectorIndexDefinition>>(json);
            if (definitions == null)
                return;

            foreach (var definition in definitions)
                AddOrReplace(definition);
        }

        private static string? GetMetadataPath(BogDatabase database)
        {
            if (string.Equals(database.DatabasePath, ":memory:", StringComparison.OrdinalIgnoreCase))
                return null;

            return Path.Combine(database.DatabasePath, "extensions", "vector.indexes.json");
        }

        private static string BuildKey(string tableName, string indexName) => $"{tableName}::{indexName}";
    }

    internal static class VectorSchemaValidation
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

    internal static class VectorMetric
    {
        internal static string NormalizeMetric(string? metric)
        {
            if (string.IsNullOrWhiteSpace(metric))
                return "cosine";

            return metric.Trim().ToLowerInvariant() switch
            {
                "cosine" => "cosine",
                "l2" => "l2",
                "l2sq" => "l2",
                "dotproduct" => "dotproduct",
                "dot_product" => "dotproduct",
                _ => throw new ArgumentException("Metric must be one of COSINE, L2, L2SQ or DOTPRODUCT.")
            };
        }

        internal static double Distance(string metric, float[] left, float[] right)
        {
            if (left.Length != right.Length)
                throw new ArgumentException("Vectors must have the same length.");

            return metric switch
            {
                "cosine" => 1d - SimilaritySearch.CosineSimilarity(left, right),
                "l2" => Convert.ToDouble(VectorScalarFunctions.L2Distance(new object?[] { left, right })),
                "dotproduct" => -Convert.ToDouble(VectorScalarFunctions.InnerProduct(new object?[] { left, right })),
                _ => throw new ArgumentException($"Unsupported vector metric '{metric}'.")
            };
        }
    }

    internal static class VectorOptionReader
    {
        public static bool ReadBool(VectorOptionalArguments optional, string name, bool defaultValue)
        {
            if (!optional.TryGet(name, out var value))
                return defaultValue;
            return value switch
            {
                bool boolValue => boolValue,
                string text when bool.TryParse(text, out var parsed) => parsed,
                _ => throw new ArgumentException($"{name} must be a boolean.")
            };
        }

        public static int ReadInt(
            VectorOptionalArguments optional,
            string name,
            int defaultValue,
            int minValue,
            int? maxValue,
            string errorMessage,
            bool requiredWhenPresent = true)
        {
            if (!optional.TryGet(name, out var value))
                return defaultValue;

            if (!requiredWhenPresent && value is null)
                return defaultValue;

            var parsed = value switch
            {
                int intValue => intValue,
                long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
                string text when int.TryParse(text, out var fromText) => fromText,
                _ => throw new ArgumentException(errorMessage)
            };

            if (parsed < minValue || (maxValue.HasValue && parsed > maxValue.Value))
                throw new ArgumentException(errorMessage);
            return parsed;
        }

        public static double ReadDouble(
            VectorOptionalArguments optional,
            string name,
            double defaultValue,
            double minValue,
            double? maxValue,
            string errorMessage,
            bool requiredWhenPresent = true)
        {
            if (!optional.TryGet(name, out var value))
                return defaultValue;

            if (!requiredWhenPresent && value is null)
                return defaultValue;

            var parsed = value switch
            {
                double doubleValue => doubleValue,
                float floatValue => floatValue,
                int intValue => intValue,
                long longValue => longValue,
                string text when double.TryParse(text, out var fromText) => fromText,
                _ => throw new ArgumentException(errorMessage)
            };

            if (parsed < minValue || (maxValue.HasValue && parsed > maxValue.Value))
                throw new ArgumentException(errorMessage);
            return parsed;
        }
    }

    internal sealed class VectorOptionalArguments
    {
        private readonly Dictionary<string, object?> _values;

        private VectorOptionalArguments(Dictionary<string, object?> values)
        {
            _values = values;
        }

        public static (List<object?> Positional, VectorOptionalArguments Optional) Parse(
            IReadOnlyList<object?> args,
            params string[] recognizedNames)
        {
            var positional = new List<object?>(args.Count);
            var optionValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var arg in args)
            {
                if (arg is not NamedFunctionArgument named)
                {
                    positional.Add(arg);
                    continue;
                }

                if (!recognizedNames.Contains(named.Name, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException($"Unrecognized optional parameter {named.Name}.");
                optionValues[named.Name] = named.Value;
            }

            return (positional, new VectorOptionalArguments(optionValues));
        }

        public bool TryGet(string name, out object? value)
            => _values.TryGetValue(name, out value);
    }

    internal sealed class CreateVectorIndexTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly VectorIndexRegistry _registry;

        public CreateVectorIndexTableFunction(BogDatabase database, VectorIndexRegistry registry)
        {
            _database = database;
            _registry = registry;
        }

        public string Name => "create_vector_index";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("table", "STRING"),
                ("index", "STRING"),
                ("property", "STRING"),
                ("metric", "STRING")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            var (positional, optional) = VectorOptionalArguments.Parse(
                args,
                "skip_if_exists",
                "metric",
                "mu",
                "ml",
                "pu",
                "alpha",
                "efc",
                "cache_embeddings");

            if (positional.Count < 3 || positional.Count > 4)
                throw new ArgumentException(
                    "create_vector_index requires table name, index name, property name, and optional metric.");

            if (positional[0] is not string tableName || string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("create_vector_index requires the first argument to be a node table name.");
            if (positional[1] is not string indexName || string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("create_vector_index requires the second argument to be an index name.");
            if (positional[2] is not string propertyName || string.IsNullOrWhiteSpace(propertyName))
                throw new ArgumentException("create_vector_index requires the third argument to be a property name.");

            if (_database.Catalog.GetTableCatalogEntry(null, tableName, useInternal: false) is not BogDb.Core.Catalog.NodeTableCatalogEntry entry)
                throw new ArgumentException($"create_vector_index requires an existing node table: {tableName}");
            if (!entry.ContainsProperty(propertyName))
                throw new ArgumentException($"Property '{propertyName}' does not exist on table '{tableName}'.");
            VectorSchemaValidation.ValidateEmbeddingProperty(entry, propertyName, tableName, "create_vector_index");

            var metricArg = positional.Count >= 4 ? positional[3]?.ToString() : null;
            if (optional.TryGet("metric", out var metricOverride))
                metricArg = metricOverride?.ToString();

            var skipIfExists = VectorOptionReader.ReadBool(optional, "skip_if_exists", false);
            var definition = new VectorIndexDefinition(
                tableName,
                indexName,
                propertyName,
                VectorMetric.NormalizeMetric(metricArg),
                VectorOptionReader.ReadInt(optional, "mu", 30, 1, 26213, "Mu must be a positive integer between 1 and 26213."),
                VectorOptionReader.ReadInt(optional, "ml", 60, 1, 26213, "Ml must be a positive integer between 1 and 26213."),
                VectorOptionReader.ReadDouble(optional, "pu", 0.05d, 0d, 1d, "Pu must be a double between 0 and 1."),
                VectorOptionReader.ReadDouble(optional, "alpha", 1.1d, 1d, null, "Alpha must be a double greater than or equal to 1."),
                VectorOptionReader.ReadInt(optional, "efc", 200, 1, null, "Efc must be a positive integer."),
                VectorOptionReader.ReadBool(optional, "cache_embeddings", true));

            if (skipIfExists && _registry.TryGet(tableName, indexName, out _))
                yield break;

            _registry.Add(definition);
            _database.Catalog.CreateIndexEntry(tableName, propertyName);
            _registry.Save(_database);

            // Build the HNSW graph from existing data
            BuildHnswGraph(tableName, indexName, definition);

            yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["table"] = tableName,
                ["index"] = indexName,
                ["property"] = propertyName,
                ["metric"] = definition.Metric
            };
        }

        private void BuildHnswGraph(string tableName, string indexName, VectorIndexDefinition definition)
        {
            Func<float[], float[], double> distFunc = definition.Metric switch
            {
                "cosine" => (a, b) => 1.0 - SimilaritySearch.CosineSimilarity(a, b),
                "dotproduct" => (a, b) => -Convert.ToDouble(VectorScalarFunctions.InnerProduct(new object?[] { a, b })),
                _ => (a, b) =>
                {
                    var sum = 0.0;
                    for (var i = 0; i < a.Length; i++) { var d = a[i] - b[i]; sum += d * d; }
                    return sum;
                }
            };

            var graph = new HnswGraph(m: definition.Mu, efConstruction: definition.Efc, distanceFunc: distFunc);
            var nodeIdMap = new Dictionary<int, object?>();

            var entry = _database.Catalog.GetTableCatalogEntry(null, tableName, useInternal: false) as NodeTableCatalogEntry;
            if (entry == null) return;

            var primaryKey = ResolvePrimaryKeyName(entry);

            foreach (var row in _database.EnumerateNodeRows(tableName))
            {
                var properties = row.Value;
                if (!properties.TryGetValue(definition.PropertyName, out var embeddingValue) || embeddingValue is null)
                    continue;

                var embedding = VectorScalarFunctions.ToFloatArray(embeddingValue);
                var rowId = properties.TryGetValue(primaryKey, out var propertyId) ? propertyId : row.Key;
                var hnswId = graph.Insert(embedding, rowId);
                nodeIdMap[hnswId] = rowId;
            }

            _registry.SetGraph(tableName, indexName, graph, nodeIdMap);
        }

        private static string ResolvePrimaryKeyName(NodeTableCatalogEntry entry)
        {
            foreach (var property in entry.GetProperties())
            {
                if (entry.GetPropertyID(property.Name) == entry.PrimaryKeyPropertyID)
                    return property.Name;
            }
            return "id";
        }
    }

    internal sealed class DropVectorIndexTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly VectorIndexRegistry _registry;

        public DropVectorIndexTableFunction(BogDatabase database, VectorIndexRegistry registry)
        {
            _database = database;
            _registry = registry;
        }

        public string Name => "drop_vector_index";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("table", "STRING"),
                ("index", "STRING")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            var (positional, optional) = VectorOptionalArguments.Parse(args, "skip_if_not_exists");

            if (positional.Count != 2)
                throw new ArgumentException("drop_vector_index requires table name and index name.");

            if (positional[0] is not string tableName || string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("drop_vector_index requires the first argument to be a node table name.");
            if (positional[1] is not string indexName || string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("drop_vector_index requires the second argument to be an index name.");

            var skipIfNotExists = VectorOptionReader.ReadBool(optional, "skip_if_not_exists", false);
            _ = _database.Catalog.GetTableCatalogEntry(null, tableName, useInternal: false)
                ?? throw new ArgumentException($"drop_vector_index requires an existing table: {tableName}");
            VectorIndexDefinition? skippedDefinition = null;
            if (skipIfNotExists && !_registry.TryGet(tableName, indexName, out skippedDefinition))
                yield break;

            var definition = skippedDefinition ?? _registry.GetRequired(tableName, indexName);
            _registry.Remove(tableName, indexName);
            if (!_registry.HasAnyForProperty(tableName, definition.PropertyName))
                _database.Catalog.DropIndexEntry(
                    new BogDb.Core.Transaction.Transaction(BogDb.Core.Transaction.TransactionType.WRITE),
                    tableName,
                    definition.PropertyName);
            _registry.Save(_database);

            yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["table"] = tableName,
                ["index"] = indexName
            };
        }
    }

    internal sealed class QueryVectorIndexTableFunction : ITableFunction
    {
        private readonly BogDatabase _database;
        private readonly VectorIndexRegistry _registry;

        public QueryVectorIndexTableFunction(BogDatabase database, VectorIndexRegistry registry)
        {
            _database = database;
            _registry = registry;
        }

        public string Name => "query_vector_index";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("rank", "INT64"),
                ("id", "ANY"),
                ("distance", "DOUBLE"),
                ("embedding", "LIST")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            var (positional, optional) = VectorOptionalArguments.Parse(
                args,
                "metric",
                "efs",
                "blind_search_up_sel",
                "directed_search_up_sel");

            if (positional.Count < 4 || positional.Count > 5)
                throw new ArgumentException(
                    "query_vector_index requires table name, index name, query vector, k, and optional metric override.");

            if (positional[0] is not string tableName || string.IsNullOrWhiteSpace(tableName))
                throw new ArgumentException("query_vector_index requires the first argument to be a node table name.");
            if (positional[1] is not string indexName || string.IsNullOrWhiteSpace(indexName))
                throw new ArgumentException("query_vector_index requires the second argument to be an index name.");

            var definition = _registry.GetRequired(tableName, indexName);
            var metric = definition.Metric;
            if (_database.Catalog.GetTableCatalogEntry(null, tableName, useInternal: false) is not NodeTableCatalogEntry entry)
                throw new ArgumentException($"query_vector_index requires an existing node table: {tableName}");
            VectorSchemaValidation.ValidateEmbeddingProperty(entry, definition.PropertyName, tableName, "query_vector_index");
            if (optional.TryGet("metric", out var namedMetric))
                metric = VectorMetric.NormalizeMetric(namedMetric?.ToString());
            else if (positional.Count >= 5 && positional[^1] is string)
                metric = VectorMetric.NormalizeMetric(positional[^1]?.ToString());



            var blindSearchUpperSelectivity = VectorOptionReader.ReadDouble(optional, "blind_search_up_sel", 0d, 0d, 1d,
                "Blind search upper selectivity threshold must be a double between 0 and 1.", requiredWhenPresent: false);
            var directedSearchUpperSelectivity = VectorOptionReader.ReadDouble(optional, "directed_search_up_sel", 0d, 0d, 1d,
                "Directed search upper selectivity threshold must be a double between 0 and 1.", requiredWhenPresent: false);
            if (optional.TryGet("blind_search_up_sel", out _) &&
                optional.TryGet("directed_search_up_sel", out _) &&
                blindSearchUpperSelectivity >= directedSearchUpperSelectivity)
            {
                throw new ArgumentException(
                    $"Blind search upper selectivity threshold is set to {blindSearchUpperSelectivity:0.000000}, but the directed search upper selectivity threshold is set to {directedSearchUpperSelectivity:0.000000}. The blind search upper selectivity threshold must be less than the directed search upper selectivity threshold.");
            }

            var hasMetricOverride = positional.Count >= 5 && positional[^1] is string;
            var limit = ParseLimit(hasMetricOverride ? positional[^2] : positional[^1]);
            var queryVector = ParseQueryVector(positional, hasMetricOverride);
            var efSearch = VectorOptionReader.ReadInt(optional, "efs", 0, 1, null,
                "Efs must be a positive integer.", requiredWhenPresent: false);

            // ── Fast path: HNSW graph search O(log n) ────────────────────────
            if (_registry.TryGetGraph(tableName, indexName, out var hnswGraph, out var nodeIdMap) &&
                hnswGraph != null && nodeIdMap != null && hnswGraph.Count > 0)
            {
                var results = hnswGraph.Search(queryVector, limit, efSearch);
                var rank = 1L;
                foreach (var result in results)
                {
                    yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["rank"] = rank++,
                        ["id"] = result.ExternalId,
                        ["distance"] = result.Distance,
                        ["embedding"] = result.Vector.Select(v => (object?)v).ToList()
                    };
                }
                yield break;
            }

            // ── Fallback: brute-force scan O(n) ──────────────────────────────
            var primaryKey = ResolvePrimaryKeyName(entry);
            var candidates = new List<(object? Id, double Distance, List<object?> Embedding)>();
            foreach (var row in _database.EnumerateNodeRows(tableName))
            {
                var properties = row.Value;
                if (!properties.TryGetValue(definition.PropertyName, out var embeddingValue) || embeddingValue is null)
                    continue;

                var embedding = VectorScalarFunctions.ToFloatArray(embeddingValue);
                var rowId = properties.TryGetValue(primaryKey, out var propertyId) ? propertyId : row.Key;
                candidates.Add((
                    rowId,
                    VectorMetric.Distance(metric, queryVector, embedding),
                    embedding.Select(value => (object?)value).ToList()));
            }

            var rank2 = 1L;
            foreach (var candidate in candidates
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Id?.ToString(), StringComparer.Ordinal)
                .Take(limit))
            {
                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rank"] = rank2++,
                    ["id"] = candidate.Id,
                    ["distance"] = candidate.Distance,
                    ["embedding"] = candidate.Embedding
                };
            }
        }

        private static int ParseLimit(object? value)
        {
            if (value is int intValue && intValue > 0)
                return intValue;
            if (value is long longValue && longValue > 0 && longValue <= int.MaxValue)
                return (int)longValue;
            if (value is string text && int.TryParse(text, out var parsed) && parsed > 0)
                return parsed;

            throw new ArgumentException("query_vector_index k must be a positive integer.");
        }

        private static float[] ParseQueryVector(IReadOnlyList<object?> args, bool hasMetricOverride)
        {
            var vectorArgCount = hasMetricOverride ? args.Count - 4 : args.Count - 3;
            if (vectorArgCount == 1)
                return VectorScalarFunctions.ToFloatArray(args[2]);

            var values = new List<float>(vectorArgCount);
            for (var i = 2; i < args.Count - (hasMetricOverride ? 2 : 1); i++)
            {
                try
                {
                    values.Add(Convert.ToSingle(args[i]));
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    throw new ArgumentException("query_vector_index requires a numeric query vector.", ex);
                }
            }

            return values.ToArray();
        }

        private static string ResolvePrimaryKeyName(BogDb.Core.Catalog.NodeTableCatalogEntry entry)
        {
            foreach (var property in entry.GetProperties())
            {
                if (entry.GetPropertyID(property.Name) == entry.PrimaryKeyPropertyID)
                    return property.Name;
            }

            return "id";
        }
    }

    internal sealed class ShowVectorIndexesTableFunction : ITableFunction
    {
        private readonly VectorIndexRegistry _registry;

        public ShowVectorIndexesTableFunction(VectorIndexRegistry registry)
        {
            _registry = registry;
        }

        public string Name => "show_vector_indexes";

        public IReadOnlyList<(string Name, string Type)>? Schema =>
            new List<(string Name, string Type)>
            {
                ("table", "STRING"),
                ("index", "STRING"),
                ("type", "STRING"),
                ("properties", "LIST"),
                ("enabled", "BOOL"),
                ("definition", "STRING")
            };

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
        {
            if (args.Count > 1)
                throw new ArgumentException("show_vector_indexes accepts at most one optional table-name argument.");

            var tableFilter = args.Count == 1 ? args[0]?.ToString() : null;
            foreach (var definition in _registry.GetAll())
            {
                if (!string.IsNullOrWhiteSpace(tableFilter) &&
                    !string.Equals(definition.TableName, tableFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["table"] = definition.TableName,
                    ["index"] = definition.IndexName,
                    ["type"] = "HNSW",
                    ["properties"] = new List<object?> { definition.PropertyName },
                    ["enabled"] = true,
                    ["definition"] =
                        $"CALL CREATE_VECTOR_INDEX('{definition.TableName}', '{definition.IndexName}', '{definition.PropertyName}', metric := '{definition.Metric}')"
                };
            }
        }
    }

    internal sealed class ShowIndexesTableFunction : ITableFunction
    {
        private readonly ShowVectorIndexesTableFunction _inner;

        public ShowIndexesTableFunction(VectorIndexRegistry registry)
        {
            _inner = new ShowVectorIndexesTableFunction(registry);
        }

        public string Name => "show_indexes";

        public IReadOnlyList<(string Name, string Type)>? Schema => _inner.Schema;

        public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
            => _inner.Invoke(args);
    }
}
