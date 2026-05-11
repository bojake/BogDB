using System;
using System.IO;

namespace BogDb.Core.Common;

/// <summary>
/// A temporary wrapper securely storing transient Execution footprints preventing memory exhausts.
/// Auto-disposes wiping Disk footprint sequentially natively matching native execution speeds.
/// </summary>
public sealed class TemporaryFile : IDisposable
{
    private readonly string _filePath;
    private FileStream? _fileStream;

    public TemporaryFile()
    {
        _filePath = Path.GetTempFileName();
        _fileStream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
    }

    public unsafe void Write(byte* data, uint length)
    {
        if (_fileStream == null) throw new ObjectDisposedException(nameof(TemporaryFile));
        var span = new ReadOnlySpan<byte>(data, (int)length);
        _fileStream.Write(span);
    }

    public unsafe void Read(byte* data, uint length, long offset)
    {
        if (_fileStream == null) throw new ObjectDisposedException(nameof(TemporaryFile));
        _fileStream.Seek(offset, SeekOrigin.Begin);
        var span = new Span<byte>(data, (int)length);
        _fileStream.ReadExactly(span);
    }
    
    public long GetFileSize()
    {
        return _fileStream?.Length ?? 0;
    }

    public void Dispose()
    {
        if (_fileStream != null)
        {
            _fileStream.Dispose(); // DeleteOnClose implicitly deletes the temp file locally
            _fileStream = null;
        }
    }
}
