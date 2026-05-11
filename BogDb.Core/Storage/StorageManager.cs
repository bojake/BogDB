using System;
using BogDb.Core.Storage.Store;
using BogDb.Core.Transaction;

namespace BogDb.Core.Storage
{
    public class StorageManager : IDisposable
    {
        private ShadowFile _shadowFile;
        private FreeSpaceManager _freeSpaceManager;
        private WAL _wal;
        private readonly bool _inMemory;
        private readonly bool _readOnly;
        private readonly string _databasePath;
        private readonly DatabaseLockManager? _lockManager;
        private readonly DiskStorageManager? _diskStorageManager;

        public StorageManager(string databasePath, bool readOnly = false, DatabaseLockManager? preAcquiredLockManager = null)
        {
            _databasePath = databasePath;
            _inMemory = string.Equals(databasePath, ":memory:", StringComparison.OrdinalIgnoreCase);
            _readOnly = readOnly;
            if (!_inMemory)
            {
                System.IO.Directory.CreateDirectory(databasePath);
            }

            // Acquire the database write lock BEFORE opening any storage files.
            // For in-memory databases, Acquire() returns null (no lock needed).
            _lockManager = _readOnly
                ? null
                : preAcquiredLockManager ?? DatabaseLockManager.Acquire(databasePath);

            _shadowFile = new ShadowFile(databasePath);
            _freeSpaceManager = new FreeSpaceManager();
            _wal = new WAL(
                databasePath,
                readOnly: _readOnly,
                inMemory: _inMemory);
            _diskStorageManager = _inMemory ? null : new DiskStorageManager(databasePath, _readOnly);
        }

        public ShadowFile GetShadowFile() => _shadowFile;
        
        public FreeSpaceManager GetFreeSpaceManager() => _freeSpaceManager;

        public WAL GetWAL() => _wal;
        
        public bool IsInMemory => _inMemory;
        public bool IsReadOnly => _readOnly;

        /// <summary>Whether this StorageManager currently holds the database write lock.</summary>
        public bool IsWriteLockHeld => _lockManager?.IsLockHeld ?? false;

        /// <summary>
        /// Returns the current graph-log file size, used to populate the WAL commit record
        /// with the committed graph-log offset for crash recovery.
        /// </summary>
        public long GetGraphLogFileSize()
        {
            if (_inMemory) return 0;
            var graphLogPath = System.IO.Path.Combine(_databasePath, "graph-log.bin");
            if (!System.IO.File.Exists(graphLogPath)) return 0;
            return new System.IO.FileInfo(graphLogPath).Length;
        }

        public void WritePage(long pageId, ReadOnlySpan<byte> data)
        {
            if (_inMemory || _diskStorageManager == null)
            {
                return;
            }

            if (_readOnly)
            {
                throw new InvalidOperationException("Database is open in read-only mode.");
            }

            // Log the page update (with payload) before applying it to disk.
            _wal.LogPageUpdateWithData(_diskStorageManager.DataFilePath, (ulong)pageId, data, (uint)data.Length);
            _diskStorageManager.WritePage(pageId, data);
        }

        public void ReadPage(long pageId, Span<byte> data)
        {
            if (_inMemory || _diskStorageManager == null)
            {
                data.Clear();
                return;
            }

            _diskStorageManager.ReadPage(pageId, data);
        }

        public bool Checkpoint()
        {
            var hasShadowPages = _shadowFile.GetNumShadowPages() > 0;
            var hasUncheckpointedFreePages = _freeSpaceManager.HasUncheckpointedChanges();
            return hasShadowPages || hasUncheckpointedFreePages;
        }

        public void FinalizeCheckpoint()
        {
            _freeSpaceManager.FinalizeCheckpoint();
        }

        public void RollbackCheckpoint()
        {
            _freeSpaceManager.RollbackCheckpoint();
        }

        public void Dispose()
        {
            _diskStorageManager?.Dispose();
            _wal.Dispose();
            _lockManager?.Dispose();
        }
    }
}
