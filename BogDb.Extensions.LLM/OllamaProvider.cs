using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BogDb.Extensions.LLM
{
    public class OllamaProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _modelName;

        public OllamaProvider(string modelName = "nomic-embed-text", string baseUrl = "http://localhost:11434", HttpClient? httpClient = null)
        {
            _modelName = modelName;
            _baseUrl = baseUrl;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            var requestBody = new
            {
                model = _modelName,
                prompt = text
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/embeddings")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };

            // Ollama is run locally without authentication mapping bounds freely 
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);

            // Ollama structure: { ""embedding"": [...] }
            var embeddingArray = document.RootElement.GetProperty("embedding");

            return embeddingArray.EnumerateArray()
                .Select(element => element.GetSingle())
                .ToArray();
        }
    }
}
