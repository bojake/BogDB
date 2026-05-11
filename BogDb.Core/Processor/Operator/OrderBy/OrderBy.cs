using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BogDb.Core.Binder;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.OrderBy;

/// <summary>
/// Physical ORDER BY operator.
///
/// Small result sets stay on the existing in-memory sort path. Larger result sets
/// are split into sorted runs, spilled to temp files, and merged back with a
/// k-way priority queue so the operator no longer needs to retain every row in RAM.
/// </summary>
public sealed class OrderBy : PhysicalOperator
{
    private const int DefaultChunkRowLimit = 2048;
    private const int MaxBufferedChunkCapacity = 4096;
    private const long MinChunkByteLimit = 4 * 1024;
    private const long MaxChunkByteLimit = 8 * 1024 * 1024;

    private readonly PhysicalOperator _child;
    private readonly IReadOnlyList<Expression> _orderByExpressions;
    private readonly IReadOnlyList<bool> _isAscending;

    private bool _isInitialized;
    private int _currentIndex;
    private List<SortRecord>? _sortedItems;
    private List<SpilledRunReader>? _runReaders;
    private PriorityQueue<MergeEntry, MergePriority>? _mergeQueue;
    private long _mergeSequence;

    internal static int ChunkRowLimit { get; set; } = DefaultChunkRowLimit;
    internal static long? ChunkByteLimitOverride { get; set; }

    public OrderBy(
        PhysicalOperator child,
        IReadOnlyList<Expression> orderByExpressions,
        IReadOnlyList<bool> isAscending,
        uint id)
        : base(PhysicalOperatorType.ORDER_BY, id)
    {
        _child = child;
        _orderByExpressions = orderByExpressions;
        _isAscending = isAscending;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!_isInitialized)
        {
            Initialize(context);
        }

        if (_sortedItems != null)
        {
            if (_currentIndex >= _sortedItems.Count)
            {
                return false;
            }

            context.RestoreState(_sortedItems[_currentIndex++].State);
            return true;
        }

        if (_mergeQueue == null)
        {
            return false;
        }

        while (_mergeQueue.Count > 0)
        {
            var entry = _mergeQueue.Dequeue();
            var reader = _runReaders![entry.RunIndex];
            var row = entry.Record;

            context.RestoreState(row.State);

            if (reader.TryReadNext(out var next))
            {
                _mergeQueue.Enqueue(
                    new MergeEntry(entry.RunIndex, next),
                    new MergePriority(next.Keys, _mergeSequence++));
            }
            else
            {
                reader.Dispose();
            }

            return true;
        }

        return false;
    }

    private void Initialize(ExecutionContext context)
    {
        _isInitialized = true;
        _currentIndex = 0;

        var comparer = new OrderByComparer(_isAscending);
        var chunkBytes = 0L;
        var chunkByteLimit = ResolveChunkByteLimit(context);
        var observedRecordCount = 0L;
        var observedRecordBytes = 0L;
        var chunk = new List<SortRecord>(ResolveChunkCapacity(chunkByteLimit, averageRecordBytes: 0));
        var spilledRunPaths = new List<string>();

        while (_child.GetNextTuple(context))
        {
            var record = BuildRecord(context);
            var recordBytes = SortStateSerializer.EstimateRecordSize(record);
            observedRecordCount++;
            observedRecordBytes += recordBytes;

            // Spill the current run before appending when the next record would
            // push an already-populated chunk over budget. Oversized single
            // records still spill immediately after being added to their own run.
            if (ShouldSpillBeforeAppend(chunk.Count, chunkBytes, recordBytes, chunkByteLimit))
            {
                SortAndSpillChunk(chunk, comparer, spilledRunPaths, context.QueryMetrics);
                chunk = new List<SortRecord>(ResolveChunkCapacity(
                    chunkByteLimit,
                    ResolveAverageRecordBytes(observedRecordBytes, observedRecordCount)));
                chunkBytes = 0;
            }

            chunk.Add(record);
            chunkBytes += recordBytes;

            if (ShouldSpillChunk(chunk.Count, chunkBytes, chunkByteLimit))
            {
                SortAndSpillChunk(chunk, comparer, spilledRunPaths, context.QueryMetrics);
                chunk = new List<SortRecord>(ResolveChunkCapacity(
                    chunkByteLimit,
                    ResolveAverageRecordBytes(observedRecordBytes, observedRecordCount)));
                chunkBytes = 0;
            }
        }

        if (spilledRunPaths.Count == 0)
        {
            chunk.Sort(comparer);
            _sortedItems = chunk;
            return;
        }

        if (chunk.Count > 0)
        {
            SortAndSpillChunk(chunk, comparer, spilledRunPaths, context.QueryMetrics);
        }

        InitializeMerge(spilledRunPaths, context.QueryMetrics);
    }

    private static long ResolveChunkByteLimit(ExecutionContext context)
    {
        if (ChunkByteLimitOverride.HasValue && ChunkByteLimitOverride.Value > 0)
        {
            return ChunkByteLimitOverride.Value;
        }

        var memoryLimit = context.BufferManager?.MemoryLimit ?? MinChunkByteLimit;
        return Math.Clamp(memoryLimit / 8, MinChunkByteLimit, MaxChunkByteLimit);
    }

    private static bool ShouldSpillChunk(int rowCount, long chunkBytes, long chunkByteLimit)
        => rowCount >= ChunkRowLimit || chunkBytes >= chunkByteLimit;

    private static bool ShouldSpillBeforeAppend(
        int currentRowCount,
        long currentChunkBytes,
        long nextRecordBytes,
        long chunkByteLimit)
        => currentRowCount > 0 && ShouldSpillChunk(currentRowCount + 1, currentChunkBytes + nextRecordBytes, chunkByteLimit);

    private static int ResolveChunkCapacity(long chunkByteLimit, long averageRecordBytes)
    {
        var budgetDrivenCapacity = averageRecordBytes > 0
            ? Math.Max(1L, chunkByteLimit / averageRecordBytes)
            : ChunkRowLimit;

        var targetCapacity = Math.Min((long)ChunkRowLimit, budgetDrivenCapacity);
        return Math.Clamp((int)Math.Min(targetCapacity, MaxBufferedChunkCapacity), 1, MaxBufferedChunkCapacity);
    }

    private static long ResolveAverageRecordBytes(long observedRecordBytes, long observedRecordCount)
        => observedRecordCount > 0 ? Math.Max(1L, observedRecordBytes / observedRecordCount) : 0L;

    private SortRecord BuildRecord(ExecutionContext context)
    {
        var keys = new object?[_orderByExpressions.Count];
        for (var i = 0; i < _orderByExpressions.Count; i++)
        {
            keys[i] = TypeCoercionHelper.Normalize(
                ExpressionExecutionHelper.Evaluate(_orderByExpressions[i], context));
        }

        return new SortRecord(keys, context.CaptureState());
    }

    private static void SortAndSpillChunk(
        List<SortRecord> chunk,
        OrderByComparer comparer,
        List<string> spilledRunPaths,
        Common.Diagnostics.QueryMetricsContext? queryMetrics)
    {
        chunk.Sort(comparer);

        var path = Path.GetTempFileName();
        spilledRunPaths.Add(path);

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 8192, FileOptions.SequentialScan);
        using var writer = new BinaryWriter(file);

        writer.Write(chunk.Count);
        foreach (var record in chunk)
        {
            SortStateSerializer.WriteRecord(writer, record);
        }
        writer.Flush();

        queryMetrics?.IncrementSpillCount();
        queryMetrics?.AddSpillRuns(1);
        queryMetrics?.AddTempBytesWritten(file.Position);
    }

    private void InitializeMerge(List<string> spilledRunPaths, Common.Diagnostics.QueryMetricsContext? queryMetrics)
    {
        _runReaders = new List<SpilledRunReader>(spilledRunPaths.Count);
        _mergeQueue = new PriorityQueue<MergeEntry, MergePriority>(new MergePriorityComparer(_isAscending));
        _mergeSequence = 0;
        queryMetrics?.ObserveMergeFanIn(spilledRunPaths.Count);

        for (var i = 0; i < spilledRunPaths.Count; i++)
        {
            var reader = new SpilledRunReader(spilledRunPaths[i]);
            _runReaders.Add(reader);

            if (reader.TryReadNext(out var first))
            {
                _mergeQueue.Enqueue(
                    new MergeEntry(i, first),
                    new MergePriority(first.Keys, _mergeSequence++));
            }
            else
            {
                reader.Dispose();
            }
        }
    }

    internal static void ResetChunkingForTests()
    {
        ChunkRowLimit = DefaultChunkRowLimit;
        ChunkByteLimitOverride = null;
    }

    internal static long EstimateSerializedStringSizeForTests(string value)
        => EstimateSerializedStringSize(value);

    internal static long EstimateSerializedRecordSizeForTests(object?[] keys, ExecutionState state)
        => SortStateSerializer.EstimateRecordSize(new SortRecord(keys, state));

    internal static byte[] SerializeRecordForTests(object?[] keys, ExecutionState state)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        SortStateSerializer.WriteRecord(writer, new SortRecord(keys, state));
        writer.Flush();
        return stream.ToArray();
    }

    internal static bool ShouldSpillBeforeAppendForTests(
        int currentRowCount,
        long currentChunkBytes,
        long nextRecordBytes,
        long chunkByteLimit)
        => ShouldSpillBeforeAppend(currentRowCount, currentChunkBytes, nextRecordBytes, chunkByteLimit);

    internal static int ResolveChunkCapacityForTests(long chunkByteLimit, long averageRecordBytes)
        => ResolveChunkCapacity(chunkByteLimit, averageRecordBytes);

    internal static int CompareKeysForTests(
        IReadOnlyList<bool> isAscending,
        object?[] left,
        object?[] right)
        => new SortKeyComparer(isAscending).Compare(left, right);

    private static long EstimateSerializedStringSize(string value)
    {
        var utf8ByteCount = Encoding.UTF8.GetByteCount(value);
        return Get7BitEncodedIntSize(utf8ByteCount) + utf8ByteCount;
    }

    private static int Get7BitEncodedIntSize(int value)
    {
        var size = 1;
        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            size++;
            remaining >>= 7;
        }

        return size;
    }

    private readonly record struct SortRecord(object?[] Keys, ExecutionState State);
    private readonly record struct MergeEntry(int RunIndex, SortRecord Record);
    private readonly record struct MergePriority(object?[] Keys, long Sequence);

    private sealed class OrderByComparer : IComparer<SortRecord>
    {
        private readonly SortKeyComparer _keyComparer;

        public OrderByComparer(IReadOnlyList<bool> isAscending)
        {
            _keyComparer = new SortKeyComparer(isAscending);
        }

        public int Compare(SortRecord x, SortRecord y)
            => _keyComparer.Compare(x.Keys, y.Keys);
    }

    private sealed class MergePriorityComparer : IComparer<MergePriority>
    {
        private readonly SortKeyComparer _keyComparer;

        public MergePriorityComparer(IReadOnlyList<bool> isAscending)
        {
            _keyComparer = new SortKeyComparer(isAscending);
        }

        public int Compare(MergePriority x, MergePriority y)
            => _keyComparer.CompareWithSequence(x.Keys, y.Keys, x.Sequence, y.Sequence);
    }

    private sealed class SpilledRunReader : IDisposable
    {
        private readonly string _path;
        private readonly FileStream _stream;
        private readonly BinaryReader _reader;
        private readonly int _recordCount;
        private int _recordsRead;
        private bool _disposed;

        public SpilledRunReader(string path)
        {
            _path = path;
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
            _reader = new BinaryReader(_stream);
            _recordCount = _reader.ReadInt32();
        }

        public bool TryReadNext(out SortRecord record)
        {
            if (_recordsRead >= _recordCount)
            {
                record = default;
                return false;
            }

            record = SortStateSerializer.ReadRecord(_reader);
            _recordsRead++;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _reader.Dispose();
            _stream.Dispose();
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
        }
    }

    private static class SortStateSerializer
    {
        private enum ValueTag : byte
        {
            Null = 0,
            Bool = 1,
            Int64 = 2,
            Int32 = 3,
            Int16 = 4,
            Int8 = 5,
            UInt64 = 6,
            UInt32 = 7,
            UInt16 = 8,
            UInt8 = 9,
            Double = 10,
            Float = 11,
            Decimal = 12,
            String = 13,
            Blob = 14,
            Guid = 15,
            DateOnly = 16,
            DateTime = 17,
            DateTimeOffset = 18,
            Interval = 19,
            List = 20,
            Dictionary = 21,
            Int128 = 22,
            UInt128 = 23,
        }

        public static void WriteRecord(BinaryWriter writer, SortRecord record)
        {
            writer.Write(record.Keys.Length);
            foreach (var key in record.Keys)
            {
                WriteValue(writer, key);
            }

            WriteExecutionState(writer, record.State);
        }

        public static SortRecord ReadRecord(BinaryReader reader)
        {
            var keyCount = reader.ReadInt32();
            var keys = new object?[keyCount];
            for (var i = 0; i < keyCount; i++)
            {
                keys[i] = ReadValue(reader);
            }

            return new SortRecord(keys, ReadExecutionState(reader));
        }

        public static long EstimateRecordSize(SortRecord record)
        {
            using var stream = new CountingStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteRecord(writer, record);
            writer.Flush();
            return stream.BytesWritten;
        }

        private static void WriteExecutionState(BinaryWriter writer, ExecutionState state)
        {
            WriteValue(writer, state.CurrentNodeId);
            WriteStringObjectDictionary(writer, state.ParameterBindings);
            WriteStringObjectDictionary(writer, state.CurrentNodeProperties);
            WriteStringObjectDictionary(writer, state.CurrentScalarBindings);
            WriteArray(writer, state.CurrentProjectionRow);
            WriteStringObjectDictionary(writer, state.CurrentVariableIds);
            WriteNestedDictionary(writer, state.CurrentVariableProperties);
            WriteStringObjectDictionary(writer, state.AggregateValues);
            WriteSemiMasks(writer, state.SemiMasks);
        }

        private static ExecutionState ReadExecutionState(BinaryReader reader)
        {
            return new ExecutionState
            {
                CurrentNodeId = ReadValue(reader),
                ParameterBindings = ReadStringObjectDictionary(reader),
                CurrentNodeProperties = ReadStringObjectDictionary(reader, allowNullValues: false),
                CurrentScalarBindings = ReadStringObjectDictionary(reader),
                CurrentProjectionRow = ReadArray(reader),
                CurrentVariableIds = ReadStringObjectDictionary(reader, allowNullValues: false),
                CurrentVariableProperties = ReadNestedDictionary(reader),
                AggregateValues = ReadStringObjectDictionary(reader),
                SemiMasks = ReadSemiMasks(reader)
            };
        }

        private static void WriteSemiMasks(BinaryWriter writer, Dictionary<ulong, HashSet<object>>? semiMasks)
        {
            writer.Write(semiMasks != null);
            if (semiMasks == null)
            {
                return;
            }

            writer.Write(semiMasks.Count);
            foreach (var (key, values) in semiMasks)
            {
                writer.Write(key);
                writer.Write(values.Count);
                foreach (var value in values)
                {
                    WriteValue(writer, value);
                }
            }
        }

        private static Dictionary<ulong, HashSet<object>>? ReadSemiMasks(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            var count = reader.ReadInt32();
            var result = new Dictionary<ulong, HashSet<object>>(count);
            for (var i = 0; i < count; i++)
            {
                var key = reader.ReadUInt64();
                var valueCount = reader.ReadInt32();
                var set = new HashSet<object>();
                for (var j = 0; j < valueCount; j++)
                {
                    set.Add(ReadValue(reader)!);
                }
                result[key] = set;
            }
            return result;
        }

        private static void WriteNestedDictionary(
            BinaryWriter writer,
            Dictionary<string, Dictionary<string, object>>? value)
        {
            writer.Write(value != null);
            if (value == null)
            {
                return;
            }

            writer.Write(value.Count);
            foreach (var (key, inner) in value)
            {
                writer.Write(key);
                WriteStringObjectDictionary(writer, inner);
            }
        }

        private static Dictionary<string, Dictionary<string, object>>? ReadNestedDictionary(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            var count = reader.ReadInt32();
            var result = new Dictionary<string, Dictionary<string, object>>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = reader.ReadString();
                result[key] = ReadStringObjectDictionary(reader, allowNullValues: false) ?? new Dictionary<string, object>(StringComparer.Ordinal);
            }
            return result;
        }

        private static void WriteArray(BinaryWriter writer, object[]? array)
        {
            writer.Write(array != null);
            if (array == null)
            {
                return;
            }

            writer.Write(array.Length);
            foreach (var value in array)
            {
                WriteValue(writer, value);
            }
        }

        private static object[]? ReadArray(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            var count = reader.ReadInt32();
            var result = new object[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = ReadValue(reader)!;
            }
            return result;
        }

        private static void WriteStringObjectDictionary(
            BinaryWriter writer,
            IEnumerable<KeyValuePair<string, object?>>? dictionary)
        {
            writer.Write(dictionary != null);
            if (dictionary == null)
            {
                return;
            }

            var items = new List<KeyValuePair<string, object?>>();
            foreach (var pair in dictionary)
            {
                items.Add(pair);
            }

            writer.Write(items.Count);
            foreach (var (key, value) in items)
            {
                writer.Write(key);
                WriteValue(writer, value);
            }
        }

        private static Dictionary<string, object>? ReadStringObjectDictionary(
            BinaryReader reader,
            bool allowNullValues = true)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            var count = reader.ReadInt32();
            var result = new Dictionary<string, object>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = reader.ReadString();
                var value = ReadValue(reader);
                result[key] = value ?? (allowNullValues ? null! : string.Empty);
            }
            return result;
        }

        private static void WriteValue(BinaryWriter writer, object? value)
        {
            value = NormalizeFallbackValue(value);

            switch (value)
            {
                case null:
                    writer.Write((byte)ValueTag.Null);
                    break;
                case bool b:
                    writer.Write((byte)ValueTag.Bool);
                    writer.Write(b);
                    break;
                case long l:
                    writer.Write((byte)ValueTag.Int64);
                    writer.Write(l);
                    break;
                case int i:
                    writer.Write((byte)ValueTag.Int32);
                    writer.Write(i);
                    break;
                case short s:
                    writer.Write((byte)ValueTag.Int16);
                    writer.Write(s);
                    break;
                case sbyte sb:
                    writer.Write((byte)ValueTag.Int8);
                    writer.Write(sb);
                    break;
                case ulong ul:
                    writer.Write((byte)ValueTag.UInt64);
                    writer.Write(ul);
                    break;
                case uint ui:
                    writer.Write((byte)ValueTag.UInt32);
                    writer.Write(ui);
                    break;
                case ushort us:
                    writer.Write((byte)ValueTag.UInt16);
                    writer.Write(us);
                    break;
                case byte ub:
                    writer.Write((byte)ValueTag.UInt8);
                    writer.Write(ub);
                    break;
                case double d:
                    writer.Write((byte)ValueTag.Double);
                    writer.Write(d);
                    break;
                case float f:
                    writer.Write((byte)ValueTag.Float);
                    writer.Write(f);
                    break;
                case decimal dec:
                    writer.Write((byte)ValueTag.Decimal);
                    writer.Write(dec);
                    break;
                case string str:
                    writer.Write((byte)ValueTag.String);
                    writer.Write(str);
                    break;
                case byte[] bytes:
                    writer.Write((byte)ValueTag.Blob);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    break;
                case Guid guid:
                    writer.Write((byte)ValueTag.Guid);
                    writer.Write(guid.ToByteArray());
                    break;
                case DateOnly date:
                    writer.Write((byte)ValueTag.DateOnly);
                    writer.Write(date.DayNumber);
                    break;
                case DateTime dateTime:
                    writer.Write((byte)ValueTag.DateTime);
                    writer.Write(dateTime.ToBinary());
                    break;
                case DateTimeOffset dateTimeOffset:
                    writer.Write((byte)ValueTag.DateTimeOffset);
                    writer.Write(dateTimeOffset.Ticks);
                    writer.Write(dateTimeOffset.Offset.Ticks);
                    break;
                case BogDbInterval interval:
                    writer.Write((byte)ValueTag.Interval);
                    writer.Write(interval.Months);
                    writer.Write(interval.Days);
                    writer.Write(interval.Microseconds);
                    break;
                case Int128 int128:
                    writer.Write((byte)ValueTag.Int128);
                    writer.Write((long)(int128 >> 64));
                    writer.Write((ulong)int128);
                    break;
                case UInt128 uint128:
                    writer.Write((byte)ValueTag.UInt128);
                    writer.Write((ulong)(uint128 >> 64));
                    writer.Write((ulong)uint128);
                    break;
                case IDictionary<string, object?> dict:
                    writer.Write((byte)ValueTag.Dictionary);
                    writer.Write(dict.Count);
                    foreach (var (key, itemValue) in dict)
                    {
                        writer.Write(key);
                        WriteValue(writer, itemValue);
                    }
                    break;
                case IDictionary legacyDict:
                    writer.Write((byte)ValueTag.Dictionary);
                    writer.Write(legacyDict.Count);
                    foreach (DictionaryEntry entry in legacyDict)
                    {
                        writer.Write(entry.Key?.ToString() ?? string.Empty);
                        WriteValue(writer, entry.Value);
                    }
                    break;
                case IEnumerable enumerable when value is not string:
                {
                    var items = new List<object?>();
                    foreach (var item in enumerable)
                    {
                        items.Add(item);
                    }

                    writer.Write((byte)ValueTag.List);
                    writer.Write(items.Count);
                    foreach (var item in items)
                    {
                        WriteValue(writer, item);
                    }
                    break;
                }
                default:
                    writer.Write((byte)ValueTag.String);
                    writer.Write(value.ToString() ?? string.Empty);
                    break;
            }
        }

        private static object? ReadValue(BinaryReader reader)
        {
            return (ValueTag)reader.ReadByte() switch
            {
                ValueTag.Null => null,
                ValueTag.Bool => reader.ReadBoolean(),
                ValueTag.Int64 => reader.ReadInt64(),
                ValueTag.Int32 => reader.ReadInt32(),
                ValueTag.Int16 => reader.ReadInt16(),
                ValueTag.Int8 => reader.ReadSByte(),
                ValueTag.UInt64 => reader.ReadUInt64(),
                ValueTag.UInt32 => reader.ReadUInt32(),
                ValueTag.UInt16 => reader.ReadUInt16(),
                ValueTag.UInt8 => reader.ReadByte(),
                ValueTag.Double => reader.ReadDouble(),
                ValueTag.Float => reader.ReadSingle(),
                ValueTag.Decimal => reader.ReadDecimal(),
                ValueTag.String => reader.ReadString(),
                ValueTag.Blob => reader.ReadBytes(reader.ReadInt32()),
                ValueTag.Guid => new Guid(reader.ReadBytes(16)),
                ValueTag.DateOnly => DateOnly.FromDayNumber(reader.ReadInt32()),
                ValueTag.DateTime => DateTime.FromBinary(reader.ReadInt64()),
                ValueTag.DateTimeOffset => new DateTimeOffset(reader.ReadInt64(), new TimeSpan(reader.ReadInt64())),
                ValueTag.Interval => new BogDbInterval(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64()),
                ValueTag.List => ReadList(reader),
                ValueTag.Dictionary => ReadDictionary(reader),
                ValueTag.Int128 => ((Int128)reader.ReadInt64() << 64) | reader.ReadUInt64(),
                ValueTag.UInt128 => ((UInt128)reader.ReadUInt64() << 64) | reader.ReadUInt64(),
                _ => throw new InvalidDataException("Unknown ORDER BY spill value tag.")
            };
        }

        private static List<object?> ReadList(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var result = new List<object?>(count);
            for (var i = 0; i < count; i++)
            {
                result.Add(ReadValue(reader));
            }
            return result;
        }

        private static Dictionary<string, object?> ReadDictionary(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var result = new Dictionary<string, object?>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = reader.ReadString();
                result[key] = ReadValue(reader);
            }
            return result;
        }

        private static long EstimateStringSize(string value)
            => EstimateSerializedStringSize(value);

        private static object? NormalizeFallbackValue(object? value)
        {
            if (value == null)
            {
                return null;
            }

            var normalized = TypeCoercionHelper.Normalize(value);
            if (!ReferenceEquals(normalized, value))
            {
                return normalized;
            }

            return value;
        }

        private sealed class CountingStream : Stream
        {
            public long BytesWritten { get; private set; }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => BytesWritten;
            public override long Position
            {
                get => BytesWritten;
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
                => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin)
                => throw new NotSupportedException();

            public override void SetLength(long value)
                => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
                => BytesWritten += count;

            public override void Write(ReadOnlySpan<byte> buffer)
                => BytesWritten += buffer.Length;

            public override void WriteByte(byte value)
                => BytesWritten++;
        }
    }
}
