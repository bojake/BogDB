using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BogDb.Core.Main;
using BogDb.Core.Transaction;

namespace BogDb.Core.Storage;

internal sealed class GraphLogWriter : IDisposable
{
    private const int PageSize = 4096;
    private readonly string _logPath;
    private readonly WAL _wal;
    private readonly bool _inMemory;
    private readonly bool _readOnly;
    private readonly object _lock = new object();
    private FileStream? _stream;
    private BinaryWriter? _writer;

    public GraphLogWriter(string dbPath, WAL wal, bool inMemory, bool readOnly = false)
    {
        _inMemory = inMemory;
        _readOnly = readOnly;
        _wal = wal;
        _logPath = Path.Combine(dbPath, "graph-log.bin");

        if (_inMemory || _readOnly) return;

        Directory.CreateDirectory(dbPath);
        _stream = new FileStream(_logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        _stream.Seek(0, SeekOrigin.End);
        _writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
    }

    public void AppendNode(string tableName, object id, Dictionary<string, object> props)
    {
        AppendRecord(1, tableName, id, null, props);
    }

    public void AppendNodeDelete(string tableName, object id)
    {
        AppendRecord(3, tableName, id, null, props: null);
    }

    public void AppendRel(string tableName, object fromId, object toId, Dictionary<string, object> props)
    {
        AppendRecord(2, tableName, fromId, toId, props);
    }

    public void AppendRelInsert(string tableName, object fromId, object toId, Dictionary<string, object> props)
    {
        AppendRecord(5, tableName, fromId, toId, props);
    }

    public void AppendRelDelete(string tableName, object fromId, object toId)
    {
        AppendRecord(4, tableName, fromId, toId, props: null);
    }

    public void Clear()
    {
        if (_inMemory || _readOnly || _stream == null) return;
        lock (_lock)
        {
            _stream.SetLength(0);
            _stream.Seek(0, SeekOrigin.Begin);
            _stream.Flush(true);
        }
    }

    public ulong GetFileSize()
    {
        if (_inMemory || _stream == null)
            return 0;

        lock (_lock)
        {
            return checked((ulong)_stream.Length);
        }
    }

    public void Truncate(ulong length)
    {
        if (_inMemory || _readOnly || _stream == null) return;

        lock (_lock)
        {
            _stream.SetLength(checked((long)length));
            _stream.Seek(0, SeekOrigin.End);
            _stream.Flush(true);
        }
    }

    private void AppendRecord(byte recordType, string tableName, object id, object? id2, Dictionary<string, object>? props)
    {
        if (_inMemory || _readOnly || _stream == null || _writer == null) return;

        lock (_lock)
        {
            var startOffset = _stream.Position;

            _writer.Write(recordType);
            _writer.Write(tableName);
            GraphDataSerializer.WriteValue(_writer, id);
            if (recordType is 2 or 4 or 5)
            {
                GraphDataSerializer.WriteValue(_writer, id2);
            }
            if (recordType is 1 or 2 or 5)
            {
                GraphDataSerializer.WriteProperties(_writer, props ?? new Dictionary<string, object>());
            }
            _writer.Flush();
            _stream.Flush(true);

            var endOffset = _stream.Position;
            LogTouchedPages(startOffset, endOffset);
        }
    }

    private void LogTouchedPages(long startOffset, long endOffset)
    {
        if (_stream == null) return;

        var startPage = (int)(startOffset / PageSize);
        var endPage = (int)((endOffset - 1) / PageSize);

        var originalPos = _stream.Position;
        for (var page = startPage; page <= endPage; page++)
        {
            var buffer = new byte[PageSize];
            _stream.Seek(page * PageSize, SeekOrigin.Begin);
            var read = _stream.Read(buffer, 0, buffer.Length);
            if (read < buffer.Length)
            {
                Array.Clear(buffer, read, buffer.Length - read);
            }
            _wal.LogPageUpdateWithData(_logPath, (ulong)page, buffer, PageSize);
        }
        _stream.Seek(originalPos, SeekOrigin.Begin);
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _stream?.Dispose();
    }
}

internal static class GraphLogReader
{
    public static void ApplyLog(string dbPath, Dictionary<string, NodeTableData> nodeTables, Dictionary<string, RelTableData> relTables)
    {
        ApplyLog(dbPath, nodeTables, relTables, long.MaxValue);
    }

    public static void ApplyLog(
        string dbPath,
        Dictionary<string, NodeTableData> nodeTables,
        Dictionary<string, RelTableData> relTables,
        long maxOffset)
    {
        var logPath = Path.Combine(dbPath, "graph-log.bin");
        if (!File.Exists(logPath)) return;

        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0 || maxOffset <= 0) return;

        var readLimit = Math.Min(stream.Length, maxOffset);
        if (readLimit <= 0) return;

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        while (stream.Position < readLimit)
        {
            byte recordType;
            try
            {
                recordType = reader.ReadByte();
            }
            catch (EndOfStreamException)
            {
                return;
            }

            if (recordType == 0)
            {
                // Treat zero record type as padding/EOF for resiliency.
                _ = IsRemainingZero(reader);
                return;
            }

            try
            {
                var tableName = reader.ReadString();
                var id = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();

                if (recordType == 1)
                {
                    var props = GraphDataSerializer.ReadProperties(reader);
                    if (!nodeTables.TryGetValue(tableName, out var table))
                    {
                        table = new NodeTableData();
                        nodeTables[tableName] = table;
                    }
                    table.Upsert(id, props);
                }
                else if (recordType == 2)
                {
                    var id2 = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                    var props = GraphDataSerializer.ReadProperties(reader);
                    if (!relTables.TryGetValue(tableName, out var table))
                    {
                        table = new RelTableData();
                        relTables[tableName] = table;
                    }
                    var key = new EdgeKey(id, id2);
                    table.Upsert(key, props);
                }
                else if (recordType == 3)
                {
                    if (nodeTables.TryGetValue(tableName, out var table))
                        table.Remove(id);
                }
                else if (recordType == 4)
                {
                    var id2 = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                    if (relTables.TryGetValue(tableName, out var table))
                    {
                        var key = new EdgeKey(id, id2);
                        table.Remove(key);
                    }
                }
                else if (recordType == 5)
                {
                    var id2 = GraphDataSerializer.ReadValue(reader) ?? Guid.NewGuid().ToString();
                    var props = GraphDataSerializer.ReadProperties(reader);
                    if (!relTables.TryGetValue(tableName, out var table))
                    {
                        table = new RelTableData();
                        relTables[tableName] = table;
                    }
                    var key = new EdgeKey(id, id2);
                    table.Insert(key, props);
                }
                else
                {
                    throw new InvalidDataException($"Unknown graph log record type: {recordType}");
                }
            }
            catch (EndOfStreamException)
            {
                return;
            }
        }
    }

    private static bool IsRemainingZero(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        var buffer = new byte[4096];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != 0)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
