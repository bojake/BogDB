using System;
using System.IO;

namespace BogDb.Core.Common.FileSystem;

public sealed class LocalFileInfo : FileInfo
{
    private readonly FileStream _stream;

    public LocalFileInfo(string path, FileStream stream) : base(path)
    {
        _stream = stream;
        FileSize = _stream.Length;
    }

    public override void Read(Span<byte> buffer, long offset)
    {
        RandomAccess.Read(_stream.SafeFileHandle, buffer, offset);
    }

    public override void Write(ReadOnlySpan<byte> buffer, long offset)
    {
        RandomAccess.Write(_stream.SafeFileHandle, buffer, offset);
        if (offset + buffer.Length > FileSize)
        {
            FileSize = offset + buffer.Length;
        }
    }

    public override void Sync()
    {
        _stream.Flush(true);
    }

    public override void Truncate(long size)
    {
        _stream.SetLength(size);
        FileSize = size;
    }

    public override long GetFileSize()
    {
        return FileSize;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stream.Dispose();
        }
    }
}
