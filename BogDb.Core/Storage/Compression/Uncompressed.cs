using System;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Compression
{
    public static class Uncompressed
    {
        public static ulong NumValues(ulong dataSize, PhysicalTypeID physicalType)
        {
            uint numBytesPerValue = GetDataTypeSizeInChunk(physicalType);
            return numBytesPerValue == 0 ? ulong.MaxValue : dataSize / numBytesPerValue;
        }

        public static uint GetDataTypeSizeInChunk(PhysicalTypeID dataType)
        {
            switch (dataType)
            {
                case PhysicalTypeID.STRING:
                case PhysicalTypeID.ARRAY:
                case PhysicalTypeID.LIST:
                case PhysicalTypeID.STRUCT:
                    return 0;
                case PhysicalTypeID.INTERNAL_ID:
                    return sizeof(ulong); // offset_t mapped to ulong
                default:
                    // Using fixed type size bounds defined in BogDb.Core.Common
                    return LogicalTypeUtils.GetFixedTypeSize(dataType);
            }
        }
    }
}
