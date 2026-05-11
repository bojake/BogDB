using System;
using System.IO.Hashing;

namespace BogDb.Core.Common.Checksum;

/// <summary>
/// A hardware-accelerated CRC32C utility replicating BogDb Native Checksum hashing 
/// validating page structures synchronously on VFS flush sequences.
/// </summary>
public static class CRC32C
{
    public static uint Compute(ReadOnlySpan<byte> source)
    {
        return Crc32.HashToUInt32(source);
    }

    public static uint Compute(byte[] source)
    {
        return Crc32.HashToUInt32(source);
    }
}
