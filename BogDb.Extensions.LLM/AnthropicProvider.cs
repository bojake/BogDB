using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BogDb.Extensions.LLM
{
    public class AnthropicProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _modelName;

        public AnthropicProvider(string apiKey, HttpClient? httpClient = null)
            : this(apiKey, "voyage-2", httpClient)
        {
        }

        public AnthropicProvider(string apiKey, string modelName, HttpClient? httpClient = null)
        {
            _apiKey = apiKey;
            _modelName = modelName;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            // Anthropic utilizes slightly different header requirements mapping 'x-api-key'
            var requestBody = new
            {
                model = _modelName,
                input = text
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/embeddings")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);

            // Structure: { ""data"": [{ ""embedding"": [...] }] }
            var dataArray = document.RootElement.GetProperty("data")[0];
            var embeddingArray = dataArray.GetProperty("embedding");

            return embeddingArray.EnumerateArray()
                .Select(element => element.GetSingle())
                .ToArray();
        }
    }
}
