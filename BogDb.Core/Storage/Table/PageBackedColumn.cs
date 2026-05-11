using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BogDb.Core.Main;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Page-backed column storage using fixed-size value slots in 4KB pages.
///
/// Layout:
///   Page 0: Header page (metadata: value count, type tag, string overflow page start)
///   Pages 1..N: Data pages containing fixed-size value slots
///   Pages N+1..M: String overflow pages (variable-length string data)
///
/// Each value slot is 16 bytes:
///   Byte 0: Flags (bit 0 = null)
///   Bytes 1-8: Value payload (as long/double depending on column type)
///   Bytes 9-15: Reserved (string overflow offset for STRING columns)
///
/// This provides the foundation for disk-backed columnar storage. Pages are
/// managed through FileHandle which supports both file-backed (memory-mapped)
/// and in-memory modes.
/// </summary>
public sealed class PageBackedColumn : IDisposable
{
    private const int PageSize = 4096;
    private const int SlotSize = 16;
    private const int SlotsPerPage = PageSize / SlotSize; // 256 values per page
    private const int HeaderPageIdx = 0;

    // Slot flags
    private const byte FlagNull = 0x01;

    // Header layout (Page 0)
    private const int HeaderOffset_NumValues = 0;       // 8 bytes: long
    private const int HeaderOffset_TypeTag = 8;          // 1 byte
    private const int HeaderOffset_OverflowPageCount = 9; // 4 bytes: int
    private const int HeaderOffset_MaxOverflowId = 13;   // 4 bytes: int

    // DiskArray Header offsets within Page 0
    private const int HeaderOffset_DiskArrayNumElements = 20; // 8 bytes: ulong
    private const int HeaderOffset_DiskArrayFirstPipIdx = 28; // 4 bytes: uint
    private const int HeaderOffset_OverflowStartPage = 32;    // 4 bytes: uint

    private readonly FileHandle _fileHandle;
    private long _numValues;
    private readonly ColumnTypeTag _typeTag;
    
    // The disk storage array for slots
    private readonly DiskArray<ValueSlot> _diskArray;
    private DiskArrayHeader _arrayHeader;

    // Value overflow: maps overflowId → managed object (strings, arrays, complex types)
    // Persisted to overflow pages via GraphDataSerializer on Flush().
    private readonly List<object?> _overflowStore = new();
    private int _nextOverflowId;
    private int _overflowPageCount;
    private uint _overflowStartPageIdx;

    // Cached page buffer to avoid repeated allocation
    private readonly byte[] _pageBuffer = new byte[PageSize];

    /// <summary>
    /// Supported column type tags for serialization.
    /// </summary>
    public enum ColumnTypeTag : byte
    {
        Int64 = 1,
        Double = 2,
        Int32 = 3,
        Bool = 4,
        String = 5,
        Float = 6,
        Int16 = 7,
        Int8 = 8,
        Date = 9,
        Timestamp = 10,
        /// <summary>
        /// Dynamic type: the runtime type tag is stored per-slot in Pad1 (byte 13).
        /// This allows heterogeneously-typed columns in graph property storage.
        /// </summary>
        Dynamic = 255,
    }

    /// <summary>
    /// Creates a new page-backed column with the given FileHandle and type.
    /// Allocates the header page.
    /// </summary>
    public PageBackedColumn(FileHandle fileHandle, ColumnTypeTag typeTag)
    {
        _fileHandle = fileHandle ?? throw new ArgumentNullException(nameof(fileHandle));
        _typeTag = typeTag;
        _numValues = 0;
        _nextOverflowId = 0;
        _overflowPageCount = 0;
        _overflowStartPageIdx = 0;

        // Allocate header page
        while (_fileHandle.NumPages <= HeaderPageIdx)
            _fileHandle.AddNewPage();

        _arrayHeader = new DiskArrayHeader
        {
            NumElements = 0,
            FirstPIPPageIdx = BogDb.Core.Common.Constants.INVALID_PAGE_IDX,
            Padding = 0
        };
        _diskArray = new DiskArray<ValueSlot>(_fileHandle, _arrayHeader, _arrayHeader);

        FlushHeader();
    }

    /// <summary>
    /// Opens an existing page-backed column from a FileHandle by reading its header.
    /// </summary>
    public PageBackedColumn(FileHandle fileHandle)
    {
        _fileHandle = fileHandle ?? throw new ArgumentNullException(nameof(fileHandle));

        // Read header
        _fileHandle.ReadPage(HeaderPageIdx, _pageBuffer);
        _numValues = BinaryPrimitives.ReadInt64LittleEndian(_pageBuffer.AsSpan(HeaderOffset_NumValues));
        _typeTag = (ColumnTypeTag)_pageBuffer[HeaderOffset_TypeTag];
        _overflowPageCount = BinaryPrimitives.ReadInt32LittleEndian(_pageBuffer.AsSpan(HeaderOffset_OverflowPageCount));
        _nextOverflowId = BinaryPrimitives.ReadInt32LittleEndian(_pageBuffer.AsSpan(HeaderOffset_MaxOverflowId));
        _overflowStartPageIdx = BinaryPrimitives.ReadUInt32LittleEndian(_pageBuffer.AsSpan(HeaderOffset_OverflowStartPage));

        _arrayHeader = new DiskArrayHeader
        {
            NumElements = BinaryPrimitives.ReadUInt64LittleEndian(_pageBuffer.AsSpan(HeaderOffset_DiskArrayNumElements)),
            FirstPIPPageIdx = BinaryPrimitives.ReadUInt32LittleEndian(_pageBuffer.AsSpan(HeaderOffset_DiskArrayFirstPipIdx)),
            Padding = 0
        };
        _diskArray = new DiskArray<ValueSlot>(_fileHandle, _arrayHeader, _arrayHeader);

        // Deserialize overflow values from disk
        DeserializeOverflowFromPages();
    }

    public long Count => _numValues;
    public ColumnTypeTag TypeTag => _typeTag;

    /// <summary>
    /// Appends a value to the column.
    /// </summary>
    public void Append(object? value)
    {
        long rowOffset = _numValues;
        
        WriteSlot(rowOffset, value, isAppend: true);
        _numValues++;
        
        // Update header block logic
        _arrayHeader.NumElements = _diskArray.GetNumElements();
        FlushHeader();
    }

    /// <summary>
    /// Looks up a value by row offset.
    /// </summary>
    public object? Lookup(long rowOffset)
    {
        if (rowOffset < 0 || rowOffset >= _numValues)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        return ReadSlot(rowOffset);
    }

    /// <summary>
    /// Updates a value at the given row offset.
    /// </summary>
    public void Update(long rowOffset, object? value)
    {
        if (rowOffset < 0 || rowOffset >= _numValues)
            throw new ArgumentOutOfRangeException(nameof(rowOffset));

        WriteSlot(rowOffset, value, isAppend: false);
    }

    /// <summary>
    /// Scans a range of values.
    /// </summary>
    public IEnumerable<object?> Scan(long startOffset, long count)
    {
        var end = Math.Min(startOffset + count, _numValues);
        for (long i = startOffset; i < end; i++)
            yield return ReadSlot(i);
    }

    /// <summary>
    /// Truncates the column to the specified number of values.
    /// DiskArray pages are not reclaimed (lazy deallocation matching C++ behavior).
    /// </summary>
    public void Truncate(long newCount)
    {
        if (newCount < 0 || newCount > _numValues)
            throw new ArgumentOutOfRangeException(nameof(newCount));
        _numValues = newCount;
        FlushHeader();
    }

    /// <summary>
    /// Flushes all pending state to disk, including overflow values.
    /// </summary>
    public void Flush()
    {
        // First checkpoint DiskArray PIPs and structures to disk
        _diskArray.Checkpoint();
        _diskArray.CheckpointInMemoryIfNecessary();

        // Serialize overflow values to pages
        SerializeOverflowToPages();

        FlushHeader();
    }

    public void Dispose()
    {
        try { Flush(); } catch { /* best effort */ }
    }

    // ─── Slot read/write ───────────────────────────────────────────────

    private void WriteSlot(long rowOffset, object? value, bool isAppend)
    {
        Span<byte> slotSpan = stackalloc byte[SlotSize];
        slotSpan.Clear();

        if (value is null)
        {
            slotSpan[0] = FlagNull;
            if (_typeTag == ColumnTypeTag.Dynamic)
                slotSpan[DynTagOffset] = 0; // no type for null
        }
        else
        {
            slotSpan[0] = 0; // not null
            if (_typeTag == ColumnTypeTag.Dynamic)
            {
                var dynTag = InferDynamicTag(value);
                SerializeValueWithTag(dynTag, value, slotSpan.Slice(1));
                slotSpan[DynTagOffset] = (byte)dynTag;
            }
            else
            {
                SerializeValue(value, slotSpan.Slice(1));
            }
        }

        ValueSlot slot;
        unsafe
        {
            fixed (byte* ptr = &slotSpan[0])
            {
                slot = *(ValueSlot*)ptr;
            }
        }

        if (isAppend)
        {
            _diskArray.PushBack(slot);
        }
        else
        {
            _diskArray.Update((ulong)rowOffset, slot);
        }
    }

    private object? ReadSlot(long rowOffset)
    {
        ValueSlot slot = _diskArray.Get((ulong)rowOffset);

        if ((slot.Flags & FlagNull) != 0)
            return null;

        Span<byte> slotSpan = stackalloc byte[SlotSize];
        unsafe
        {
            fixed (byte* ptr = &slotSpan[0])
            {
                *(ValueSlot*)ptr = slot;
            }
        }

        if (_typeTag == ColumnTypeTag.Dynamic)
        {
            var dynTag = (ColumnTypeTag)slotSpan[DynTagOffset];
            return DeserializeValueWithTag(dynTag, slotSpan.Slice(1));
        }

        return DeserializeValue(slotSpan.Slice(1));
    }

    // ─── Value serialization ───────────────────────────────────────────

    // Offset of the dynamic type tag within the 16-byte slot (ValueSlot.Pad1 = byte 13)
    private const int DynTagOffset = 13;

    private static ColumnTypeTag InferDynamicTag(object value) => value switch
    {
        long => ColumnTypeTag.Int64,
        int => ColumnTypeTag.Int32,
        double => ColumnTypeTag.Double,
        float => ColumnTypeTag.Float,
        bool => ColumnTypeTag.Bool,
        string => ColumnTypeTag.String,
        short => ColumnTypeTag.Int16,
        sbyte => ColumnTypeTag.Int8,
        byte => ColumnTypeTag.Int32,
        uint => ColumnTypeTag.Int64,
        ulong => ColumnTypeTag.Int64,
        decimal => ColumnTypeTag.Double,
        // Complex types (arrays, lists, etc.) go through overflow store
        System.Collections.IEnumerable => ColumnTypeTag.Dynamic, // sentinel: stored as object in overflow
        _ => ColumnTypeTag.String, // fallback: ToString()
    };

    private void SerializeValueWithTag(ColumnTypeTag tag, object value, Span<byte> payload)
    {
        switch (tag)
        {
            case ColumnTypeTag.Int64:
                BinaryPrimitives.WriteInt64LittleEndian(payload, Convert.ToInt64(value));
                break;
            case ColumnTypeTag.Double:
                BinaryPrimitives.WriteDoubleLittleEndian(payload, Convert.ToDouble(value));
                break;
            case ColumnTypeTag.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(payload, Convert.ToInt32(value));
                break;
            case ColumnTypeTag.Float:
                BinaryPrimitives.WriteSingleLittleEndian(payload, Convert.ToSingle(value));
                break;
            case ColumnTypeTag.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(payload, Convert.ToInt16(value));
                break;
            case ColumnTypeTag.Int8:
                payload[0] = (byte)Convert.ToSByte(value);
                break;
            case ColumnTypeTag.Bool:
                payload[0] = Convert.ToBoolean(value) ? (byte)1 : (byte)0;
                break;
            case ColumnTypeTag.String:
                var str = value?.ToString() ?? string.Empty;
                int strId = _nextOverflowId++;
                _overflowStore.Add(str);
                BinaryPrimitives.WriteInt32LittleEndian(payload, strId);
                break;
            case ColumnTypeTag.Dynamic:
                // Complex value — store in overflow by reference
                int objId = _nextOverflowId++;
                _overflowStore.Add(value);
                BinaryPrimitives.WriteInt32LittleEndian(payload, objId);
                break;
            default:
                BinaryPrimitives.WriteInt64LittleEndian(payload, Convert.ToInt64(value));
                break;
        }
    }

    private object? DeserializeValueWithTag(ColumnTypeTag tag, ReadOnlySpan<byte> payload) => tag switch
    {
        ColumnTypeTag.Int64 or ColumnTypeTag.Timestamp => BinaryPrimitives.ReadInt64LittleEndian(payload),
        ColumnTypeTag.Double => BinaryPrimitives.ReadDoubleLittleEndian(payload),
        ColumnTypeTag.Int32 or ColumnTypeTag.Date => BinaryPrimitives.ReadInt32LittleEndian(payload),
        ColumnTypeTag.Float => (double)BinaryPrimitives.ReadSingleLittleEndian(payload),
        ColumnTypeTag.Int16 => (long)BinaryPrimitives.ReadInt16LittleEndian(payload),
        ColumnTypeTag.Int8 => (long)(sbyte)payload[0],
        ColumnTypeTag.Bool => payload[0] != 0,
        ColumnTypeTag.String or ColumnTypeTag.Dynamic => DeserializeOverflow(payload),
        _ => BinaryPrimitives.ReadInt64LittleEndian(payload),
    };

    private object? DeserializeOverflow(ReadOnlySpan<byte> payload)
    {
        int overflowId = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (overflowId >= 0 && overflowId < _overflowStore.Count)
            return _overflowStore[overflowId];
        return null;
    }

    private void SerializeValue(object value, Span<byte> payload)
    {
        switch (_typeTag)
        {
            case ColumnTypeTag.Int64:
            case ColumnTypeTag.Timestamp:
                BinaryPrimitives.WriteInt64LittleEndian(payload, Convert.ToInt64(value));
                break;
            case ColumnTypeTag.Double:
                BinaryPrimitives.WriteDoubleLittleEndian(payload, Convert.ToDouble(value));
                break;
            case ColumnTypeTag.Int32:
            case ColumnTypeTag.Date:
                BinaryPrimitives.WriteInt32LittleEndian(payload, Convert.ToInt32(value));
                break;
            case ColumnTypeTag.Float:
                BinaryPrimitives.WriteSingleLittleEndian(payload, Convert.ToSingle(value));
                break;
            case ColumnTypeTag.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(payload, Convert.ToInt16(value));
                break;
            case ColumnTypeTag.Int8:
                payload[0] = (byte)Convert.ToSByte(value);
                break;
            case ColumnTypeTag.Bool:
                payload[0] = Convert.ToBoolean(value) ? (byte)1 : (byte)0;
                break;
            case ColumnTypeTag.String:
                var str = value.ToString() ?? string.Empty;
                int stringId = _nextOverflowId++;
                _overflowStore.Add(str);
                BinaryPrimitives.WriteInt32LittleEndian(payload, stringId);
                break;
            default:
                throw new NotSupportedException($"Unsupported column type tag: {_typeTag}");
        }
    }

    private object? DeserializeValue(ReadOnlySpan<byte> payload)
    {
        switch (_typeTag)
        {
            case ColumnTypeTag.Int64:
            case ColumnTypeTag.Timestamp:
                return BinaryPrimitives.ReadInt64LittleEndian(payload);
            case ColumnTypeTag.Double:
                return BinaryPrimitives.ReadDoubleLittleEndian(payload);
            case ColumnTypeTag.Int32:
            case ColumnTypeTag.Date:
                return BinaryPrimitives.ReadInt32LittleEndian(payload);
            case ColumnTypeTag.Float:
                return (double)BinaryPrimitives.ReadSingleLittleEndian(payload); // widen to double for consistency
            case ColumnTypeTag.Int16:
                return (long)BinaryPrimitives.ReadInt16LittleEndian(payload);
            case ColumnTypeTag.Int8:
                return (long)(sbyte)payload[0];
            case ColumnTypeTag.Bool:
                return payload[0] != 0;
            case ColumnTypeTag.String:
                int stringId = BinaryPrimitives.ReadInt32LittleEndian(payload);
                if (stringId >= 0 && stringId < _overflowStore.Count)
                    return _overflowStore[stringId];
                return null;
            default:
                throw new NotSupportedException($"Unsupported column type tag: {_typeTag}");
        }
    }

    // ─── Header page ───────────────────────────────────────────────────

    private void FlushHeader()
    {
        _arrayHeader = _diskArray.GetCurrentHeader();

        var header = new byte[PageSize];
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(HeaderOffset_NumValues), _numValues);
        header[HeaderOffset_TypeTag] = (byte)_typeTag;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(HeaderOffset_OverflowPageCount), _overflowPageCount);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(HeaderOffset_MaxOverflowId), _nextOverflowId);

        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(HeaderOffset_DiskArrayNumElements), _arrayHeader.NumElements);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(HeaderOffset_DiskArrayFirstPipIdx), _arrayHeader.FirstPIPPageIdx);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(HeaderOffset_OverflowStartPage), _overflowStartPageIdx);

        _fileHandle.WritePage(HeaderPageIdx, header);
    }

    // ─── Overflow persistence ─────────────────────────────────────────

    /// <summary>
    /// Serializes all overflow values to contiguous pages after the DiskArray data.
    /// </summary>
    private void SerializeOverflowToPages()
    {
        if (_nextOverflowId == 0)
        {
            _overflowPageCount = 0;
            return;
        }

        // Serialize all overflow values to a MemoryStream
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write(_nextOverflowId); // entry count header
        for (int i = 0; i < _nextOverflowId; i++)
        {
            var value = (i < _overflowStore.Count) ? _overflowStore[i] : null;
            GraphDataSerializer.WriteValue(writer, value);
        }
        writer.Flush();

        // Determine start page: right after any DiskArray-managed pages
        _overflowStartPageIdx = _fileHandle.NumPages;

        // Write stream contents across 4KB pages
        var data = ms.ToArray();
        int totalPages = (data.Length + PageSize - 1) / PageSize;

        for (int p = 0; p < totalPages; p++)
        {
            var page = new byte[PageSize];
            int offset = p * PageSize;
            int length = Math.Min(PageSize, data.Length - offset);
            Buffer.BlockCopy(data, offset, page, 0, length);

            // Ensure the page exists
            while (_fileHandle.NumPages <= _overflowStartPageIdx + (uint)p)
                _fileHandle.AddNewPage();

            _fileHandle.WritePage(_overflowStartPageIdx + (uint)p, page);
        }

        _overflowPageCount = totalPages;
    }

    /// <summary>
    /// Deserializes overflow values from pages written by SerializeOverflowToPages.
    /// </summary>
    private void DeserializeOverflowFromPages()
    {
        _overflowStore.Clear();

        if (_overflowPageCount == 0 || _nextOverflowId == 0)
        {
            // No overflow data — fill with nulls for compatibility
            for (int i = 0; i < _nextOverflowId; i++)
                _overflowStore.Add(null);
            return;
        }

        // Read all overflow pages into a contiguous buffer
        var data = new byte[_overflowPageCount * PageSize];
        var pageBuf = new byte[PageSize];
        for (int p = 0; p < _overflowPageCount; p++)
        {
            _fileHandle.ReadPage(_overflowStartPageIdx + (uint)p, pageBuf);
            Buffer.BlockCopy(pageBuf, 0, data, p * PageSize, PageSize);
        }

        // Deserialize values
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        int entryCount = reader.ReadInt32();
        for (int i = 0; i < entryCount; i++)
        {
            _overflowStore.Add(GraphDataSerializer.ReadValue(reader));
        }
    }
}
