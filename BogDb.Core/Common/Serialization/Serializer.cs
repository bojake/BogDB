using System;
using System.Buffers.Binary;
using System.Text;
using BogDb.Core.Common.FileSystem;

namespace BogDb.Core.Common.Serialization;

/// <summary>
/// A fast binary serializer appending structs sequentially into an underlying VFS FileInfo handle.
/// </summary>
public sealed class Serializer
{
    private readonly BogDb.Core.Common.FileSystem.FileInfo _fileInfo;
    private long _offset;

    public Serializer(BogDb.Core.Common.FileSystem.FileInfo fileInfo, long initialOffset = 0)
    {
        _fileInfo = fileInfo;
        _offset = initialOffset;
    }

    public void Write<T>(T value) where T : unmanaged
    {
        unsafe
        {
            var span = new ReadOnlySpan<byte>(&value, sizeof(T));
            _fileInfo.Write(span, _offset);
            _offset += sizeof(T);
        }
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Write(bytes.Length);
        _fileInfo.Write(bytes, _offset);
        _offset += bytes.Length;
    }

    public void WriteBuffer(ReadOnlySpan<byte> buffer)
    {
        Write(buffer.Length);
        _fileInfo.Write(buffer, _offset);
        _offset += buffer.Length;
    }

    public long GetCurrentOffset() => _offset;
}
