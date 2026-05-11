using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace BogDb.Extensions.LLM
{
    public class OpenAiProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _modelName;

        public OpenAiProvider(string apiKey, HttpClient? httpClient = null)
            : this(apiKey, "text-embedding-ada-002", httpClient)
        {
        }

        public OpenAiProvider(string apiKey, string modelName, HttpClient? httpClient = null)
        {
            _apiKey = apiKey;
            _modelName = modelName;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            var requestBody = new
            {
                input = text,
                model = _modelName
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);

            // Extract the first embedding element iterating System.Text.Json spans accurately natively
            var dataArray = document.RootElement.GetProperty("data")[0];
            var embeddingArray = dataArray.GetProperty("embedding");

            return embeddingArray.EnumerateArray()
                .Select(element => element.GetSingle())
                .ToArray();
        }
    }
}
