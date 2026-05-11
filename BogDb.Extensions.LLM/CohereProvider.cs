using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BogDb.Extensions.LLM
{
    public class CohereProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _modelName;

        public CohereProvider(string apiKey, HttpClient? httpClient = null)
            : this(apiKey, "embed-english-v3.0", httpClient)
        {
        }

        public CohereProvider(string apiKey, string modelName, HttpClient? httpClient = null)
        {
            _apiKey = apiKey;
            _modelName = modelName;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            var requestBody = new
            {
                texts = new[] { text },
                model = _modelName,
                input_type = "search_document"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.cohere.ai/v1/embed")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);

            // Cohere sends embeddings mapped differently: { ""embeddings"": [[...]] }
            var embeddingsArray = document.RootElement.GetProperty("embeddings")[0];

            return embeddingsArray.EnumerateArray()
                .Select(element => element.GetSingle())
                .ToArray();
        }
    }
}
