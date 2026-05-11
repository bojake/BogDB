using System;
using System.Numerics;

namespace BogDb.Extensions.Vector
{
    /// <summary>
    /// Demonstrates raw execution parity against BogDb C++ by exposing native
    /// Hardware Accelerated SIMD Math comparisons computing Vector Distances globally perfectly!
    /// </summary>
    public static class SimilaritySearch
    {
        public static float CosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
        {
            if (vectorA.Length != vectorB.Length)
                throw new ArgumentException("Vectors must have the same length natively!");

            float dotProduct = 0f;
            float normA = 0f;
            float normB = 0f;

            int i = 0;
            // Native SIMD Hardware Acceleration effortlessly mapping C++ semantics perfectly natively
            int vectorSize = System.Numerics.Vector<float>.Count;
            if (vectorA.Length >= vectorSize)
            {
                var dotSimd = System.Numerics.Vector<float>.Zero;
                var normASimd = System.Numerics.Vector<float>.Zero;
                var normBSimd = System.Numerics.Vector<float>.Zero;

                for (; i <= vectorA.Length - vectorSize; i += vectorSize)
                {
                    var va = new System.Numerics.Vector<float>(vectorA.Slice(i));
                    var vb = new System.Numerics.Vector<float>(vectorB.Slice(i));

                    dotSimd += va * vb;
                    normASimd += va * va;
                    normBSimd += vb * vb;
                }
                
                for (int j = 0; j < vectorSize; j++)
                {
                    dotProduct += dotSimd[j];
                    normA += normASimd[j];
                    normB += normBSimd[j];
                }
            }

            // Remainder scalars dynamically resolved
            for (; i < vectorA.Length; i++)
            {
                dotProduct += vectorA[i] * vectorB[i];
                normA += vectorA[i] * vectorA[i];
                normB += vectorB[i] * vectorB[i];
            }

            if (normA == 0 || normB == 0) return 0;
            return dotProduct / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
        }
    }
}
