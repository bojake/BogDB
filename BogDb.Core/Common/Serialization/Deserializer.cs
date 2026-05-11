using System;
using System.Text;
using BogDb.Core.Common.FileSystem;

namespace BogDb.Core.Common.Serialization;

/// <summary>
/// A fast binary deserializer reading structs sequentially from an underlying VFS FileInfo handle.
/// </summary>
public sealed class Deserializer
{
    private readonly BogDb.Core.Common.FileSystem.FileInfo _fileInfo;
    private long _offset;

    public Deserializer(BogDb.Core.Common.FileSystem.FileInfo fileInfo, long initialOffset = 0)
    {
        _fileInfo = fileInfo;
        _offset = initialOffset;
    }

    public T Read<T>() where T : unmanaged
    {
        unsafe
        {
            T value = default;
            var span = new Span<byte>(&value, sizeof(T));
            _fileInfo.Read(span, _offset);
            _offset += sizeof(T);
            return value;
        }
    }

    public string ReadString()
    {
        int length = Read<int>();
        var span = length <= 1024 ? stackalloc byte[length] : new byte[length];
        _fileInfo.Read(span, _offset);
        _offset += length;
        return Encoding.UTF8.GetString(span);
    }

    public byte[] ReadBuffer()
    {
        int length = Read<int>();
        byte[] buffer = new byte[length];
        _fileInfo.Read(buffer, _offset);
        _offset += length;
        return buffer;
    }

    public long GetCurrentOffset() => _offset;
}
