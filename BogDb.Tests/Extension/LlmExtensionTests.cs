using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using BogDb.Extensions.LLM;
using BogDb.Core.Main;
using BogDb.Extensions.Vector;

namespace BogDb.Tests.Extension
{
    public class LlmExtensionTests
    {
        private sealed class FakeLlmProvider : ILlmProvider
        {
            private readonly float[] _embedding;
            private int _calls;

            public FakeLlmProvider(params float[] embedding)
            {
                _embedding = embedding;
            }

            public Task<float[]> GenerateEmbeddingAsync(string text)
            {
                var offset = _calls++;
                var embedding = new float[_embedding.Length];
                for (var i = 0; i < _embedding.Length; i++)
                    embedding[i] = _embedding[i] + offset;
                return Task.FromResult(embedding);
            }
        }

        private sealed class SemanticFakeLlmProvider : ILlmProvider
        {
            public Task<float[]> GenerateEmbeddingAsync(string text)
            {
                return text switch
                {
                    "fruit" => Task.FromResult(new[] { 1.0f, 0.0f }),
                    "apple" => Task.FromResult(new[] { 0.99f, 0.01f }),
                    "banana" => Task.FromResult(new[] { 0.95f, 0.05f }),
                    "car" => Task.FromResult(new[] { 0.0f, 1.0f }),
                    _ => Task.FromResult(new[] { 0.5f, 0.5f })
                };
            }
        }

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Fake OpenAI response dynamically mapped
                string mockResponse = @"{
                    ""data"": [{
                        ""embedding"": [0.1, 0.2, 0.3, 0.4]
                    }]
                }";

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(mockResponse)
                });
            }
        }

        [Fact]
        public async Task OpenAiProvider_GenerateEmbeddingAsync_ExtractsFloatsSuccessfully()
        {
            var mockClient = new HttpClient(new MockHttpMessageHandler());
            var provider = new OpenAiProvider("fake_key", mockClient);

            float[] embedding = await provider.GenerateEmbeddingAsync("Hello BogDb!");

            Assert.NotNull(embedding);
            Assert.Equal(4, embedding.Length);
            Assert.Equal(0.1f, embedding[0]);
            Assert.Equal(0.4f, embedding[3]);
        }

        private class AnthropicMockHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string mockResponse = @"{ ""data"": [{ ""embedding"": [0.5, 0.6] }] }";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(mockResponse) });
            }
        }

        [Fact]
        public async Task AnthropicProvider_GenerateEmbeddingAsync_ExtractsFloatsSuccessfully()
        {
            var provider = new AnthropicProvider("fake_key", new HttpClient(new AnthropicMockHandler()));
            float[] embedding = await provider.GenerateEmbeddingAsync("Hello Claude!");
            
            Assert.Equal(2, embedding.Length);
            Assert.Equal(0.6f, embedding[1]);
        }

        private class CohereMockHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string mockResponse = @"{ ""embeddings"": [[0.7, 0.8, 0.9]] }"; // Cohere has double arrays usually, mapping [0]
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(mockResponse) });
            }
        }

        [Fact]
        public async Task CohereProvider_GenerateEmbeddingAsync_ExtractsFloatsSuccessfully()
        {
            var provider = new CohereProvider("fake_key", new HttpClient(new CohereMockHandler()));
            float[] embedding = await provider.GenerateEmbeddingAsync("Hello Cohere!");
            
            Assert.Equal(3, embedding.Length);
            Assert.Equal(0.9f, embedding[2]);
        }

        private class OllamaMockHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string mockResponse = @"{ ""embedding"": [1.1, 1.2, 1.3, 1.4, 1.5] }"; 
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(mockResponse) });
            }
        }

        [Fact]
        public async Task OllamaProvider_GenerateEmbeddingAsync_ExtractsFloatsSuccessfully()
        {
            var provider = new OllamaProvider("fake_model", "http://localhost", new HttpClient(new OllamaMockHandler()));
            float[] embedding = await provider.GenerateEmbeddingAsync("Hello Local Model!");
            
            Assert.Equal(5, embedding.Length);
            Assert.Equal(1.1f, embedding[0]);
        }

        private class LmStudioMockHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // LMStudio replicates OpenAI exactly
                string mockResponse = @"{ ""data"": [{ ""embedding"": [2.1, 2.2, 2.3] }] }"; 
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(mockResponse) });
            }
        }

        [Fact]
        public async Task LmStudioProvider_GenerateEmbeddingAsync_ExtractsFloatsSuccessfully()
        {
            var provider = new LmStudioProvider("local-model", "http://localhost", new HttpClient(new LmStudioMockHandler()));
            float[] embedding = await provider.GenerateEmbeddingAsync("Hello LMStudio!");
            
            Assert.Equal(3, embedding.Length);
            Assert.Equal(2.3f, embedding[2]);
        }

        private sealed class MockHttpJsonMessageHandler : HttpMessageHandler
        {
            private readonly byte[] _jsonBytes;

            public MockHttpJsonMessageHandler(string content)
            {
                _jsonBytes = System.Text.Encoding.UTF8.GetBytes(content);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(Send(request, cancellationToken));

            protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.Method == HttpMethod.Head)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK);
                    response.Content = new ByteArrayContent(Array.Empty<byte>());
                    response.Content.Headers.ContentLength = _jsonBytes.Length;
                    return response;
                }

                if (request.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(_jsonBytes)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }

        [Fact]
        public void LoadExtension_RegistersLlmScalarAndOptions()
        {
            using var db = BogDatabase.CreateInMemory();

            new LlmExtension().Load(db);

            Assert.True(db.ScalarFunctionRegistry.Contains("llm_embed"));
            Assert.True(db.ScalarFunctionRegistry.Contains("llm_embed_batch"));
            Assert.True(db.ScalarFunctionRegistry.Contains("llm_similarity"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("llm_embed_texts"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("llm_rank_texts"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("llm_rank_nodes"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("llm_embed_nodes"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("llm_search_nodes"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("llm_search_json_array"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("llm_embed_json_array"));
            Assert.True(db.StandaloneTableFunctionRegistry.Contains("llm_ingest_json_array_to_nodes"));
            Assert.True(db.TryGetExtensionOption("llm_provider", out _));
            Assert.True(db.TryGetExtensionOption("llm_api_key", out var apiKeyOption));
            Assert.True(apiKeyOption.IsConfidential);
            Assert.Equal("ollama", db.GetExtensionOptionValue("llm_provider"));
        }

        [Fact]
        public void ReturnLlmEmbed_UsesRegisteredScalarFunction()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new FakeLlmProvider(0.25f, 0.5f, 0.75f)).Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("RETURN llm_embed('hello world')");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var values = Assert.IsAssignableFrom<IEnumerable>(result.GetNext().GetValue(0))
                .Cast<object?>()
                .ToArray();
            Assert.Equal(3, values.Length);
            Assert.Equal(0.25f, Assert.IsType<float>(values[0]));
            Assert.Equal(0.75f, Assert.IsType<float>(values[2]));
        }

        [Fact]
        public void ReturnLlmEmbedBatch_UsesRegisteredScalarFunction()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new FakeLlmProvider(1.0f, 2.0f)).Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("RETURN llm_embed_batch(['alpha', 'beta'])");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var batches = Assert.IsAssignableFrom<IEnumerable>(result.GetNext().GetValue(0))
                .Cast<object?>()
                .ToArray();

            Assert.Equal(2, batches.Length);

            var embedding0 = Assert.IsAssignableFrom<IEnumerable>(batches[0])
                .Cast<object?>()
                .ToArray();
            Assert.Equal(1.0f, Assert.IsType<float>(embedding0[0]));
            Assert.Equal(2.0f, Assert.IsType<float>(embedding0[1]));

            var embedding1 = Assert.IsAssignableFrom<IEnumerable>(batches[1])
                .Cast<object?>()
                .ToArray();
            Assert.Equal(2.0f, Assert.IsType<float>(embedding1[0]));
            Assert.Equal(3.0f, Assert.IsType<float>(embedding1[1]));
        }

        [Fact]
        public void CallLlmEmbedTexts_ReturnsOneRowPerInputText()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new FakeLlmProvider(1.0f, 2.0f)).Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("CALL llm_embed_texts('alpha', 'beta') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(new[] { "index", "text", "embedding" }, result.ColumnNames);

            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal(0L, first.GetInt64(0));
            Assert.Equal("alpha", first.GetString(1));
            var firstEmbedding = Assert.IsAssignableFrom<IEnumerable>(first.GetValue(2))
                .Cast<object?>()
                .ToArray();
            Assert.Equal(1.0f, Assert.IsType<float>(firstEmbedding[0]));
            Assert.Equal(2.0f, Assert.IsType<float>(firstEmbedding[1]));

            Assert.True(result.HasNext());
            var second = result.GetNext();
            Assert.Equal(1L, second.GetInt64(0));
            Assert.Equal("beta", second.GetString(1));
            var secondEmbedding = Assert.IsAssignableFrom<IEnumerable>(second.GetValue(2))
                .Cast<object?>()
                .ToArray();
            Assert.Equal(2.0f, Assert.IsType<float>(secondEmbedding[0]));
            Assert.Equal(3.0f, Assert.IsType<float>(secondEmbedding[1]));
        }

        [Fact]
        public void CallLlmEmbedTexts_WithInvalidBatchValue_ReturnsError()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new FakeLlmProvider(1.0f, 2.0f)).Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("CALL llm_embed_texts('alpha', 42) RETURN *");

            Assert.False(result.IsSuccess);
            Assert.Contains("string arguments", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CallLlmRankTexts_ReturnsCandidatesSortedBySimilarity()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("CALL llm_rank_texts('fruit', 'car', 'banana', 'apple') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(new[] { "rank", "text", "score", "embedding" }, result.ColumnNames);

            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal(1L, first.GetInt64(0));
            Assert.Equal("apple", first.GetString(1));
            Assert.True(first.GetDouble(2) > 0.99);

            Assert.True(result.HasNext());
            var second = result.GetNext();
            Assert.Equal(2L, second.GetInt64(0));
            Assert.Equal("banana", second.GetString(1));
            Assert.True(second.GetDouble(2) > 0.99);

            Assert.True(result.HasNext());
            var third = result.GetNext();
            Assert.Equal(3L, third.GetInt64(0));
            Assert.Equal("car", third.GetString(1));
            Assert.Equal(0.0, third.GetDouble(2), 5);
        }

        [Fact]
        public void CallLlmRankTexts_WithListArgument_ReturnsRows()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("CALL llm_rank_texts('fruit', ['car', 'banana', 'apple']) RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var count = 0;
            while (result.HasNext())
            {
                result.GetNext();
                count++;
            }

            Assert.Equal(3, count);
        }

        [Fact]
        public void CallLlmRankTexts_WithInvalidCandidate_ReturnsError()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("CALL llm_rank_texts('fruit', 'apple', 42) RETURN *");

            Assert.False(result.IsSuccess);
            Assert.Contains("candidate string arguments", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CreateNodeTable_WithFloatArrayProperty_AcceptsEmbeddingSchema()
        {
            using var db = BogDatabase.CreateInMemory();
            using var conn = new BogConnection(db);

            var result = conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])");

            Assert.True(result.IsSuccess, result.ErrorMessage);
        }

        [Fact]
        public void CreateAndSetNodeEmbeddings_StoresGraphEmbeddingsForSimilarityQueries()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            new VectorExtension().Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple', embedding: llm_embed('apple')})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'banana-doc', body:'banana'})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'car-doc', body:'car'})").IsSuccess);

            var setResult = conn.Query("MATCH (d:Document) WHERE d.embedding IS NULL SET d.embedding = llm_embed(d.body) RETURN count(d)");
            Assert.True(setResult.IsSuccess, setResult.ErrorMessage);

            var stored = conn.Query(
                "MATCH (d:Document {id:'banana-doc'}) RETURN d.embedding");

            Assert.True(stored.IsSuccess, stored.ErrorMessage);
            Assert.True(stored.HasNext());
            var storedEmbedding = Assert.IsAssignableFrom<IEnumerable>(stored.GetNext().GetValue(0))
                .Cast<object?>()
                .ToArray();
            Assert.Equal(2, storedEmbedding.Length);
            Assert.Equal(0.95f, Assert.IsType<float>(storedEmbedding[0]), 4);
            Assert.Equal(0.05f, Assert.IsType<float>(storedEmbedding[1]), 4);

            var ranked = conn.Query(
                "MATCH (d:Document) " +
                "RETURN d.id AS id, vector_cosine_similarity(d.embedding, llm_embed('fruit')) AS score " +
                "ORDER BY score DESC, id ASC LIMIT 2");

            Assert.True(ranked.IsSuccess, ranked.ErrorMessage);
            Assert.True(ranked.HasNext());
            var first = ranked.GetNext();
            Assert.Equal("apple-doc", first.GetString(0));
            Assert.True(first.GetDouble(1) > 0.99);

            Assert.True(ranked.HasNext());
            var second = ranked.GetNext();
            Assert.Equal("banana-doc", second.GetString(0));
            Assert.True(second.GetDouble(1) > 0.99);
        }

        [Fact]
        public void MatchStoredEmbeddings_CanRankWithLlmSimilarityHelper()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple', embedding: llm_embed('apple')})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'car-doc', body:'car', embedding: llm_embed('car')})").IsSuccess);

            var ranked = conn.Query(
                "MATCH (d:Document) " +
                "RETURN d.id AS id, llm_similarity(d.embedding, 'fruit') AS score " +
                "ORDER BY score DESC, id ASC");

            Assert.True(ranked.IsSuccess, ranked.ErrorMessage);
            Assert.True(ranked.HasNext());
            var first = ranked.GetNext();
            Assert.Equal("apple-doc", first.GetString(0));
            Assert.True(first.GetDouble(1) > 0.99);

            Assert.True(ranked.HasNext());
            var second = ranked.GetNext();
            Assert.Equal("car-doc", second.GetString(0));
            Assert.Equal(0.0, second.GetDouble(1), 5);
        }

        [Fact]
        public void CallLlmRankNodes_RanksStoredEmbeddingsFromNodeTable()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple', embedding: llm_embed('apple')})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'banana-doc', body:'banana', embedding: llm_embed('banana')})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'car-doc', body:'car', embedding: llm_embed('car')})").IsSuccess);

            var result = conn.Query("CALL llm_rank_nodes('Document', 'fruit') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(new[] { "rank", "id", "text", "score", "embedding" }, result.ColumnNames);

            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal(1L, first.GetInt64(0));
            Assert.Equal("apple-doc", first.GetString(1));
            Assert.Equal("apple", first.GetString(2));
            Assert.True(first.GetDouble(3) > 0.99);

            Assert.True(result.HasNext());
            var second = result.GetNext();
            Assert.Equal("banana-doc", second.GetString(1));
            Assert.Equal("banana", second.GetString(2));

            Assert.True(result.HasNext());
            var third = result.GetNext();
            Assert.Equal("car-doc", third.GetString(1));
            Assert.Equal(0.0, third.GetDouble(3), 5);
        }

        [Fact]
        public void CallLlmRankNodes_SupportsExplicitPropertiesAndLimit()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Snippet(id STRING, content STRING, vec FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Snippet {id:'apple-snippet', content:'apple', vec: llm_embed('apple')})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Snippet {id:'banana-snippet', content:'banana', vec: llm_embed('banana')})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Snippet {id:'car-snippet', content:'car', vec: llm_embed('car')})").IsSuccess);

            var result = conn.Query("CALL llm_rank_nodes('Snippet', 'fruit', 'vec', 'content', 2) RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var count = 0;
            while (result.HasNext())
            {
                result.GetNext();
                count++;
            }

            Assert.Equal(2, count);
        }

        [Fact]
        public void CallLlmRankNodes_WithMissingEmbeddingProperty_ReturnsError()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(conn.Query("CREATE NODE TABLE Document(id STRING, body STRING)").IsSuccess);

            var result = conn.Query("CALL llm_rank_nodes('Document', 'fruit') RETURN *");

            Assert.False(result.IsSuccess);
            Assert.Contains("Embedding property", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CallLlmEmbedNodes_BackfillsMissingEmbeddings()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple'})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'banana-doc', body:'banana'})").IsSuccess);

            var result = conn.Query("CALL llm_embed_nodes('Document') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var count = 0;
            while (result.HasNext())
            {
                result.GetNext();
                count++;
            }

            Assert.Equal(2, count);

            var stored = conn.Query(
                "MATCH (d:Document) RETURN d.id, llm_similarity(d.embedding, 'fruit') AS score ORDER BY d.id");

            Assert.True(stored.IsSuccess, stored.ErrorMessage);
            Assert.True(stored.HasNext());
            var first = stored.GetNext();
            Assert.Equal("apple-doc", first.GetString(0));
            Assert.True(first.GetDouble(1) > 0.99);
        }

        [Fact]
        public void CallLlmEmbedNodes_DefaultsToBackfillWithoutOverwriting()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple', embedding: llm_embed('car')})").IsSuccess);

            var result = conn.Query("CALL llm_embed_nodes('Document') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.False(result.HasNext());

            var stored = conn.Query("MATCH (d:Document {id:'apple-doc'}) RETURN llm_similarity(d.embedding, 'car')");
            Assert.True(stored.IsSuccess, stored.ErrorMessage);
            Assert.Equal(1.0, stored.GetNext().GetDouble(0), 5);
        }

        [Fact]
        public void CallLlmEmbedNodes_RejectsNonFloatArrayEmbeddingProperty()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding INT64[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple'})").IsSuccess);

            var result = conn.Query("CALL llm_embed_nodes('Document') RETURN *");
            Assert.False(result.IsSuccess);
            Assert.Contains("FLOAT[]", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CallLlmEmbedNodes_OverwriteTrue_ReplacesExistingEmbeddings()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple', embedding: llm_embed('car')})").IsSuccess);

            var result = conn.Query("CALL llm_embed_nodes('Document', 'body', 'embedding', true) RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());

            var stored = conn.Query("MATCH (d:Document {id:'apple-doc'}) RETURN llm_similarity(d.embedding, 'fruit')");
            Assert.True(stored.IsSuccess, stored.ErrorMessage);
            Assert.True(stored.GetNext().GetDouble(0) > 0.99);
        }

        [Fact]
        public void CallLlmSearchNodes_RanksStoredEmbeddings()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple', embedding: llm_embed('apple')})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'car-doc', body:'car', embedding: llm_embed('car')})").IsSuccess);

            var result = conn.Query("CALL llm_search_nodes('Document', 'fruit') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal("apple-doc", first.GetString(1));
            Assert.True(first.GetDouble(3) > 0.99);

            Assert.True(result.HasNext());
            var second = result.GetNext();
            Assert.Equal("car-doc", second.GetString(1));
            Assert.Equal(0.0, second.GetDouble(3), 5);
        }

        [Fact]
        public void CallLlmSearchNodes_EmbedMissingTrue_BackfillsThenRanks()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple'})").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'banana-doc', body:'banana'})").IsSuccess);

            var result = conn.Query("CALL llm_search_nodes('Document', 'fruit', 'body', 'embedding', 2, true) RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var count = 0;
            while (result.HasNext())
            {
                var row = result.GetNext();
                Assert.True(row.GetDouble(3) > 0.99);
                count++;
            }

            Assert.Equal(2, count);

            var stored = conn.Query("MATCH (d:Document) RETURN d.id, llm_similarity(d.embedding, 'fruit') AS score ORDER BY d.id");
            Assert.True(stored.IsSuccess, stored.ErrorMessage);
            Assert.True(stored.HasNext());
            Assert.True(stored.GetNext().GetDouble(1) > 0.99);
        }

        [Fact]
        public void CallLlmSearchNodes_WithoutStoredEmbeddingsAndNoBackfill_ReturnsNoRows()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[])").IsSuccess);
            Assert.True(conn.Query("CREATE (:Document {id:'apple-doc', body:'apple'})").IsSuccess);

            var result = conn.Query("CALL llm_search_nodes('Document', 'fruit') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.False(result.HasNext());
        }

        [Fact]
        public void CallLlmSearchJsonArray_RanksLocalJsonRows()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            var path = Path.Combine(Path.GetTempPath(), $"llm-search-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "[{\"body\":\"car\"},{\"body\":\"banana\"},{\"body\":\"apple\"}]");

                var result = conn.Query($"CALL llm_search_json_array('{path.Replace("\\", "/")}', 'fruit') RETURN *");

                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.True(result.HasNext());
                var first = result.GetNext();
                Assert.Equal("apple", first.GetString(1));
                Assert.True(first.GetDouble(2) > 0.99);

                Assert.True(result.HasNext());
                var second = result.GetNext();
                Assert.Equal("banana", second.GetString(1));
                Assert.True(second.GetDouble(2) > 0.99);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void CallLlmSearchJsonArray_UsesRegisteredHttpFileSystem()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            db.RegisterFileSystem(
                "http",
                new BogDb.Extensions.HttpFS.HttpFileSystem(new HttpClient(new MockHttpJsonMessageHandler(
                    "[{\"content\":\"car\"},{\"content\":\"apple\"}]"))));
            using var conn = new BogConnection(db);

            var result = conn.Query("CALL llm_search_json_array('http://example.test/rows.json', 'fruit', 'content') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal("apple", first.GetString(1));
            Assert.True(first.GetDouble(2) > 0.99);
        }

        [Fact]
        public void CallLlmSearchJsonArray_WithMissingTextField_ReturnsNoRows()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            var path = Path.Combine(Path.GetTempPath(), $"llm-search-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "[{\"note\":\"apple\"}]");

                var result = conn.Query($"CALL llm_search_json_array('{path.Replace("\\", "/")}', 'fruit') RETURN *");

                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.False(result.HasNext());
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void CallLlmEmbedJsonArray_ReturnsEmbeddingsForLocalJsonRows()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            var path = Path.Combine(Path.GetTempPath(), $"llm-embed-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "[{\"body\":\"apple\"},{\"body\":\"car\"}]");

                var result = conn.Query($"CALL llm_embed_json_array('{path.Replace("\\", "/")}') RETURN *");

                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(new[] { "index", "text", "embedding", "row_json" }, result.ColumnNames);

                Assert.True(result.HasNext());
                var first = result.GetNext();
                Assert.Equal(0L, first.GetInt64(0));
                Assert.Equal("apple", first.GetString(1));
                var embedding = Assert.IsAssignableFrom<IEnumerable>(first.GetValue(2))
                    .Cast<object?>()
                    .ToArray();
                Assert.Equal(2, embedding.Length);
                Assert.Equal(0.99f, Assert.IsType<float>(embedding[0]), 4);
                Assert.Contains("\"body\":\"apple\"", first.GetString(3), StringComparison.Ordinal);

                Assert.True(result.HasNext());
                var second = result.GetNext();
                Assert.Equal(1L, second.GetInt64(0));
                Assert.Equal("car", second.GetString(1));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void CallLlmEmbedJsonArray_UsesRegisteredHttpFileSystem()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            db.RegisterFileSystem(
                "http",
                new BogDb.Extensions.HttpFS.HttpFileSystem(new HttpClient(new MockHttpJsonMessageHandler(
                    "[{\"content\":\"banana\"}]"))));
            using var conn = new BogConnection(db);

            var result = conn.Query("CALL llm_embed_json_array('http://example.test/embed.json', 'content') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal("banana", first.GetString(1));
            var embedding = Assert.IsAssignableFrom<IEnumerable>(first.GetValue(2))
                .Cast<object?>()
                .ToArray();
            Assert.Equal(0.95f, Assert.IsType<float>(embedding[0]), 4);
            Assert.Equal(0.05f, Assert.IsType<float>(embedding[1]), 4);
        }

        [Fact]
        public void CallLlmIngestJsonArrayToNodes_StoresEmbeddingsInGraph()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Document(id STRING, body STRING, embedding FLOAT[], category STRING)").IsSuccess);

            var path = Path.Combine(Path.GetTempPath(), $"llm-ingest-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "[{\"id\":\"apple-doc\",\"body\":\"apple\",\"category\":\"fruit\"},{\"id\":\"car-doc\",\"body\":\"car\",\"category\":\"vehicle\"}]");

                var result = conn.Query($"CALL llm_ingest_json_array_to_nodes('Document', '{path.Replace("\\", "/")}') RETURN *");

                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(new[] { "id", "text", "embedding", "row_json" }, result.ColumnNames);

                Assert.True(result.HasNext());
                var first = result.GetNext();
                Assert.Equal("apple-doc", first.GetString(0));
                Assert.Equal("apple", first.GetString(1));

                var ranked = conn.Query(
                    "MATCH (d:Document) RETURN d.id, d.category, llm_similarity(d.embedding, 'fruit') AS score ORDER BY score DESC, d.id ASC");

                Assert.True(ranked.IsSuccess, ranked.ErrorMessage);
                Assert.True(ranked.HasNext());
                var top = ranked.GetNext();
                Assert.Equal("apple-doc", top.GetString(0));
                Assert.Equal("fruit", top.GetString(1));
                Assert.True(top.GetDouble(2) > 0.99);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void CallLlmIngestJsonArrayToNodes_UsesRegisteredHttpFileSystem()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            db.RegisterFileSystem(
                "http",
                new BogDb.Extensions.HttpFS.HttpFileSystem(new HttpClient(new MockHttpJsonMessageHandler(
                    "[{\"doc_id\":\"banana-doc\",\"content\":\"banana\"}]"))));
            using var conn = new BogConnection(db);

            Assert.True(
                conn.Query("CREATE NODE TABLE Snippet(id STRING, content STRING, embedding FLOAT[])").IsSuccess);

            var result = conn.Query(
                "CALL llm_ingest_json_array_to_nodes('Snippet', 'http://example.test/snippets.json', 'doc_id', 'content') RETURN *");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(result.HasNext());
            var first = result.GetNext();
            Assert.Equal("banana-doc", first.GetString(0));
            Assert.Equal("banana", first.GetString(1));

            var stored = conn.Query("MATCH (s:Snippet {id:'banana-doc'}) RETURN llm_similarity(s.embedding, 'fruit')");
            Assert.True(stored.IsSuccess, stored.ErrorMessage);
            Assert.True(stored.GetNext().GetDouble(0) > 0.99);
        }

        [Fact]
        public void ReturnLlmSimilarity_WithInvalidEmbeddingValue_ReturnsError()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new SemanticFakeLlmProvider()).Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("RETURN llm_similarity(['apple'], 'fruit')");

            Assert.False(result.IsSuccess);
            Assert.Contains("numeric values", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ReturnLlmEmbedBatch_WithNonStringElement_ReturnsError()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension(_ => new FakeLlmProvider(1.0f, 2.0f)).Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("RETURN llm_embed_batch(['alpha', 42])");

            Assert.False(result.IsSuccess);
            Assert.Contains("list of strings", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CallOptionAssignment_ConfiguresLlmProvider()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension().Load(db);
            using var conn = new BogConnection(db);

            var result = conn.Query("CALL llm_provider='openai'");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal("openai", db.GetExtensionOptionValue("llm_provider"));
        }

        [Fact]
        public void ReturnLlmEmbed_RemoteProviderWithoutApiKey_ReturnsError()
        {
            using var db = BogDatabase.CreateInMemory();
            new LlmExtension().Load(db);
            db.SetExtensionOption("llm_provider", "openai");
            using var conn = new BogConnection(db);

            var result = conn.Query("RETURN llm_embed('hello world')");

            Assert.False(result.IsSuccess);
            Assert.Contains("llm_api_key", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
    }
}
