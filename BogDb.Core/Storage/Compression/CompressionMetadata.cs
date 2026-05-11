using System.Collections.Generic;

namespace BogDb.Core.Storage.Compression
{
    public enum CompressionType : byte
    {
        UNCOMPRESSED = 0,
        CONSTANT = 1,
        BOOLEAN_BITPACKING = 2,
        INTEGER_BITPACKING = 3,
        ALP = 4
    }

    public class CompressionMetadata
    {
        public object Min { get; set; }
        public object Max { get; set; }
        public CompressionType Compression { get; set; }
        public AlpMetadata AlpExtraMetadata { get; set; }
        public List<CompressionMetadata> Children { get; set; } = new();

        public CompressionMetadata(object min, object max, CompressionType compression)
        {
            Min = min;
            Max = max;
            Compression = compression;
        }

        public CompressionMetadata GetChild(int index)
        {
            return Children[index];
        }

        public bool IsConstant() => Compression == CompressionType.CONSTANT;
    }

    public class AlpMetadata
    {
        public byte Exp { get; set; }
        public byte Fac { get; set; }
        public ulong ExceptionCount { get; set; }
        public ulong ExceptionCapacity { get; set; }
        
        public AlpMetadata(byte exp, byte fac, ulong exceptionCount, ulong exceptionCapacity)
        {
            Exp = exp;
            Fac = fac;
            ExceptionCount = exceptionCount;
            ExceptionCapacity = exceptionCapacity;
        }
    }
}
