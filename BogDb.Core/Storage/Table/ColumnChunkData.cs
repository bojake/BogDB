using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using BogDb.Core.Common;
using BogDb.Core.ExpressionEvaluator;
using BogDb.Core.Storage.Compression;
using BogDb.Core.Storage.BufferManager;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Replicates C++ column_chunk_data.cpp read paths. 
/// Handles grabbing Memory mapped spans from the BufferManager for evaluating chunks of Node/Edge properties.
/// </summary>
public unsafe class ColumnChunkData
{
    private const uint DictionaryRefFlag = 0x8000_0000;
    private const uint TypedDictionaryPayloadFlag = 0x8000_0000;
    private const uint OffsetTableDictionaryPayloadFlag = 0x4000_0000;
    private const uint CompactOffsetTableDictionaryPayloadFlag = 0x2000_0000;
    private const uint CompressedDictionaryPayloadFlag = 0x1000_0000;
    private const uint CompressedContainerDictionaryPayloadFlag = 0x0800_0000;
    private const uint TypedPayloadFlag = 0x8000_0000;
    private const byte TaggedNull = 0;
    private const byte TaggedInt64 = 1;
    private const byte TaggedString = 2;
    private const byte TaggedBool = 3;
    private const byte TaggedDouble = 4;
    private const byte TaggedBlob = 5;
    private const byte TaggedList = 6;
    private const byte TaggedStruct = 7;
    private const int MaxTaggedDecodeDepth = 16;

    private readonly FileHandle? _dataFH;
    private readonly uint _pageIdx;
    private readonly uint _numBytesPerValue;
    private readonly List<object?>? _inMemoryValues;
    
    // In BogDb C++ this metadata comes from catalog / disk headers
    public ColumnChunkData(FileHandle dataFH, uint pageIdx, PhysicalTypeID type)
    {
        _dataFH = dataFH;
        _pageIdx = pageIdx;
        _numBytesPerValue = GetDataTypeSizeInChunk(type);
        _inMemoryValues = null;
        DataType = ToLogicalType(type);
        Metadata = new CompressionMetadata(0L, 0L, CompressionType.UNCOMPRESSED);
    }

    public ColumnChunkData(IEnumerable<object?> values, LogicalTypeID logicalType, CompressionMetadata? metadata = null)
    {
        _inMemoryValues = new List<object?>(values);
        _dataFH = null;
        _pageIdx = 0;
        _numBytesPerValue = GetDataTypeSizeInChunk(LogicalTypeUtils.GetPhysicalType(logicalType));
        DataType = logicalType;
        Metadata = metadata ?? new CompressionMetadata(0L, 0L, CompressionType.UNCOMPRESSED);
    }

    public LogicalTypeID DataType { get; }
    public CompressionMetadata Metadata { get; }

    /// <summary>
    /// Scans values directly out of the memory mapped BufferManager Frame into the ValueVector
    /// Zero-allocation pointer copies.
    /// </summary>
    public void Scan(BufferManager.BufferManager bm, ValueVector output, uint offset, uint length, uint posInOutputVector)
    {
        if (_inMemoryValues is not null)
        {
            ScanInMemory(output, offset, length, posInOutputVector);
            return;
        }

        if (_dataFH is null)
            throw new InvalidOperationException("ColumnChunkData is not initialized with a data source.");

        var frame = bm.Pin(_dataFH, _pageIdx, PageReadPolicy.READ_PAGE);
        try
        {
            if (output.DataType is LogicalTypeID.STRING or LogicalTypeID.BLOB)
            {
                ScanFileBackedStrings(frame, bm, _dataFH, _pageIdx, output, offset, length, posInOutputVector);
                return;
            }
            if (output.DataType is LogicalTypeID.LIST or LogicalTypeID.ARRAY)
            {
                ScanFileBackedInt64Lists(frame, bm, _dataFH, _pageIdx, output, offset, length, posInOutputVector);
                return;
            }
            if (output.DataType is LogicalTypeID.STRUCT or LogicalTypeID.MAP or LogicalTypeID.UNION or
                LogicalTypeID.NODE or LogicalTypeID.REL or LogicalTypeID.RECURSIVE_REL)
            {
                ScanFileBackedInt64Structs(frame, bm, _dataFH, _pageIdx, output, offset, length, posInOutputVector);
                return;
            }

            if (RequiresComplexFileBackedDecode(output.DataType))
            {
                // Safe fallback until on-disk dictionary/overflow decode is ported.
                // Preserve scan shape without emitting invalid raw bytes.
                SetRangeNull(output, posInOutputVector, length);
                return;
            }

            byte* srcPtr = frame + (offset * _numBytesPerValue);
            
            // Bulk memory mapped replication
            output.CopyFrom(srcPtr, posInOutputVector, length);
        }
        finally
        {
            bm.Unpin(_dataFH, _pageIdx);
        }
    }

    private void ScanInMemory(ValueVector output, uint offset, uint length, uint posInOutputVector)
    {
        if (_inMemoryValues is null)
            return;

        var end = Math.Min((int)(offset + length), _inMemoryValues.Count);
        var outPos = posInOutputVector;

        for (var i = (int)offset; i < end; i++, outPos++)
        {
            var value = _inMemoryValues[i];
            WriteValueToVector(output, outPos, value);
        }
    }

    internal static void WriteValueToVector(ValueVector output, uint pos, object? value)
    {
        if (value is null)
        {
            output.SetNull(pos, true);
            return;
        }

        output.SetNull(pos, false);
        switch (output.DataType)
        {
            case LogicalTypeID.INT128:
                output.SetValue<Int128>(pos, value is Int128 i128 ? i128 : (Int128)Convert.ToInt64(value));
                break;
            case LogicalTypeID.UINT128:
                output.SetValue<UInt128>(pos, value switch
                {
                    UInt128 u128 => u128,
                    Int128 signed128 when signed128 >= 0 => (UInt128)signed128,
                    _ => (UInt128)Convert.ToUInt64(value)
                });
                break;
            case LogicalTypeID.INT64:
                output.SetValue<long>(pos, Convert.ToInt64(value));
                break;
            case LogicalTypeID.INT32:
                output.SetValue<int>(pos, Convert.ToInt32(value));
                break;
            case LogicalTypeID.INT16:
                output.SetValue<short>(pos, Convert.ToInt16(value));
                break;
            case LogicalTypeID.INT8:
                output.SetValue<sbyte>(pos, Convert.ToSByte(value));
                break;
            case LogicalTypeID.UINT64:
                output.SetValue<ulong>(pos, Convert.ToUInt64(value));
                break;
            case LogicalTypeID.UINT32:
                output.SetValue<uint>(pos, Convert.ToUInt32(value));
                break;
            case LogicalTypeID.UINT16:
                output.SetValue<ushort>(pos, Convert.ToUInt16(value));
                break;
            case LogicalTypeID.UINT8:
                output.SetValue<byte>(pos, Convert.ToByte(value));
                break;
            case LogicalTypeID.DOUBLE:
                output.SetValue<double>(pos, Convert.ToDouble(value));
                break;
            case LogicalTypeID.FLOAT:
                output.SetValue<float>(pos, Convert.ToSingle(value));
                break;
            case LogicalTypeID.BOOL:
                output.SetValue<byte>(pos, Convert.ToBoolean(value) ? (byte)1 : (byte)0);
                break;
            case LogicalTypeID.DATE:
                output.SetValue<int>(pos, Convert.ToInt32(value));
                break;
            case LogicalTypeID.INTERVAL:
                output.SetValue<long>(pos, Convert.ToInt64(value));
                break;
            case LogicalTypeID.DECIMAL:
                // Current type-info path maps DECIMAL to DOUBLE physical storage in this port.
                output.SetValue<double>(pos, Convert.ToDouble(value));
                break;
            case LogicalTypeID.TIMESTAMP:
            case LogicalTypeID.TIMESTAMP_SEC:
            case LogicalTypeID.TIMESTAMP_MS:
            case LogicalTypeID.TIMESTAMP_NS:
            case LogicalTypeID.TIMESTAMP_TZ:
            case LogicalTypeID.SERIAL:
                output.SetValue<long>(pos, Convert.ToInt64(value));
                break;
            case LogicalTypeID.INTERNAL_ID:
                if (value is InternalID internalId)
                {
                    output.SetValue<InternalID>(pos, internalId);
                }
                else if (value is long longOffset)
                {
                    output.SetValue<InternalID>(pos, new InternalID((ulong)longOffset, 0));
                }
                else if (value is ulong ulongOffset)
                {
                    output.SetValue<InternalID>(pos, new InternalID(ulongOffset, 0));
                }
                else
                {
                    throw new NotSupportedException($"Cannot coerce value '{value}' to {LogicalTypeID.INTERNAL_ID}.");
                }
                break;
            case LogicalTypeID.STRING:
            case LogicalTypeID.BLOB:
                if (value is KuString kuString)
                {
                    StringFunctionEvaluator.SetKuString(output, pos, kuString.GetAsString());
                }
                else
                {
                    StringFunctionEvaluator.SetKuString(output, pos, Convert.ToString(value) ?? string.Empty);
                }
                break;
            case LogicalTypeID.UUID:
                if (value is Int128 uuidI128)
                {
                    output.SetValue<Int128>(pos, uuidI128);
                    break;
                }
                if (value is UInt128 uuidU128)
                {
                    output.SetValue<Int128>(pos, (Int128)uuidU128);
                    break;
                }
                if (value is Guid guid)
                {
                    output.SetValue<Int128>(pos, GuidToInt128(guid));
                    break;
                }
                if (value is string s && Guid.TryParse(s, out var parsedGuid))
                {
                    output.SetValue<Int128>(pos, GuidToInt128(parsedGuid));
                    break;
                }
                throw new NotSupportedException($"Cannot coerce value '{value}' to {LogicalTypeID.UUID}.");
            case LogicalTypeID.LIST:
            case LogicalTypeID.ARRAY:
                output.SetAuxValue(pos, CloneListLike(value));
                break;
            case LogicalTypeID.STRUCT:
            case LogicalTypeID.MAP:
            case LogicalTypeID.UNION:
            case LogicalTypeID.NODE:
            case LogicalTypeID.REL:
            case LogicalTypeID.RECURSIVE_REL:
                output.SetAuxValue(pos, CloneStructLike(value));
                break;
            case LogicalTypeID.ANY:
                output.SetAuxValue(pos, value);
                break;
            default:
                throw new NotSupportedException($"In-memory scan does not support output type {output.DataType}.");
        }
    }

    private static List<object?> CloneListLike(object value)
    {
        if (value is IEnumerable<object?> typed)
            return new List<object?>(typed);
        if (value is IEnumerable enumerable && value is not string)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
                list.Add(item);
            return list;
        }

        throw new NotSupportedException($"Cannot coerce value '{value}' to nested list payload.");
    }

    private static Dictionary<string, object?> CloneStructLike(object value)
    {
        if (value is IDictionary<string, object?> typedNullable)
            return new Dictionary<string, object?>(typedNullable, StringComparer.OrdinalIgnoreCase);
        if (value is IDictionary<string, object> typed)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in typed)
                dict[k] = v;
            return dict;
        }
        if (value is IDictionary nonGeneric)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in nonGeneric)
            {
                if (entry.Key is not string key)
                    throw new NotSupportedException("Struct/map payload keys must be string.");
                dict[key] = entry.Value;
            }
            return dict;
        }

        throw new NotSupportedException($"Cannot coerce value '{value}' to nested struct payload.");
    }

    private static uint GetDataTypeSizeInChunk(PhysicalTypeID typeId)
    {
        switch (typeId)
        {
            case PhysicalTypeID.ANY: return 16;
            case PhysicalTypeID.BOOL: return 1;
            case PhysicalTypeID.INT64:
            case PhysicalTypeID.DOUBLE:
                return 8;
            case PhysicalTypeID.ALP_EXCEPTION_FLOAT: return 4;
            case PhysicalTypeID.ALP_EXCEPTION_DOUBLE: return 8;
            case PhysicalTypeID.INTERNAL_ID:
                return 16;
            case PhysicalTypeID.INT32:
            case PhysicalTypeID.FLOAT:
                return 4;
            case PhysicalTypeID.INT16: return 2;
            case PhysicalTypeID.INT8: return 1;
            case PhysicalTypeID.UINT64: return 8;
            case PhysicalTypeID.UINT32: return 4;
            case PhysicalTypeID.UINT16: return 2;
            case PhysicalTypeID.UINT8: return 1;
            case PhysicalTypeID.INT128:
            case PhysicalTypeID.UINT128:
                return 16;
            // Native C++ KuString is 16 bytes for pointer/header
            case PhysicalTypeID.STRING: return 16;
            case PhysicalTypeID.LIST:
            case PhysicalTypeID.ARRAY:
                return 12;
            case PhysicalTypeID.STRUCT:
            case PhysicalTypeID.POINTER:
                return 8;
            case PhysicalTypeID.INTERVAL:
                return 8;
            default:
                // Forward-compatible fallback for unknown/future physical IDs.
                // Use ANY-sized slots and let logical type fallback (ToLogicalType -> ANY)
                // preserve safe non-crashing behavior.
                return 16;
        }
    }

    private static LogicalTypeID ToLogicalType(PhysicalTypeID typeId)
    {
        return typeId switch
        {
            PhysicalTypeID.ANY => LogicalTypeID.ANY,
            PhysicalTypeID.BOOL => LogicalTypeID.BOOL,
            PhysicalTypeID.INT64 => LogicalTypeID.INT64,
            PhysicalTypeID.DOUBLE => LogicalTypeID.DOUBLE,
            PhysicalTypeID.INT32 => LogicalTypeID.INT32,
            PhysicalTypeID.FLOAT => LogicalTypeID.FLOAT,
            PhysicalTypeID.INT16 => LogicalTypeID.INT16,
            PhysicalTypeID.INT8 => LogicalTypeID.INT8,
            PhysicalTypeID.UINT64 => LogicalTypeID.UINT64,
            PhysicalTypeID.UINT32 => LogicalTypeID.UINT32,
            PhysicalTypeID.UINT16 => LogicalTypeID.UINT16,
            PhysicalTypeID.UINT8 => LogicalTypeID.UINT8,
            PhysicalTypeID.INT128 => LogicalTypeID.INT128,
            PhysicalTypeID.UINT128 => LogicalTypeID.UINT128,
            PhysicalTypeID.INTERVAL => LogicalTypeID.INTERVAL,
            PhysicalTypeID.STRING => LogicalTypeID.STRING,
            PhysicalTypeID.LIST => LogicalTypeID.LIST,
            PhysicalTypeID.ARRAY => LogicalTypeID.ARRAY,
            PhysicalTypeID.STRUCT => LogicalTypeID.STRUCT,
            PhysicalTypeID.POINTER => LogicalTypeID.POINTER,
            PhysicalTypeID.ALP_EXCEPTION_FLOAT => LogicalTypeID.FLOAT,
            PhysicalTypeID.ALP_EXCEPTION_DOUBLE => LogicalTypeID.DOUBLE,
            PhysicalTypeID.INTERNAL_ID => LogicalTypeID.INTERNAL_ID,
            _ => LogicalTypeID.ANY
        };
    }

    private static Int128 GuidToInt128(Guid guid)
    {
        var bytes = guid.ToByteArray();
        UInt128 value = 0;
        for (var i = 0; i < bytes.Length; i++)
            value |= (UInt128)bytes[i] << (i * 8);
        return (Int128)value;
    }

    private static bool RequiresComplexFileBackedDecode(LogicalTypeID type)
    {
        return type is LogicalTypeID.LIST or LogicalTypeID.ARRAY or LogicalTypeID.STRUCT or
            LogicalTypeID.MAP or LogicalTypeID.UNION or LogicalTypeID.NODE or
            LogicalTypeID.REL or LogicalTypeID.RECURSIVE_REL or LogicalTypeID.ANY;
    }

    private static void SetRangeNull(ValueVector output, uint startPos, uint length)
    {
        for (var i = 0u; i < length; i++)
            output.SetNull(startPos + i, true);
    }

    private static unsafe void ScanFileBackedStrings(
        byte* frame,
        BufferManager.BufferManager bm,
        FileHandle dataFH,
        uint currentPageIdx,
        ValueVector output,
        uint offset,
        uint length,
        uint posInOutputVector)
    {
        const int kuStringSize = 16;
        var utf8Buffer = new byte[(int)KuString.SHORT_STR_LENGTH];
        var entryBuffer = new byte[kuStringSize];
        var dictionaryCache = new Dictionary<ulong, List<DictionaryVarLenEntry>>();
        var pageSize = dataFH.GetPageSize();
        var baseFileOffset = ((ulong)currentPageIdx * pageSize) + ((ulong)offset * kuStringSize);

        for (var i = 0u; i < length; i++)
        {
            var dstPos = posInOutputVector + i;
            var entryFileOffset = baseFileOffset + ((ulong)i * kuStringSize);
            if (!TryReadBytesFromFile(
                    bm,
                    dataFH,
                    entryFileOffset,
                    kuStringSize,
                    currentPageIdx,
                    frame,
                    entryBuffer))
            {
                output.SetNull(dstPos, true);
                continue;
            }
            var raw = entryBuffer.AsSpan();
            var strLen = BitConverter.ToUInt32(raw.Slice(0, sizeof(uint)));

            if ((strLen & DictionaryRefFlag) != 0)
            {
                var dictIndex = strLen & ~DictionaryRefFlag;
                var dictionaryOffset = BitConverter.ToUInt64(raw.Slice(8, sizeof(ulong)));
                if (!TryReadDictionaryPayloadValue(
                        bm,
                        dataFH,
                        currentPageIdx,
                        frame,
                        dictionaryOffset,
                        dictIndex,
                        dictionaryCache,
                        out var dictionaryEntry))
                {
                    output.SetNull(dstPos, true);
                    continue;
                }

                if (dictionaryEntry.IsNull)
                {
                    output.SetNull(dstPos, true);
                    continue;
                }

                WriteVarLenPayload(output, dstPos, dictionaryEntry.Payload);
                continue;
            }

            if (KuString.IsShortString(strLen))
            {
                if (strLen == 0)
                {
                    StringFunctionEvaluator.SetKuBytes(output, dstPos, ReadOnlySpan<byte>.Empty);
                    continue;
                }

                var payload = utf8Buffer.AsSpan(0, (int)strLen);
                var prefixLen = Math.Min((int)strLen, 4);
                raw.Slice(4, prefixLen).CopyTo(payload);
                if (strLen > 4)
                    raw.Slice(8, (int)strLen - 4).CopyTo(payload.Slice(4));

                WriteVarLenPayload(output, dstPos, payload);
                continue;
            }

            // Overflow strings use bytes 8-15 as a file-byte offset in this port's disk path.
            if (strLen > int.MaxValue)
            {
                output.SetNull(dstPos, true);
                continue;
            }

            var overflowOffset = BitConverter.ToUInt64(raw.Slice(8, sizeof(ulong)));
            if (overflowOffset == 0)
            {
                output.SetNull(dstPos, true);
                continue;
            }

            if (!TryReadBytesFromFile(
                    bm,
                    dataFH,
                    overflowOffset,
                    strLen,
                    currentPageIdx,
                    frame,
                    out var utf8Bytes))
            {
                output.SetNull(dstPos, true);
                continue;
            }

            WriteVarLenPayload(output, dstPos, utf8Bytes);
        }
    }

    private readonly struct DictionaryVarLenEntry
    {
        public DictionaryVarLenEntry(byte[] payload, bool isNull)
        {
            Payload = payload;
            IsNull = isNull;
        }

        public byte[] Payload { get; }
        public bool IsNull { get; }
    }

    private static unsafe bool TryReadDictionaryPayloadValue(
        BufferManager.BufferManager bm,
        FileHandle dataFH,
        uint currentPageIdx,
        byte* currentPageFrame,
        ulong dictionaryOffset,
        uint dictionaryIndex,
        Dictionary<ulong, List<DictionaryVarLenEntry>> dictionaryCache,
        out DictionaryVarLenEntry payload)
    {
        payload = default;
        if (dictionaryOffset == 0)
            return false;

        if (!dictionaryCache.TryGetValue(dictionaryOffset, out var entries))
        {
            if (!TryReadDictionaryPayload(
                    bm,
                    dataFH,
                    currentPageIdx,
                    currentPageFrame,
                    dictionaryOffset,
                    out entries))
            {
                return false;
            }
            dictionaryCache[dictionaryOffset] = entries;
        }

        if (dictionaryIndex >= (uint)entries.Count)
            return false;
        payload = entries[(int)dictionaryIndex];
        return true;
    }

    private static unsafe bool TryReadDictionaryPayload(
        BufferManager.BufferManager bm,
        FileHandle dataFH,
        uint currentPageIdx,
        byte* currentPageFrame,
        ulong dictionaryOffset,
        out List<DictionaryVarLenEntry> entries)
    {
        entries = new List<DictionaryVarLenEntry>();
        var u32 = new byte[sizeof(uint)];
        if (!TryReadBytesFromFile(bm, dataFH, dictionaryOffset, sizeof(uint), currentPageIdx, currentPageFrame, u32))
            return false;

        var rawCount = BitConverter.ToUInt32(u32, 0);
        var offsetTablePayload = (rawCount & OffsetTableDictionaryPayloadFlag) != 0;
        var typedPayload = (rawCount & TypedDictionaryPayloadFlag) != 0;
        var compactOffsetTablePayload = (rawCount & CompactOffsetTableDictionaryPayloadFlag) != 0;
        var compressedPayload = (rawCount & CompressedDictionaryPayloadFlag) != 0;
        var compressedContainerPayload = (rawCount & CompressedContainerDictionaryPayloadFlag) != 0;
        var count = rawCount & ~(TypedDictionaryPayloadFlag | OffsetTableDictionaryPayloadFlag |
                                 CompactOffsetTableDictionaryPayloadFlag | CompressedDictionaryPayloadFlag |
                                 CompressedContainerDictionaryPayloadFlag);
        if (count == 0)
            return true;
        if (count > 4096)
            return false;

        var cursor = dictionaryOffset + sizeof(uint);
        if (offsetTablePayload)
        {
            if (compressedPayload && compressedContainerPayload)
            {
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, u32))
                    return false;
                var compressedLen = BitConverter.ToUInt32(u32, 0);
                cursor += sizeof(uint);
                if (compressedLen > 8_388_608)
                    return false;

                var compressed = new byte[compressedLen];
                if (compressedLen > 0 && !TryReadBytesFromFile(bm, dataFH, cursor, (int)compressedLen, currentPageIdx, currentPageFrame, compressed))
                    return false;
                if (!TryInflateDictionaryBlob(compressed, out var decompressed))
                    return false;

                return TryReadCompressedContainerOffsetTableEntries(
                    decompressed,
                    count,
                    typedPayload,
                    compactOffsetTablePayload,
                    entries);
            }

            // C++-adjacent payload layout for this port:
            // rawCount(with flag) + uint64[count] offsets + uint32 blobLen + byte[blobLen] blobData
            var offsets = new ulong[count];
            var u64 = new byte[sizeof(ulong)];
            for (var i = 0u; i < count; i++)
            {
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(ulong), currentPageIdx, currentPageFrame, u64))
                    return false;
                offsets[i] = BitConverter.ToUInt64(u64, 0);
                cursor += sizeof(ulong);
            }

            if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, u32))
                return false;
            var blobLen = BitConverter.ToUInt32(u32, 0);
            cursor += sizeof(uint);
            if (blobLen > 8_388_608) // 8MB guard for test/runtime safety.
                return false;

            var blob = new byte[blobLen];
            if (blobLen > 0 && !TryReadBytesFromFile(bm, dataFH, cursor, (int)blobLen, currentPageIdx, currentPageFrame, blob))
                return false;
            if (compressedPayload)
            {
                if (!TryInflateDictionaryBlob(blob, out blob))
                    return false;
            }
            var decodedBlobLen = (ulong)blob.Length;

            var prev = 0UL;
            for (var i = 0u; i < count; i++)
            {
                var start = offsets[i];
                var end = i + 1 < count ? offsets[i + 1] : decodedBlobLen;
                if (start > end || end > decodedBlobLen || start < prev)
                    return false;
                var len = (int)(end - start);
                var value = new byte[len];
                if (len > 0)
                    blob.AsSpan((int)start, len).CopyTo(value);
                if (!typedPayload)
                {
                    entries.Add(new DictionaryVarLenEntry(value, isNull: false));
                }
                else
                {
                    if (!TryParseTaggedOffsetTableEntry(value, compactOffsetTablePayload, out var typedEntry))
                        return false;
                    entries.Add(typedEntry);
                }
                prev = start;
            }
            return true;
        }

        if (compressedPayload)
        {
            if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, u32))
                return false;
            var compressedLen = BitConverter.ToUInt32(u32, 0);
            cursor += sizeof(uint);
            if (compressedLen > 8_388_608)
                return false;

            var compressed = new byte[compressedLen];
            if (compressedLen > 0 && !TryReadBytesFromFile(bm, dataFH, cursor, (int)compressedLen, currentPageIdx, currentPageFrame, compressed))
                return false;

            if (!TryInflateDictionaryBlob(compressed, out var decompressed))
                return false;

            return TryReadDictionaryEntriesFromSpan(decompressed, count, typedPayload, entries);
        }

        for (var i = 0u; i < count; i++)
        {
            if (!typedPayload)
            {
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, u32))
                    return false;
                var len = BitConverter.ToUInt32(u32, 0);
                cursor += sizeof(uint);
                if (len > 1_048_576)
                    return false;

                var value = new byte[len];
                if (len > 0 && !TryReadBytesFromFile(bm, dataFH, cursor, (int)len, currentPageIdx, currentPageFrame, value))
                    return false;
                cursor += len;
                entries.Add(new DictionaryVarLenEntry(value, isNull: false));
                continue;
            }

            var one = new byte[1];
            if (!TryReadBytesFromFile(bm, dataFH, cursor, 1, currentPageIdx, currentPageFrame, one))
                return false;
            cursor += 1;
            switch (one[0])
            {
                case TaggedNull:
                    entries.Add(new DictionaryVarLenEntry(Array.Empty<byte>(), isNull: true));
                    break;
                case TaggedString:
                case TaggedBlob:
                {
                    if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, u32))
                        return false;
                    var len = BitConverter.ToUInt32(u32, 0);
                    cursor += sizeof(uint);
                    if (len > 1_048_576)
                        return false;

                    var value = new byte[len];
                    if (len > 0 && !TryReadBytesFromFile(bm, dataFH, cursor, (int)len, currentPageIdx, currentPageFrame, value))
                        return false;
                    cursor += len;
                    entries.Add(new DictionaryVarLenEntry(value, isNull: false));
                    break;
                }
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadDictionaryEntriesFromSpan(
        byte[] buffer,
        uint count,
        bool typedPayload,
        List<DictionaryVarLenEntry> entries)
    {
        var cursor = 0;
        for (var i = 0u; i < count; i++)
        {
            if (!typedPayload)
            {
                if (cursor + sizeof(uint) > buffer.Length)
                    return false;
                var len = BitConverter.ToUInt32(buffer, cursor);
                cursor += sizeof(uint);
                if (len > 1_048_576 || cursor + len > buffer.Length)
                    return false;

                var value = new byte[len];
                if (len > 0)
                    Array.Copy(buffer, cursor, value, 0, (int)len);
                cursor += (int)len;
                entries.Add(new DictionaryVarLenEntry(value, isNull: false));
                continue;
            }

            if (cursor + 1 > buffer.Length)
                return false;
            var tag = buffer[cursor++];
            switch (tag)
            {
                case TaggedNull:
                    entries.Add(new DictionaryVarLenEntry(Array.Empty<byte>(), isNull: true));
                    break;
                case TaggedString:
                case TaggedBlob:
                {
                    if (cursor + sizeof(uint) > buffer.Length)
                        return false;
                    var len = BitConverter.ToUInt32(buffer, cursor);
                    cursor += sizeof(uint);
                    if (len > 1_048_576 || cursor + len > buffer.Length)
                        return false;

                    var value = new byte[len];
                    if (len > 0)
                        Array.Copy(buffer, cursor, value, 0, (int)len);
                    cursor += (int)len;
                    entries.Add(new DictionaryVarLenEntry(value, isNull: false));
                    break;
                }
                default:
                    return false;
            }
        }

        return cursor == buffer.Length;
    }

    private static bool TryReadCompressedContainerOffsetTableEntries(
        byte[] buffer,
        uint count,
        bool typedPayload,
        bool compactOffsetTablePayload,
        List<DictionaryVarLenEntry> entries)
    {
        var cursor = 0;
        var offsets = new ulong[count];
        for (var i = 0u; i < count; i++)
        {
            if (cursor + sizeof(ulong) > buffer.Length)
                return false;
            offsets[i] = BitConverter.ToUInt64(buffer, cursor);
            cursor += sizeof(ulong);
        }

        if (cursor + sizeof(uint) > buffer.Length)
            return false;
        var blobLen = BitConverter.ToUInt32(buffer, cursor);
        cursor += sizeof(uint);
        if (blobLen > 8_388_608 || cursor + blobLen > buffer.Length)
            return false;

        var blob = new byte[blobLen];
        if (blobLen > 0)
            Array.Copy(buffer, cursor, blob, 0, (int)blobLen);
        cursor += (int)blobLen;
        if (cursor != buffer.Length)
            return false;

        var decodedBlobLen = (ulong)blob.Length;
        var prev = 0UL;
        for (var i = 0u; i < count; i++)
        {
            var start = offsets[i];
            var end = i + 1 < count ? offsets[i + 1] : decodedBlobLen;
            if (start > end || end > decodedBlobLen || start < prev)
                return false;
            var len = (int)(end - start);
            var value = new byte[len];
            if (len > 0)
                blob.AsSpan((int)start, len).CopyTo(value);

            if (!typedPayload)
            {
                entries.Add(new DictionaryVarLenEntry(value, isNull: false));
            }
            else
            {
                if (!TryParseTaggedOffsetTableEntry(value, compactOffsetTablePayload, out var typedEntry))
                    return false;
                entries.Add(typedEntry);
            }
            prev = start;
        }

        return true;
    }

    private static bool TryInflateDictionaryBlob(byte[] compressed, out byte[] decompressed)
    {
        decompressed = Array.Empty<byte>();
        try
        {
            using var input = new MemoryStream(compressed);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            if (output.Length > 16 * 1024 * 1024) // 16MB hard guard.
                return false;
            decompressed = output.ToArray();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool TryParseTaggedOffsetTableEntry(
        byte[] entryBytes,
        bool compactOffsetTablePayload,
        out DictionaryVarLenEntry entry)
    {
        entry = default;
        if (entryBytes.Length == 0)
            return false;

        var tag = entryBytes[0];
        switch (tag)
        {
            case TaggedNull:
                if (entryBytes.Length != 1)
                    return false;
                entry = new DictionaryVarLenEntry(Array.Empty<byte>(), isNull: true);
                return true;
            case TaggedString:
            case TaggedBlob:
                if (compactOffsetTablePayload)
                {
                    var compactPayload = new byte[entryBytes.Length - 1];
                    if (compactPayload.Length > 0)
                        Array.Copy(entryBytes, 1, compactPayload, 0, compactPayload.Length);
                    entry = new DictionaryVarLenEntry(compactPayload, isNull: false);
                    return true;
                }

                if (entryBytes.Length < 1 + sizeof(uint))
                    return false;
                var len = BitConverter.ToUInt32(entryBytes, 1);
                if (len > 1_048_576)
                    return false;
                if (entryBytes.Length != 1 + sizeof(uint) + len)
                    return false;
                var payload = new byte[len];
                if (len > 0)
                    Array.Copy(entryBytes, 1 + sizeof(uint), payload, 0, (int)len);
                entry = new DictionaryVarLenEntry(payload, isNull: false);
                return true;
            default:
                return false;
        }
    }

    private static void WriteVarLenPayload(ValueVector output, uint pos, ReadOnlySpan<byte> payload)
    {
        if (output.DataType == LogicalTypeID.BLOB)
        {
            StringFunctionEvaluator.SetKuBytes(output, pos, payload);
            return;
        }

        try
        {
            StringFunctionEvaluator.SetKuString(output, pos, Encoding.UTF8.GetString(payload));
        }
        catch (DecoderFallbackException)
        {
            output.SetNull(pos, true);
        }
    }

    private static unsafe void ScanFileBackedInt64Lists(
        byte* frame,
        BufferManager.BufferManager bm,
        FileHandle dataFH,
        uint currentPageIdx,
        ValueVector output,
        uint offset,
        uint length,
        uint posInOutputVector)
    {
        const int listEntrySize = 12; // uint64 payload offset + uint32 element count
        var entryBuffer = new byte[listEntrySize];
        var pageSize = dataFH.GetPageSize();
        var baseFileOffset = ((ulong)currentPageIdx * pageSize) + ((ulong)offset * listEntrySize);

        for (var i = 0u; i < length; i++)
        {
            var dstPos = posInOutputVector + i;
            var entryFileOffset = baseFileOffset + ((ulong)i * listEntrySize);
            if (!TryReadBytesFromFile(
                    bm,
                    dataFH,
                    entryFileOffset,
                    listEntrySize,
                    currentPageIdx,
                    frame,
                    entryBuffer))
            {
                output.SetNull(dstPos, true);
                continue;
            }

            var payloadOffset = BitConverter.ToUInt64(entryBuffer.AsSpan(0, sizeof(ulong)));
            var rawElementCount = BitConverter.ToUInt32(entryBuffer.AsSpan(sizeof(ulong), sizeof(uint)));
            var typedPayload = (rawElementCount & TypedPayloadFlag) != 0;
            var elementCount = rawElementCount & ~TypedPayloadFlag;

            if (elementCount == 0)
            {
                output.SetAuxValue(dstPos, new List<object?>());
                output.SetNull(dstPos, false);
                continue;
            }
            if (payloadOffset == 0)
            {
                output.SetNull(dstPos, true);
                continue;
            }
            if (elementCount > int.MaxValue / sizeof(long))
            {
                output.SetNull(dstPos, true);
                continue;
            }

            if (!typedPayload)
            {
                var payloadLen = checked((int)elementCount * sizeof(long));
                var payload = new byte[payloadLen];
                if (!TryReadBytesFromFile(
                        bm,
                        dataFH,
                        payloadOffset,
                        payloadLen,
                        currentPageIdx,
                        frame,
                        payload))
                {
                    output.SetNull(dstPos, true);
                    continue;
                }

                var list = new List<object?>((int)elementCount);
                for (var elem = 0; elem < elementCount; elem++)
                {
                    var value = BitConverter.ToInt64(payload.AsSpan((int)elem * sizeof(long), sizeof(long)));
                    list.Add(value);
                }

                output.SetAuxValue(dstPos, list);
                output.SetNull(dstPos, false);
                continue;
            }

            var typedList = new List<object?>((int)elementCount);
            var cursor = payloadOffset;
            var valid = true;
            for (var elem = 0; elem < elementCount; elem++)
            {
                if (!TryReadTaggedValue(bm, dataFH, currentPageIdx, frame, ref cursor, out var value, 0))
                {
                    valid = false;
                    break;
                }
                typedList.Add(value);
            }

            if (!valid)
            {
                output.SetNull(dstPos, true);
                continue;
            }

            output.SetAuxValue(dstPos, typedList);
            output.SetNull(dstPos, false);
        }
    }

    private static unsafe void ScanFileBackedInt64Structs(
        byte* frame,
        BufferManager.BufferManager bm,
        FileHandle dataFH,
        uint currentPageIdx,
        ValueVector output,
        uint offset,
        uint length,
        uint posInOutputVector)
    {
        const int structEntrySize = 8; // uint64 payloadOffset
        var entryBuffer = new byte[structEntrySize];
        var u32Buffer = new byte[sizeof(uint)];
        var i64Buffer = new byte[sizeof(long)];
        var pageSize = dataFH.GetPageSize();
        var baseFileOffset = ((ulong)currentPageIdx * pageSize) + ((ulong)offset * structEntrySize);

        for (var i = 0u; i < length; i++)
        {
            var dstPos = posInOutputVector + i;
            var entryFileOffset = baseFileOffset + ((ulong)i * structEntrySize);
            if (!TryReadBytesFromFile(bm, dataFH, entryFileOffset, structEntrySize, currentPageIdx, frame, entryBuffer))
            {
                output.SetNull(dstPos, true);
                continue;
            }

            var payloadOffset = BitConverter.ToUInt64(entryBuffer, 0);
            if (payloadOffset == 0)
            {
                output.SetNull(dstPos, true);
                continue;
            }

            if (!TryReadBytesFromFile(bm, dataFH, payloadOffset, sizeof(uint), currentPageIdx, frame, u32Buffer))
            {
                output.SetNull(dstPos, true);
                continue;
            }

            var rawFieldCount = BitConverter.ToUInt32(u32Buffer, 0);
            var typedPayload = (rawFieldCount & TypedPayloadFlag) != 0;
            var fieldCount = rawFieldCount & ~TypedPayloadFlag;
            if (fieldCount > 1024)
            {
                output.SetNull(dstPos, true);
                continue;
            }

            var cursor = payloadOffset + sizeof(uint);
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var valid = true;

            for (var f = 0u; f < fieldCount; f++)
            {
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, frame, u32Buffer))
                {
                    valid = false;
                    break;
                }
                var keyLen = BitConverter.ToUInt32(u32Buffer, 0);
                cursor += sizeof(uint);

                if (keyLen == 0 || keyLen > 4096)
                {
                    valid = false;
                    break;
                }

                var keyBytes = new byte[keyLen];
                if (!TryReadBytesFromFile(bm, dataFH, cursor, (int)keyLen, currentPageIdx, frame, keyBytes))
                {
                    valid = false;
                    break;
                }
                cursor += keyLen;

                string key;
                try
                {
                    key = Encoding.UTF8.GetString(keyBytes);
                }
                catch (DecoderFallbackException)
                {
                    valid = false;
                    break;
                }

                if (!typedPayload)
                {
                    if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(long), currentPageIdx, frame, i64Buffer))
                    {
                        valid = false;
                        break;
                    }
                    cursor += sizeof(long);
                    dict[key] = BitConverter.ToInt64(i64Buffer, 0);
                    continue;
                }

                if (!TryReadTaggedValue(bm, dataFH, currentPageIdx, frame, ref cursor, out var taggedValue, 0))
                {
                    valid = false;
                    break;
                }
                dict[key] = taggedValue;
            }

            if (!valid)
            {
                output.SetNull(dstPos, true);
                continue;
            }

            output.SetAuxValue(dstPos, dict);
            output.SetNull(dstPos, false);
        }
    }

    private static unsafe bool TryReadTaggedValue(
        BufferManager.BufferManager bm,
        FileHandle dataFH,
        uint currentPageIdx,
        byte* currentPageFrame,
        ref ulong cursor,
        out object? value,
        int depth)
    {
        value = null;
        if (depth > MaxTaggedDecodeDepth)
            return false;

        var one = new byte[1];
        if (!TryReadBytesFromFile(bm, dataFH, cursor, 1, currentPageIdx, currentPageFrame, one))
            return false;
        cursor += 1;

        switch (one[0])
        {
            case TaggedNull:
                value = null;
                return true;
            case TaggedInt64:
            {
                var i64 = new byte[sizeof(long)];
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(long), currentPageIdx, currentPageFrame, i64))
                    return false;
                cursor += sizeof(long);
                value = BitConverter.ToInt64(i64, 0);
                return true;
            }
            case TaggedString:
            {
                var lenBuf = new byte[sizeof(uint)];
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, lenBuf))
                    return false;
                cursor += sizeof(uint);
                var len = BitConverter.ToUInt32(lenBuf, 0);
                if (len > 4096)
                    return false;

                var strBytes = new byte[len];
                if (len > 0 && !TryReadBytesFromFile(bm, dataFH, cursor, (int)len, currentPageIdx, currentPageFrame, strBytes))
                    return false;
                cursor += len;
                try
                {
                    value = Encoding.UTF8.GetString(strBytes);
                    return true;
                }
                catch (DecoderFallbackException)
                {
                    return false;
                }
            }
            case TaggedBlob:
            {
                var lenBuf = new byte[sizeof(uint)];
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, lenBuf))
                    return false;
                cursor += sizeof(uint);
                var len = BitConverter.ToUInt32(lenBuf, 0);
                if (len > 1_048_576)
                    return false;

                var blobBytes = new byte[len];
                if (len > 0 && !TryReadBytesFromFile(bm, dataFH, cursor, (int)len, currentPageIdx, currentPageFrame, blobBytes))
                    return false;
                cursor += len;
                value = blobBytes;
                return true;
            }
            case TaggedBool:
            {
                if (!TryReadBytesFromFile(bm, dataFH, cursor, 1, currentPageIdx, currentPageFrame, one))
                    return false;
                cursor += 1;
                value = one[0] != 0;
                return true;
            }
            case TaggedDouble:
            {
                var dbl = new byte[sizeof(double)];
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(double), currentPageIdx, currentPageFrame, dbl))
                    return false;
                cursor += sizeof(double);
                value = BitConverter.ToDouble(dbl, 0);
                return true;
            }
            case TaggedList:
            {
                var lenBuf = new byte[sizeof(uint)];
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, lenBuf))
                    return false;
                cursor += sizeof(uint);
                var count = BitConverter.ToUInt32(lenBuf, 0);
                if (count > 1024)
                    return false;

                var list = new List<object?>((int)count);
                for (var i = 0u; i < count; i++)
                {
                    if (!TryReadTaggedValue(bm, dataFH, currentPageIdx, currentPageFrame, ref cursor, out var nested, depth + 1))
                        return false;
                    list.Add(nested);
                }

                value = list;
                return true;
            }
            case TaggedStruct:
            {
                var lenBuf = new byte[sizeof(uint)];
                if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, lenBuf))
                    return false;
                cursor += sizeof(uint);
                var fieldCount = BitConverter.ToUInt32(lenBuf, 0);
                if (fieldCount > 1024)
                    return false;

                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0u; i < fieldCount; i++)
                {
                    if (!TryReadBytesFromFile(bm, dataFH, cursor, sizeof(uint), currentPageIdx, currentPageFrame, lenBuf))
                        return false;
                    var keyLen = BitConverter.ToUInt32(lenBuf, 0);
                    cursor += sizeof(uint);
                    if (keyLen == 0 || keyLen > 4096)
                        return false;

                    var keyBytes = new byte[keyLen];
                    if (!TryReadBytesFromFile(bm, dataFH, cursor, (int)keyLen, currentPageIdx, currentPageFrame, keyBytes))
                        return false;
                    cursor += keyLen;

                    string key;
                    try
                    {
                        key = Encoding.UTF8.GetString(keyBytes);
                    }
                    catch (DecoderFallbackException)
                    {
                        return false;
                    }

                    if (!TryReadTaggedValue(bm, dataFH, currentPageIdx, currentPageFrame, ref cursor, out var nested, depth + 1))
                        return false;
                    dict[key] = nested;
                }

                value = dict;
                return true;
            }
            default:
                return false;
        }
    }

    private static unsafe bool TryReadBytesFromFile(
        BufferManager.BufferManager bm,
        FileHandle dataFH,
        ulong overflowOffset,
        uint strLen,
        uint currentPageIdx,
        byte* currentPageFrame,
        out byte[] utf8Bytes)
    {
        utf8Bytes = Array.Empty<byte>();
        if (strLen == 0 || strLen > int.MaxValue)
            return false;

        var pageSize = dataFH.GetPageSize();
        if (pageSize == 0 || dataFH.NumPages == 0)
            return false;
        var fileBytes = dataFH.NumPages * pageSize;
        if (overflowOffset >= fileBytes)
            return false;
        var endExclusive = overflowOffset + strLen;
        if (endExclusive < overflowOffset || endExclusive > fileBytes)
            return false;

        utf8Bytes = new byte[strLen];
        var remaining = strLen;
        var writePos = 0;
        var readOffset = overflowOffset;

        while (remaining > 0)
        {
            var pageIdx = (uint)(readOffset / pageSize);
            var inPageOffset = (uint)(readOffset % pageSize);
            if (inPageOffset >= pageSize)
                return false;

            var toRead = (uint)Math.Min((ulong)remaining, pageSize - inPageOffset);
            if (pageIdx == currentPageIdx)
            {
                var src = new ReadOnlySpan<byte>(currentPageFrame + (int)inPageOffset, (int)toRead);
                src.CopyTo(utf8Bytes.AsSpan(writePos, (int)toRead));
            }
            else
            {
                var page = bm.Pin(dataFH, pageIdx, PageReadPolicy.READ_PAGE);
                try
                {
                    var src = new ReadOnlySpan<byte>(page + (int)inPageOffset, (int)toRead);
                    src.CopyTo(utf8Bytes.AsSpan(writePos, (int)toRead));
                }
                finally
                {
                    bm.Unpin(dataFH, pageIdx);
                }
            }

            readOffset += toRead;
            writePos += (int)toRead;
            remaining -= toRead;
        }

        return true;
    }

    private static unsafe bool TryReadBytesFromFile(
        BufferManager.BufferManager bm,
        FileHandle dataFH,
        ulong offset,
        int length,
        uint currentPageIdx,
        byte* currentPageFrame,
        byte[] destination)
    {
        if (length <= 0 || destination.Length < length)
            return false;

        var pageSize = dataFH.GetPageSize();
        if (pageSize == 0 || dataFH.NumPages == 0)
            return false;
        var fileBytes = dataFH.NumPages * pageSize;
        var endExclusive = offset + (ulong)length;
        if (offset >= fileBytes || endExclusive < offset || endExclusive > fileBytes)
            return false;

        var remaining = (uint)length;
        var readOffset = offset;
        var writePos = 0;
        while (remaining > 0)
        {
            var pageIdx = (uint)(readOffset / pageSize);
            var inPageOffset = (uint)(readOffset % pageSize);
            var toRead = (uint)Math.Min((ulong)remaining, pageSize - inPageOffset);
            if (pageIdx == currentPageIdx)
            {
                var src = new ReadOnlySpan<byte>(currentPageFrame + (int)inPageOffset, (int)toRead);
                src.CopyTo(destination.AsSpan(writePos, (int)toRead));
            }
            else
            {
                var page = bm.Pin(dataFH, pageIdx, PageReadPolicy.READ_PAGE);
                try
                {
                    var src = new ReadOnlySpan<byte>(page + (int)inPageOffset, (int)toRead);
                    src.CopyTo(destination.AsSpan(writePos, (int)toRead));
                }
                finally
                {
                    bm.Unpin(dataFH, pageIdx);
                }
            }

            readOffset += toRead;
            writePos += (int)toRead;
            remaining -= toRead;
        }

        return true;
    }
}
