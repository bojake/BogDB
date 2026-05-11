using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using BogDb.Core.Main;
using BogDb.Extensions.LLM;
using BogDb.Extensions.Vector;

namespace BogDb.Tests.Extension
{
    public class VectorExtensionTests
    {
        private sealed class TextAwareFakeLlmProvider : ILlmProvider
        {
            public Task<float[]> GenerateEmbeddingAsync(string text)
            {
                return text switch
                {
                    "left" => Task.FromResult(new[] { 1.0f, 0.0f, 0.0f }),
                    "right" => Task.FromResult(new[] { 0.0f, 1.0f, 0.0f }),
                    "same" => Task.FromResult(new[] { 1.0f, 0.0f, 0.0f }),
                    _ => Task.FromResult(new[] { 1.0f, 1.0f, 0.0f })
                };
            }
        }

        [Fact]
        public void SimilaritySearch_CosineSimilarity_ComputesPrecisionProperlyUnderHardwareIntrinsics()
        {
            // Create two vectors matching Float Array topological bounds perfectly
            float[] vectorA = { 1.0f, 2.0f, 3.0f };
            float[] vectorB = { 4.0f, 5.0f, 6.0f };
            
            // Expected Cosine calculation: (A . B) / (||A|| * ||B||)
            // (1*4 + 2*5 + 3*6) / (sqrt(1+4+9) * sqrt(16+25+36))
            // 32 / (sqrt(14) * sqrt(77)) = 32 / (3.74165 * 8.7749) = 32 / 32.8329 = 0.97463
            float expected = 0.974631846f;

            float result = SimilaritySearch.CosineSimilarity(vectorA.AsSpan(), vectorB.AsSpan());

            Assert.Equal(expected, result, 5); // Accurate to 5 precision bounds!
        }

        [Fact]
        public void SimilaritySearch_CosineSimilarity_ComputesVeryLargeSIMDVectorsCorrectly()
        {
            // Instantiate 10,000 floats mapped natively explicitly mapping Vector<float> Loop Unrolling
            float[] vectorA = new float[10000];
            float[] vectorB = new float[10000];
            
            for (int i = 0; i < 10000; i++)
            {
                vectorA[i] = 1.0f;
                vectorB[i] = 1.0f;
            }

            // Cosine of two identical vectors is always exactly 1.0 f
            float result = SimilaritySearch.CosineSimilarity(vectorA.AsSpan(), vectorB.AsSpan());
            
            // Ensure bounds mapped efficiently without floating point degradation
            Assert.Equal(1.0f, result, 4);
        }
        
        [Fact]
        public void SimilaritySearch_ThrowsArgumentException_WhenSIMDLengthsMisMatch()
        {
            float[] vectorA = { 1.0f, 2.0f };
            float[] vectorB = { 4.0f, 5.0f, 6.0f };

            Assert.Throws<ArgumentException>(() => 
            {
                SimilaritySearch.CosineSimilarity(vectorA.AsSpan(), vectorB.AsSpan());
            });
        }

        [Fact]
        public void LoadExtension_RegistersVectorScalarFunctions()
        {
            using var db = BogDatabase.CreateInMemory();

            new VectorExtension().Load(db);

            Assert.True(db.ScalarFunctionRegistry.Contains("vector_inner_product"));
            Assert.True(db.ScalarFunctionRegistry.Contains("vector_cosine_similarity"));
            Assert.True(db.ScalarFunctionRegistry.Contains("vector_distance"));
            Assert.True(db.ScalarFunctionRegistry.Contains("vector_normalize"));
            Assert.True(db.ScalarFunctionRegistry.Contains("array_distance"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("create_vector_index"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("query_vector_index"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("drop_vector_index"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("show_vector_indexes"));
        }

        [Fact]
        public void ReturnVectorCosineSimilarity_UsesRegisteredScalarFunction()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query(
                "RETURN vector_cosine_similarity([1.0, 0.0, 0.0], [1.0, 0.0, 0.0]) AS similarity");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            Assert.Equal(1.0, result.GetNext().GetDouble(0), 4);
        }

        [Fact]
        public void ReturnVectorCosineSimilarity_ComposesWithLlmEmbeddings()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new TextAwareFakeLlmProvider()).Load(db);
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query(
                "RETURN vector_cosine_similarity(llm_embed('same'), llm_embed('left')) AS same_score, " +
                "vector_cosine_similarity(llm_embed('left'), llm_embed('right')) AS orthogonal_score");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var row = result.GetNext();
            Assert.Equal(1.0, row.GetDouble(0), 4);
            Assert.Equal(0.0, row.GetDouble(1), 4);
        }

        [Fact]
        public void ReturnVectorNormalize_ReturnsNormalizedVector()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("RETURN vector_normalize([3.0, 4.0])");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var values = Assert.IsAssignableFrom<IEnumerable>(result.GetNext().GetValue(0))
                .Cast<object?>()
                .ToArray();
            Assert.Equal(0.6f, Assert.IsType<float>(values[0]), 4);
            Assert.Equal(0.8f, Assert.IsType<float>(values[1]), 4);
        }

        [Fact]
        public void ReturnArrayDistance_UsesNativeStyleAlias()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("RETURN array_distance([0.0, 0.0], [3.0, 4.0])");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            Assert.Equal(5.0, result.GetNext().GetDouble(0), 4);
        }

        [Fact]
        public void CallQueryVectorIndex_SearchesStoredEmbeddings()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', embedding:[1.0, 0.0]})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'banana-doc', embedding:[0.9, 0.1]})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'car-doc', embedding:[0.0, 1.0]})").IsSuccess);

            var createIndex = conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding', 'cosine') RETURN *");
            Assert.True(createIndex.IsSuccess, createIndex.ErrorMessage);
            Assert.True(createIndex.HasNext());

            var result = conn.Query("CALL query_vector_index('Document', 'doc_vec', [1.0, 0.0], 2) RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(new[] { "rank", "id", "distance", "embedding" }, result.ColumnNames);

            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal(1L, first.GetInt64(0));
            Assert.Equal("apple-doc", first.GetString(1));
            Assert.Equal(0.0, first.GetDouble(2), 5);

            Assert.True(result.HasNext());
            var second = result.GetNext();
            Assert.Equal("banana-doc", second.GetString(1));
            Assert.True(second.GetDouble(2) < 0.01);
        }

        [Fact]
        public void CallQueryVectorIndex_ComposesWithLlmStoredEmbeddings()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new TextAwareFakeLlmProvider()).Load(db);
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'left-doc', body:'left', embedding: llm_embed('left')})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'right-doc', body:'right', embedding: llm_embed('right')})").IsSuccess);

            Assert.True(conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding', 'cosine') RETURN *").IsSuccess);

            var result = conn.Query("CALL query_vector_index('Document', 'doc_vec', [1.0, 0.0, 0.0], 2) RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal("left-doc", first.GetString(1));
            Assert.Equal(0.0, first.GetDouble(2), 5);
        }

        [Fact]
        public void CallDropVectorIndex_RemovesSearchRegistration()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding') RETURN *").IsSuccess);
            Assert.True(conn.Query("CALL drop_vector_index('Document', 'doc_vec') RETURN *").IsSuccess);

            var result = conn.Query("CALL query_vector_index('Document', 'doc_vec', [1.0, 0.0], 1) RETURN *");

            Assert.False(result.IsSuccess);
            Assert.Contains("doesn't have an index", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CallShowVectorIndexes_ReturnsNativeStyleMetadata()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding', 'l2') RETURN *").IsSuccess);

            var result = conn.Query("CALL show_vector_indexes() RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(new[] { "table", "index", "type", "properties", "enabled", "definition" }, result.ColumnNames);
            Assert.True(result.HasNext());
            var row = result.GetNext();
            Assert.Equal("Document", row.GetString(0));
            Assert.Equal("doc_vec", row.GetString(1));
            Assert.Equal("HNSW", row.GetString(2));
            var properties = Assert.IsAssignableFrom<IEnumerable>(row.GetValue(3)).Cast<object?>().ToArray();
            Assert.Single(properties);
            Assert.Equal("embedding", Assert.IsType<string>(properties[0]));
            Assert.True(row.GetBoolean(4));
            Assert.Contains("CREATE_VECTOR_INDEX", row.GetString(5), StringComparison.Ordinal);
        }

        [Fact]
        public void CallShowIndexes_AliasesVectorMetadata()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding') RETURN *").IsSuccess);

            var result = conn.Query("CALL show_indexes() RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var row = result.GetNext();
            Assert.Equal("Document", row.GetString(0));
            Assert.Equal("doc_vec", row.GetString(1));
        }

        [Fact]
        public void CallCreateVectorIndex_SkipIfExistsSuppressesDuplicateError()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding') RETURN *").IsSuccess);

            var duplicate = conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding') RETURN *");
            Assert.False(duplicate.IsSuccess);

            var skipped = conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding', skip_if_exists := true) RETURN *");
            Assert.True(skipped.IsSuccess, skipped.ErrorMessage);
            Assert.False(skipped.HasNext());
        }

        [Fact]
        public void CallDropVectorIndex_SkipIfNotExistsSuppressesMissingError()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);

            var missing = conn.Query("CALL drop_vector_index('Document', 'doc_vec') RETURN *");
            Assert.False(missing.IsSuccess);

            var skipped = conn.Query("CALL drop_vector_index('Document', 'doc_vec', skip_if_not_exists := true) RETURN *");
            Assert.True(skipped.IsSuccess, skipped.ErrorMessage);
            Assert.False(skipped.HasNext());
        }

        [Fact]
        public void CallVectorIndexFunctions_ValidateNativeOptionalParameters()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', embedding:[1.0, 0.0]})").IsSuccess);
            Assert.True(conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding', metric := 'l2', mu := 30, ml := 60, pu := 0.05, alpha := 1.1, efc := 200, cache_embeddings := false) RETURN *").IsSuccess);

            var unknownCreate = conn.Query("CALL create_vector_index('Document', 'bad_vec', 'embedding', unknown_param := 1) RETURN *");
            Assert.False(unknownCreate.IsSuccess);
            Assert.Contains("Unrecognized optional parameter unknown_param", unknownCreate.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            var badEfs = conn.Query("CALL query_vector_index('Document', 'doc_vec', [1.0, 0.0], 1, efs := -1) RETURN *");
            Assert.False(badEfs.IsSuccess);
            Assert.Contains("Efs must be a positive integer", badEfs.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            var badThresholds = conn.Query("CALL query_vector_index('Document', 'doc_vec', [1.0, 0.0], 1, directed_search_up_sel := 0.1, blind_search_up_sel := 0.8) RETURN *");
            Assert.False(badThresholds.IsSuccess);
            Assert.Contains("blind search upper selectivity threshold must be less", badThresholds.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CreateAndDropVectorIndex_UpdatesCoreCatalogIndexEntries()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
            Assert.False(db.Catalog.ContainsIndexEntry("Document", "embedding"));

            Assert.True(conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding') RETURN *").IsSuccess);
            Assert.True(db.Catalog.ContainsIndexEntry("Document", "embedding"));

            Assert.True(conn.Query("CALL drop_vector_index('Document', 'doc_vec') RETURN *").IsSuccess);
            Assert.False(db.Catalog.ContainsIndexEntry("Document", "embedding"));
        }

        [Fact]
        public void CallCreateVectorIndex_RejectsNonFloatArrayProperty()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding INT64[])").IsSuccess);

            var result = conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding') RETURN *");
            Assert.False(result.IsSuccess);
            Assert.Contains("FLOAT[]", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CallQueryVectorIndex_NormalizesDeclaredFloatArrayElements_FromRawStorageValues()
        {
            using var db = BogDatabase.CreateInMemory();
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);

            conn.BeginWriteTransaction();
            var tx = conn.ClientContext.ActiveTransaction!;
            db.NodeTables["Document"].Upsert(tx, "apple-doc", new Dictionary<string, object>
            {
                ["id"] = "apple-doc",
                ["embedding"] = new List<object?> { 1L, 0L }
            });
            db.NodeTables["Document"].Upsert(tx, "car-doc", new Dictionary<string, object>
            {
                ["id"] = "car-doc",
                ["embedding"] = new List<object?> { 0L, 1L }
            });
            conn.Commit();

            Assert.True(conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding', 'cosine') RETURN *").IsSuccess);

            var result = conn.Query("CALL query_vector_index('Document', 'doc_vec', [1.0, 0.0], 1) RETURN *");
            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var row = result.GetNext();
            Assert.Equal("apple-doc", row.GetString(1));
            var embedding = Assert.IsAssignableFrom<IEnumerable>(row.GetValue(3)).Cast<object?>().ToArray();
            Assert.Equal(1.0f, Assert.IsType<float>(embedding[0]));
            Assert.Equal(0.0f, Assert.IsType<float>(embedding[1]));
        }

        [Fact]
        public void Reopen_PreservesVectorIndexDefinitions()
        {
            var path = Path.Combine(Path.GetTempPath(), $"bogdb-vector-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            try
            {
                using (var db = BogDatabase.Open(path))
                {
                    new VectorExtension().Load(db);
                    using var conn = new BogConnection(db);

                    Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, embedding FLOAT[])").IsSuccess);
                    conn.BeginWriteTransaction();
                    conn.UpsertNode("Document", "apple-doc", new Dictionary<string, object>
                    {
                        ["id"] = "apple-doc",
                        ["embedding"] = new List<object?> { 1.0f, 0.0f }
                    });
                    conn.UpsertNode("Document", "car-doc", new Dictionary<string, object>
                    {
                        ["id"] = "car-doc",
                        ["embedding"] = new List<object?> { 0.0f, 1.0f }
                    });
                    conn.Commit();
                    Assert.True(conn.Query("CALL create_vector_index('Document', 'doc_vec', 'embedding', 'cosine') RETURN *").IsSuccess);
                }

                using (var reopened = BogDatabase.Open(path))
                {
                    new VectorExtension().Load(reopened);
                    using var conn = new BogConnection(reopened);

                    var indexes = conn.Query("CALL show_vector_indexes() RETURN *");
                    Assert.True(indexes.IsSuccess, indexes.ErrorMessage);
                    Assert.True(indexes.HasNext());
                    var indexRow = indexes.GetNext();
                    Assert.Equal("Document", indexRow.GetString(0));
                    Assert.Equal("doc_vec", indexRow.GetString(1));

                    var search = conn.Query("CALL query_vector_index('Document', 'doc_vec', [1.0, 0.0], 1) RETURN *");
                    Assert.True(search.IsSuccess, search.ErrorMessage);
                    Assert.True(search.HasNext());
                    var resultRow = search.GetNext();
                    Assert.Equal("apple-doc", resultRow.GetString(1));
                    Assert.Equal(0.0, resultRow.GetDouble(2), 5);
                }
            }
            finally
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
        }
    }
}
