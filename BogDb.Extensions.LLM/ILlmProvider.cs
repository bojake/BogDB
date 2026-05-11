using System.Threading.Tasks;

namespace BogDb.Extensions.LLM
{
    /// <summary>
    /// Base Interface bridging Text Generation and Embedding mappings directly into BogDb pipelines smoothly natively!
    /// </summary>
    public interface ILlmProvider
    {
        Task<float[]> GenerateEmbeddingAsync(string text);
    }
}
