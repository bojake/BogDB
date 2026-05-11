//------------------------------------------------------------------------------
// Checksum support for WAL records.
// C++ parity: checksum_writer.h / checksum_reader.h
//
// Uses CRC32 to verify WAL record integrity on replay.
//------------------------------------------------------------------------------

using System;
using System.IO;
using System.IO.Hashing;

namespace BogDb.Core.Transaction;

/// <summary>
/// Wraps a BinaryWriter to append CRC32 checksums after each record block.
/// C++ parity: ChecksumWriter in checksum_writer.h.
/// </summary>
public sealed class ChecksumWriter : IDisposable
{
    private readonly BinaryWriter _inner;
    private readonly MemoryStream _buffer;
    private readonly BinaryWriter _bufferWriter;

    public ChecksumWriter(BinaryWriter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _buffer = new MemoryStream();
        _bufferWriter = new BinaryWriter(_buffer);
    }

    /// <summary>The writer to use for building a record. Call FlushWithChecksum to commit.</summary>
    public BinaryWriter Writer => _bufferWriter;

    /// <summary>
    /// Writes the buffered data + its CRC32 checksum to the underlying stream.
    /// </summary>
    public void FlushWithChecksum()
    {
        var data = _buffer.ToArray();
        var crc = new Crc32();
        crc.Append(data);
        var hash = crc.GetCurrentHashAsUInt32();

        _inner.Write(data.Length);
        _inner.Write(data);
        _inner.Write(hash);

        _buffer.SetLength(0);
    }

    public void Dispose()
    {
        _bufferWriter.Dispose();
        _buffer.Dispose();
    }
}

/// <summary>
/// Wraps a BinaryReader to verify CRC32 checksums on each record block.
/// C++ parity: ChecksumReader in checksum_reader.h.
/// </summary>
public sealed class ChecksumReader : IDisposable
{
    private readonly BinaryReader _inner;
    private readonly string _errorMessage;

    public ChecksumReader(BinaryReader inner, string errorMessage = "Checksum verification failed")
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _errorMessage = errorMessage;
    }

    /// <summary>
    /// Reads a checksummed block: [length:int32][data:bytes][crc32:uint32].
    /// Returns a BinaryReader over the verified data.
    /// Throws InvalidDataException on mismatch.
    /// </summary>
    public BinaryReader ReadVerifiedBlock()
    {
        var length = _inner.ReadInt32();
        var data = _inner.ReadBytes(length);
        var storedCrc = _inner.ReadUInt32();

        var crc = new Crc32();
        crc.Append(data);
        var computedCrc = crc.GetCurrentHashAsUInt32();

        if (storedCrc != computedCrc)
        {
            throw new InvalidDataException(_errorMessage);
        }

        return new BinaryReader(new MemoryStream(data));
    }

    /// <summary>Current read position in the underlying stream.</summary>
    public long ReadOffset => _inner.BaseStream.Position;

    /// <summary>Whether the underlying stream has been fully consumed.</summary>
    public bool Finished => _inner.BaseStream.Position >= _inner.BaseStream.Length;

    public void Dispose()
    {
        // Don't dispose the inner reader — caller owns it
    }
}
