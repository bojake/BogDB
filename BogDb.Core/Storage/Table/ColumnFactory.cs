using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using BogDb.Core.Main;
using BogDb.Core.Storage.BufferManager;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Factory for creating Column instances with the appropriate storage backend.
/// For in-memory databases, creates standard ColumnChunk-backed columns.
/// For file-backed databases, creates PageBackedColumn-backed columns with
/// file-backed DiskArray storage in a <c>columns/</c> subdirectory.
///
/// The factory also manages a column manifest that tracks all created columns,
/// enabling reopen of existing column files on database restart.
/// </summary>
public sealed class ColumnFactory : IDisposable
{
    private const string ColumnsDirName = "columns";
    private const string ManifestFileName = "manifest.json";

    private readonly BufferManager.BufferManager _bufferManager;
    private readonly string _dbPath;
    private readonly string _columnsDir;
    private readonly bool _isInMemory;
    private uint _nextFileIndex;

    // Registry of all page-backed columns for checkpoint and manifest
    private readonly Dictionary<string, ColumnEntry> _columnEntries = new(StringComparer.OrdinalIgnoreCase);

    public ColumnFactory(BufferManager.BufferManager bufferManager, string dbPath, bool isInMemory)
    {
        _bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
        _dbPath = dbPath;
        _isInMemory = isInMemory;
        _nextFileIndex = 1000; // Start above system-reserved file indices

        if (!_isInMemory)
        {
            _columnsDir = Path.Combine(dbPath, ColumnsDirName);
            Directory.CreateDirectory(_columnsDir);
        }
        else
        {
            _columnsDir = string.Empty;
        }
    }

    /// <summary>
    /// Creates a Column with the appropriate storage backend.
    /// </summary>
    public Column CreateColumn(string name, int initialCapacity = 1024)
    {
        if (_isInMemory)
            return new Column(name, initialCapacity);

        return CreatePageBackedColumn(name, initialCapacity);
    }

    /// <summary>
    /// Flushes all page-backed columns to disk (overflow + DiskArray).
    /// Call this during PersistState/Checkpoint.
    /// </summary>
    public void CheckpointAll()
    {
        if (_isInMemory) return;

        foreach (var entry in _columnEntries.Values)
        {
            entry.PageBacked?.Flush();
        }

        WriteManifest();
    }

    /// <summary>
    /// Attempts to load existing columns from a previously-written manifest.
    /// Returns a dictionary of (registryKey → Column) for all recovered columns.
    /// </summary>
    public Dictionary<string, Column>? TryLoadExistingColumns()
    {
        if (_isInMemory) return null;

        var manifestPath = Path.Combine(_columnsDir, ManifestFileName);
        if (!File.Exists(manifestPath)) return null;

        try
        {
            var json = File.ReadAllText(manifestPath);
            var entries = JsonSerializer.Deserialize<ManifestEntry[]>(json);
            if (entries == null || entries.Length == 0) return null;

            var result = new Dictionary<string, Column>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var filePath = Path.Combine(_columnsDir, entry.FileName);
                if (!File.Exists(filePath)) continue;

                var fileIndex = (uint)Interlocked.Increment(ref _nextFileIndex);
                var fileHandle = _bufferManager.GetFileHandle(filePath, 0x00, fileIndex);

                var pageBacked = new PageBackedColumn(fileHandle);
                var column = new Column(entry.ColumnName, pageBacked, 1024);

                var registryKey = $"{entry.TableName}:{entry.ColumnName}";
                _columnEntries[registryKey] = new ColumnEntry
                {
                    TableName = entry.TableName,
                    ColumnName = entry.ColumnName,
                    FileName = entry.FileName,
                    PageBacked = pageBacked,
                    Column = column
                };

                result[registryKey] = column;
            }

            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null; // Manifest corrupt — fall back to legacy load
        }
    }

    /// <summary>
    /// Registers a table context for column tracking. Called during BindPersistenceSurface.
    /// </summary>
    public void SetTableContext(string tableName)
    {
        // Currently a no-op; table context is captured per-column in CreatePageBackedColumn.
        // This method exists as a hook for future per-table directory layout.
    }

    private string? _currentTableName;

    /// <summary>
    /// Sets the current table name context for subsequent CreateColumn calls.
    /// </summary>
    public void BeginTable(string tableName)
    {
        _currentTableName = tableName;
    }

    private Column CreatePageBackedColumn(string name, int initialCapacity)
    {
        var fileIndex = (uint)Interlocked.Increment(ref _nextFileIndex);
        var safeName = SanitizeFileName(name);
        var fileName = $"col_{safeName}_{fileIndex}.kz";
        var filePath = Path.Combine(_columnsDir, fileName);

        // File-backed FileHandle — pages are memory-mapped to disk
        var fileHandle = _bufferManager.GetFileHandle(filePath, 0x00, fileIndex);

        var pageBacked = new PageBackedColumn(fileHandle, PageBackedColumn.ColumnTypeTag.Dynamic);
        var column = new Column(name, pageBacked, initialCapacity);

        var tableName = _currentTableName ?? "__unknown";
        var registryKey = $"{tableName}:{name}";
        _columnEntries[registryKey] = new ColumnEntry
        {
            TableName = tableName,
            ColumnName = name,
            FileName = fileName,
            PageBacked = pageBacked,
            Column = column
        };

        return column;
    }

    private void WriteManifest()
    {
        var entries = new List<ManifestEntry>();
        foreach (var entry in _columnEntries.Values)
        {
            entries.Add(new ManifestEntry
            {
                TableName = entry.TableName,
                ColumnName = entry.ColumnName,
                FileName = entry.FileName,
                Count = entry.PageBacked?.Count ?? 0
            });
        }

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        var manifestPath = Path.Combine(_columnsDir, ManifestFileName);
        File.WriteAllText(manifestPath, json);
    }

    private static string SanitizeFileName(string name)
    {
        var safe = new char[Math.Min(name.Length, 32)];
        for (int i = 0; i < safe.Length; i++)
        {
            var c = name[i];
            safe[i] = char.IsLetterOrDigit(c) || c == '_' ? c : '_';
        }
        return new string(safe);
    }

    public void Dispose()
    {
        // FileHandles are owned by BufferManager — it handles disposal.
        // We just need to clear our references.
        _columnEntries.Clear();
    }

    // ─── Internal types ───────────────────────────────────────────────

    private class ColumnEntry
    {
        public string TableName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public PageBackedColumn? PageBacked { get; set; }
        public Column? Column { get; set; }
    }

    private class ManifestEntry
    {
        public string TableName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Count { get; set; }
    }
}
