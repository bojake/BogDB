using System;
using System.IO;

namespace BogDb.Core.Storage
{
    /// <summary>
    /// Phase 35: Natively tracks virtual MemoryManager blocks routing structural chunks out of managed environments mapping explicit POSIX file configurations dynamically sequentially faithfully reliably appropriately efficiently!
    /// </summary>
    public class DiskStorageManager : IDisposable
    {
        private readonly string _databasePath;
        private readonly bool _readOnly;
        private readonly FileStream _fileStream;
        public string DataFilePath { get; }

        public DiskStorageManager(string databasePath, bool readOnly = false)
        {
            _databasePath = databasePath;
            _readOnly = readOnly;
            // Native IO bridging using explicit RandomAccess natively tracking unmanaged byte allocations reliably!
            string dataPath = Path.Combine(databasePath, "data.kz");
            DataFilePath = dataPath;

            try
            {
                if (_readOnly)
                {
                    _fileStream = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.RandomAccess);
                }
                else
                {
                    // FileShare.Read allows concurrent readers to open the same data file.
                    // Write exclusivity is enforced by DatabaseLockManager's .lock file,
                    // not at the data file handle level.
                    _fileStream = new FileStream(dataPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.RandomAccess);
                }
            }
            catch (IOException ex)
            {
                throw new DatabaseBusyException(databasePath, -1, null, ex);
            }
        }

        public void WritePage(long pageId, ReadOnlySpan<byte> data)
        {
            if (_readOnly)
            {
                throw new InvalidOperationException("Database is open in read-only mode.");
            }

            long offset = pageId * 4096; // 4KB Page Size Constants
            _fileStream.Seek(offset, SeekOrigin.Begin);
            _fileStream.Write(data);
        }

        public void ReadPage(long pageId, Span<byte> data)
        {
            long offset = pageId * 4096;
            _fileStream.Seek(offset, SeekOrigin.Begin);
            
            int bytesRead = _fileStream.Read(data);
            if (bytesRead < data.Length)
            {
                data.Slice(bytesRead).Clear(); // Zero-pad incomplete pages
            }
        }

        public void Flush()
        {
            if (_readOnly)
            {
                return;
            }

            _fileStream.Flush(true); // Hard flush to OS buffers
        }

        public void Dispose()
        {
            if (!_readOnly)
            {
                _fileStream?.Flush();
            }
            _fileStream?.Dispose();
        }
    }
}
