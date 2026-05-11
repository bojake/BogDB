using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Index;

/// <summary>
/// Disk-backed INodeIndex that checkpoints its data to a binary file and
/// restores on reopen. Uses InMemoryNodeIndex for in-process operations and
/// persists entries to disk during checkpoint.
///
/// File format:
///   [4 bytes] magic (0x4B5A4958 = "KZIX")
///   [4 bytes] version (1)
///   [8 bytes] entry count
///   For each entry:
///     [4 bytes] key type (0=string, 1=int64, 2=double, 3=bool)
///     [variable] key data (length-prefixed for strings)
///     [4 bytes] offset count
///     For each offset:
///       [8 bytes] offset value
///
/// C++ parity: mirrors the in-memory → disk checkpoint pattern from
/// src/storage/index/hash_index.cpp (checkpoint/prepareCommit paths).
/// </summary>
public sealed class DiskBackedNodeIndex : INodeIndex, IDisposable
{
    private const uint MAGIC = 0x4B5A4958; // "KZIX"
    private const uint VERSION = 1;

    private readonly InMemoryNodeIndex _inner = new();
    private readonly string _filePath;
    private bool _dirty;

    /// <summary>
    /// Create a new disk-backed index that will persist to <paramref name="filePath"/>.
    /// If the file already exists, its contents are loaded into memory.
    /// </summary>
    public DiskBackedNodeIndex(string filePath)
    {
        _filePath = filePath;

        if (File.Exists(filePath))
            LoadFromDisk();
    }

    public long Count => _inner.Count;

    public void Put(object key, long nodeOffset)
    {
        _inner.Put(key, nodeOffset);
        _dirty = true;
    }

    public bool TryLookup(object key, out long nodeOffset)
        => _inner.TryLookup(key, out nodeOffset);

    public bool TryLookupAll(object key, out IReadOnlyList<long> nodeOffsets)
        => _inner.TryLookupAll(key, out nodeOffsets);

    public bool Remove(object key, long nodeOffset)
    {
        var removed = _inner.Remove(key, nodeOffset);
        if (removed) _dirty = true;
        return removed;
    }

    public IEnumerable<KeyValuePair<object, IReadOnlyList<long>>> EnumerateEntries()
        => _inner.EnumerateEntries();

    public void Clear()
    {
        _inner.Clear();
        _dirty = true;
    }

    /// <summary>
    /// True if the in-memory state has diverged from disk since the last checkpoint.
    /// </summary>
    public bool IsDirty => _dirty;

    /// <summary>
    /// The file path this index persists to.
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Persist the current in-memory state to disk. Called during database checkpoint.
    /// </summary>
    public void Checkpoint()
    {
        if (!_dirty) return;

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var fs = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, Encoding.UTF8);

        writer.Write(MAGIC);
        writer.Write(VERSION);

        var entries = _inner.EnumerateEntries().ToList();
        writer.Write((long)entries.Count);

        foreach (var (key, offsets) in entries)
        {
            WriteKey(writer, key);
            writer.Write(offsets.Count);
            foreach (var offset in offsets)
                writer.Write(offset);
        }

        _dirty = false;
    }

    /// <summary>
    /// Load entries from the persisted file into the in-memory index.
    /// </summary>
    private void LoadFromDisk()
    {
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(fs, Encoding.UTF8);

        var magic = reader.ReadUInt32();
        if (magic != MAGIC)
            throw new InvalidDataException($"Invalid index file magic: 0x{magic:X8} (expected 0x{MAGIC:X8})");

        var version = reader.ReadUInt32();
        if (version != VERSION)
            throw new InvalidDataException($"Unsupported index file version: {version} (expected {VERSION})");

        var entryCount = reader.ReadInt64();
        for (var i = 0L; i < entryCount; i++)
        {
            var key = ReadKey(reader);
            var offsetCount = reader.ReadInt32();
            for (var j = 0; j < offsetCount; j++)
            {
                var offset = reader.ReadInt64();
                _inner.Put(key, offset);
            }
        }

        _dirty = false;
    }

    private static void WriteKey(BinaryWriter writer, object key)
    {
        switch (key)
        {
            case string s:
                writer.Write((int)0);
                writer.Write(s);
                break;
            case long l:
                writer.Write((int)1);
                writer.Write(l);
                break;
            case double d:
                writer.Write((int)2);
                writer.Write(d);
                break;
            case bool b:
                writer.Write((int)3);
                writer.Write(b);
                break;
            case int i:
                writer.Write((int)1);
                writer.Write((long)i);
                break;
            default:
                // Fall back to string representation
                writer.Write((int)0);
                writer.Write(key.ToString() ?? "");
                break;
        }
    }

    private static object ReadKey(BinaryReader reader)
    {
        var keyType = reader.ReadInt32();
        return keyType switch
        {
            0 => (object)reader.ReadString(),
            1 => reader.ReadInt64(),
            2 => reader.ReadDouble(),
            3 => reader.ReadBoolean(),
            _ => throw new InvalidDataException($"Unknown key type: {keyType}")
        };
    }

    public void Dispose()
    {
        // Flush if dirty on dispose
        if (_dirty)
        {
            try { Checkpoint(); } catch { /* best effort */ }
        }
    }
}
