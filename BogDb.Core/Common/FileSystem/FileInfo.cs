using System;
using System.IO;

namespace BogDb.Core.Common.FileSystem;

/// <summary>
/// Represents an abstract file handle mapping to standard POSIX/Windows IO interfaces
/// or virtualized remote objects (e.g., S3).
/// </summary>
public abstract class FileInfo : IDisposable
{
    public string Path { get; }
    public long FileSize { get; protected set; }
    
    protected FileInfo(string path)
    {
        Path = path;
    }

    public abstract void Read(Span<byte> buffer, long offset);
    public abstract void Write(ReadOnlySpan<byte> buffer, long offset);
    public abstract void Sync();
    public abstract void Truncate(long size);
    public abstract long GetFileSize();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);
}
