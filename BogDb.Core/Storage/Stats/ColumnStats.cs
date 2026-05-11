using System;
using System.Runtime.InteropServices;
using System.Text;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Stats
{
    public class ColumnStats
    {
        private const ulong Fnv64OffsetBasis = 14695981039346656037UL;
        private const ulong Fnv64Prime = 1099511628211UL;
        private const ulong NullValueHash = 0x9E37_79B9_7F4A_7C15UL;

        private readonly HyperLogLog? _hll;

        public ColumnStats()
        {
        }

        public ColumnStats(ColumnStats other)
        {
            _hll = other._hll is null ? null : new HyperLogLog(other._hll);
        }

        public ColumnStats(PhysicalTypeID dataType)
        {
            if (dataType != PhysicalTypeID.LIST && dataType != PhysicalTypeID.STRUCT && dataType != PhysicalTypeID.ARRAY)
            {
                _hll = new HyperLogLog();
            }
        }

        public ulong GetNumDistinctValues()
        {
            return _hll?.Count() ?? 0;
        }

        public void Update(ValueVector vector)
        {
            if (_hll == null)
                return;

            var selVector = vector.State.GetSelVector();
            var selected = selVector.GetMutableBuffer();
            var values = vector.GetAsReadOnlySpan();
            var valueSize = (int)LogicalTypeUtils.GetFixedTypeSize(LogicalTypeUtils.GetPhysicalType(vector.DataType));

            for (var i = 0; i < selected.Length; i++)
            {
                var pos = selected[i];
                ulong hash;
                if (vector.IsNull(pos))
                {
                    hash = NullValueHash;
                }
                else if (vector.DataType is LogicalTypeID.STRING or LogicalTypeID.BLOB)
                {
                    hash = HashKuString(vector.GetValue<KuString>(pos), vector.DataType == LogicalTypeID.BLOB);
                }
                else
                {
                    var start = pos * valueSize;
                    hash = HashBytes(values.Slice(start, valueSize));
                }
                _hll.InsertElement(hash);
            }
        }

        public void Merge(ColumnStats other)
        {
            if (_hll != null && other._hll != null)
            {
                _hll.Merge(other._hll);
            }
        }

        private static ulong HashKuString(KuString value, bool treatAsBlob)
        {
            if (value.Length == 0)
                return HashBytes(ReadOnlySpan<byte>.Empty);

            if (KuString.IsShortString(value.Length))
            {
                Span<byte> raw = stackalloc byte[16];
                MemoryMarshal.Write(raw, in value);
                Span<byte> shortBytes = stackalloc byte[(int)value.Length];
                var prefixLen = (int)Math.Min(value.Length, KuString.PREFIX_LENGTH);
                raw.Slice(4, prefixLen).CopyTo(shortBytes);
                if (value.Length > KuString.PREFIX_LENGTH)
                    raw.Slice(8, (int)value.Length - 4).CopyTo(shortBytes.Slice(4));
                return HashBytes(shortBytes);
            }

            if (value.OverflowPtr == 0)
                return HashBytes(ReadOnlySpan<byte>.Empty);

            unsafe
            {
                var overflow = new ReadOnlySpan<byte>((byte*)value.OverflowPtr, checked((int)value.Length));
                if (treatAsBlob)
                    return HashBytes(overflow);
                try
                {
                    // Normalize UTF-8 strings through SetKuString-compatible decode.
                    var normalized = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(overflow));
                    return HashBytes(normalized);
                }
                catch (DecoderFallbackException)
                {
                    return HashBytes(overflow);
                }
            }
        }

        private static ulong HashBytes(ReadOnlySpan<byte> bytes)
        {
            var hash = Fnv64OffsetBasis;
            for (var i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= Fnv64Prime;
            }
            return hash;
        }
    }
}
