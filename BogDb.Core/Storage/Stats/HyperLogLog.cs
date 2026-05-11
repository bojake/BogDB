using System;

namespace BogDb.Core.Storage.Stats
{
    public class HyperLogLog
    {
        public const int P = 6;
        public const int Q = 64 - P;
        public const int M = 1 << P;
        public const double ALPHA = 0.721347520444481703680; // 1 / (2 log(2))

        private byte[] _k = new byte[M];

        public HyperLogLog()
        {
        }

        public HyperLogLog(HyperLogLog other)
        {
            _k = new byte[M];
            Array.Copy(other._k, _k, M);
        }

        // Algorithm 1
        public void InsertElement(ulong h)
        {
            var i = h & ((1UL << P) - 1);
            h >>= P;
            h |= 1UL << Q;
            
            // Count zeros trailing
            byte z = (byte)(System.Numerics.BitOperations.TrailingZeroCount(h) + 1);
            Update((int)i, z);
        }

        public void Update(int i, byte z)
        {
            _k[i] = Math.Max(_k[i], z);
        }

        public byte GetRegister(int i)
        {
            return _k[i];
        }

        public ulong Count()
        {
            uint[] c = new uint[Q + 2];
            ExtractCounts(c);
            return (ulong)EstimateCardinality(c);
        }

        // Algorithm 2
        public void Merge(HyperLogLog other)
        {
            for (int i = 0; i < M; ++i)
            {
                Update(i, other._k[i]);
            }
        }

        // Algorithm 4
        private void ExtractCounts(uint[] c)
        {
            for (int i = 0; i < M; ++i)
            {
                c[_k[i]]++;
            }
        }

        private static double HLLSigma(double x)
        {
            if (x == 1.0) return double.PositiveInfinity;

            double z_prime = double.NaN;
            double y = 1.0;
            double z = x;
            
            do
            {
                x *= x;
                z_prime = z;
                z += x * y;
                y += y;
            } while (z_prime != z);

            return z;
        }

        private static double HLLTau(double x)
        {
            if (x == 0.0 || x == 1.0) return 0.0;

            double z_prime = double.NaN;
            double y = 1.0;
            double z = 1.0 - x;

            do
            {
                x = Math.Sqrt(x);
                z_prime = z;
                y *= 0.5;
                z -= Math.Pow(1.0 - x, 2) * y;
            } while (z_prime != z);

            return z / 3.0;
        }

        // Algorithm 6
        public static long EstimateCardinality(uint[] c)
        {
            double z = M * HLLTau((M - c[Q]) / (double)M);

            for (int k = Q; k >= 1; --k)
            {
                z += c[k];
                z *= 0.5;
            }

            z += M * HLLSigma(c[0] / (double)M);

            return (long)Math.Round(ALPHA * M * M / z, MidpointRounding.AwayFromZero);
        }
    }
}
