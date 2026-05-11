using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace BogDb.Core.Storage
{
    /// <summary>
    /// Manages an exclusive .lock file in the database directory to enforce
    /// single-writer ownership across processes. The OS automatically releases
    /// the file handle on process crash, so the lock is crash-safe.
    ///
    /// Two files are used:
    ///   .lock      — held open with FileShare.None for exclusivity (empty sentinel)
    ///   .lock.info — readable metadata (PID, timestamp) written after acquiring the lock
    /// </summary>
    public sealed class DatabaseLockManager : IDisposable
    {
        private const string LockFileName = ".lock";
        private const string LockInfoFileName = ".lock.info";

        private FileStream? _lockStream;
        private readonly string _lockFilePath;
        private readonly string _lockInfoFilePath;
        private readonly string _databasePath;
        private bool _disposed;

        /// <summary>Whether this instance currently holds the write lock.</summary>
        public bool IsLockHeld => _lockStream != null && !_disposed;

        private DatabaseLockManager(string databasePath)
        {
            _databasePath = databasePath;
            _lockFilePath = Path.Combine(databasePath, LockFileName);
            _lockInfoFilePath = Path.Combine(databasePath, LockInfoFileName);
        }

        /// <summary>
        /// Acquires the write lock for the database at <paramref name="databasePath"/>.
        /// Throws <see cref="DatabaseBusyException"/> if another process already holds it.
        /// Returns null (no-op) for in-memory databases.
        /// </summary>
        public static DatabaseLockManager? Acquire(string databasePath)
        {
            if (string.Equals(databasePath, ":memory:", StringComparison.OrdinalIgnoreCase))
                return null;

            var manager = new DatabaseLockManager(databasePath);
            manager.AcquireInternal();
            return manager;
        }

        private void AcquireInternal()
        {
            Directory.CreateDirectory(_databasePath);

            try
            {
                // FileShare.None ensures only one process can hold this file open.
                // This is the SINGLE exclusive-lock point for the entire database.
                // On macOS/Unix this works cross-process via flock semantics.
                _lockStream = new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4,
                    FileOptions.None);
            }
            catch (IOException ex)
            {
                // Another process holds the lock — read its metadata for diagnostics
                var (ownerPid, ownerTimestamp) = TryReadLockOwner();
                throw new DatabaseBusyException(_databasePath, ownerPid, ownerTimestamp, ex);
            }

            // Write diagnostic metadata to a separate, readable info file
            WriteLockInfo();
        }

        private void WriteLockInfo()
        {
            try
            {
                File.WriteAllText(_lockInfoFilePath,
                    Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n" +
                    DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\n");
            }
            catch
            {
                // Info file is best-effort diagnostics
            }
        }

        /// <summary>
        /// Attempts to read the PID and timestamp from the lock info file.
        /// Returns (-1, null) if the file can't be read.
        /// </summary>
        private (int pid, DateTimeOffset? timestamp) TryReadLockOwner()
        {
            try
            {
                if (!File.Exists(_lockInfoFilePath))
                    return (-1, null);

                var lines = File.ReadAllLines(_lockInfoFilePath);

                int pid = -1;
                DateTimeOffset? ts = null;

                if (lines.Length > 0 && int.TryParse(lines[0].Trim(), out var parsedPid))
                    pid = parsedPid;

                if (lines.Length > 1 && DateTimeOffset.TryParse(lines[1].Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var parsedTs))
                    ts = parsedTs;

                return (pid, ts);
            }
            catch
            {
                return (-1, null);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _lockStream?.Dispose();
            _lockStream = null;

            // Best-effort cleanup of lock files
            TryDeleteFile(_lockFilePath);
            TryDeleteFile(_lockInfoFilePath);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // File deletion is best-effort
            }
        }
    }
}
