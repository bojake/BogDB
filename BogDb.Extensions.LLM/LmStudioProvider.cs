using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace BogDb.Extensions.LLM
{
    public class LmStudioProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _modelName;

        public LmStudioProvider(string modelName = "local-model", string baseUrl = "http://localhost:1234/v1", HttpClient? httpClient = null)
        {
            _modelName = modelName;
            _baseUrl = baseUrl;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            // LMStudio replicates the exact OpenAI structure but serves it locally at a custom port
            var requestBody = new
            {
                input = text,
                model = _modelName
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/embeddings")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);

            // Structure remains identical to OpenAI: { ""data"": [{ ""embedding"": [...] }] }
            var dataArray = document.RootElement.GetProperty("data")[0];
            var embeddingArray = dataArray.GetProperty("embedding");

            return embeddingArray.EnumerateArray()
                .Select(element => element.GetSingle())
                .ToArray();
        }
    }
}
