using System;

namespace BogDb.Core.Storage
{
    /// <summary>
    /// Thrown when a database cannot be opened because another writer already holds
    /// the database lock. Clients should catch this instead of raw IOException
    /// to detect concurrent-open scenarios.
    /// </summary>
    public sealed class DatabaseBusyException : InvalidOperationException
    {
        /// <summary>Path to the database directory that is locked.</summary>
        public string DatabasePath { get; }

        /// <summary>PID of the process that currently owns the write lock, or -1 if unknown.</summary>
        public int OwnerProcessId { get; }

        /// <summary>UTC timestamp when the owning process acquired the lock, or null if unknown.</summary>
        public DateTimeOffset? OwnerTimestamp { get; }

        public DatabaseBusyException(string databasePath, int ownerProcessId, DateTimeOffset? ownerTimestamp)
            : base(FormatMessage(databasePath, ownerProcessId, ownerTimestamp))
        {
            DatabasePath = databasePath;
            OwnerProcessId = ownerProcessId;
            OwnerTimestamp = ownerTimestamp;
        }

        public DatabaseBusyException(string databasePath, int ownerProcessId, DateTimeOffset? ownerTimestamp, Exception innerException)
            : base(FormatMessage(databasePath, ownerProcessId, ownerTimestamp), innerException)
        {
            DatabasePath = databasePath;
            OwnerProcessId = ownerProcessId;
            OwnerTimestamp = ownerTimestamp;
        }

        private static string FormatMessage(string databasePath, int ownerProcessId, DateTimeOffset? ownerTimestamp)
        {
            var owner = ownerProcessId > 0
                ? $" (PID {ownerProcessId}, since {ownerTimestamp?.ToString("o") ?? "unknown"})"
                : "";
            return $"Database at '{databasePath}' is currently in use by another writer{owner}. " +
                   "Only one writer is allowed at a time.";
        }
    }
}
