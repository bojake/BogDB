using System.Collections;
using BogDb.Core.Common;

namespace BogDb.Core.Main.QueryResult;

/// <summary>
/// Lightweight type-aware wrapper around a query result value for embedding scenarios.
/// </summary>
public readonly struct BogValue
{
    private readonly object? _rawValue;
    private readonly object? _normalizedValue;

    private BogValue(object? rawValue)
    {
        _rawValue = rawValue;
        _normalizedValue = TypeCoercionHelper.Normalize(rawValue);
        LogicalType = InferLogicalType(_normalizedValue);
    }

    public object? RawValue => _rawValue;
    public object? Value => _normalizedValue;
    public LogicalTypeID LogicalType { get; }
    public BogDbLogicalType Type => BogDbLogicalType.FromValue(_normalizedValue);
    public bool IsNull => _normalizedValue is null or DBNull;

    public static BogValue FromObject(object? value) => new(value);

    public long GetInt64() => TypeCoercionHelper.ToInt64(_normalizedValue);

    public double GetDouble() => TypeCoercionHelper.ToDouble(_normalizedValue);

    public bool GetBoolean() => TypeCoercionHelper.ToBool(_normalizedValue);

    public string? GetString() => TypeCoercionHelper.ToBogDbString(_normalizedValue);

    public IReadOnlyList<BogValue> AsList()
    {
        if (_normalizedValue is null or DBNull)
            return Array.Empty<BogValue>();

        if (_normalizedValue is string || _normalizedValue is byte[])
            throw new InvalidCastException($"Cannot convert value of type '{LogicalType}' to a list.");

        if (_normalizedValue is not IEnumerable sequence)
            throw new InvalidCastException($"Cannot convert value of type '{LogicalType}' to a list.");

        var values = new List<BogValue>();
        foreach (var item in sequence)
            values.Add(FromObject(item));
        return values;
    }

    public IReadOnlyDictionary<string, BogValue> AsDictionary()
    {
        if (_normalizedValue is null or DBNull)
            return new Dictionary<string, BogValue>(StringComparer.Ordinal);

        if (TryGetDictionaryEntries(_normalizedValue, out var entries))
            return entries;

        throw new InvalidCastException($"Cannot convert value of type '{LogicalType}' to a dictionary.");
    }

    public T? As<T>()
    {
        if (IsNull)
            return default;

        var targetType = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (_normalizedValue is T direct)
            return direct;

        object? converted;
        if (underlyingType == typeof(string))
            converted = GetString();
        else if (underlyingType == typeof(long))
            converted = GetInt64();
        else if (underlyingType == typeof(int))
            converted = checked((int)GetInt64());
        else if (underlyingType == typeof(short))
            converted = checked((short)GetInt64());
        else if (underlyingType == typeof(sbyte))
            converted = checked((sbyte)GetInt64());
        else if (underlyingType == typeof(ulong))
            converted = checked((ulong)GetInt64());
        else if (underlyingType == typeof(uint))
            converted = checked((uint)GetInt64());
        else if (underlyingType == typeof(ushort))
            converted = checked((ushort)GetInt64());
        else if (underlyingType == typeof(byte))
            converted = checked((byte)GetInt64());
        else if (underlyingType == typeof(double))
            converted = GetDouble();
        else if (underlyingType == typeof(float))
            converted = (float)GetDouble();
        else if (underlyingType == typeof(bool))
            converted = GetBoolean();
        else if (underlyingType == typeof(BogDbInterval) &&
            TypeCoercionHelper.TryParseInterval(_normalizedValue, out var interval))
            converted = interval;
        else if (_normalizedValue is IConvertible && typeof(IConvertible).IsAssignableFrom(underlyingType))
            converted = Convert.ChangeType(_normalizedValue, underlyingType, System.Globalization.CultureInfo.InvariantCulture);
        else if (underlyingType.IsInstanceOfType(_normalizedValue))
            converted = _normalizedValue;
        else
            throw new InvalidCastException(
                $"Cannot convert value of type '{_normalizedValue?.GetType().Name ?? "null"}' to '{underlyingType.Name}'.");

        return (T?)converted;
    }

    public override string ToString() => GetString() ?? string.Empty;

    private static bool TryGetDictionaryEntries(object value, out Dictionary<string, BogValue> entries)
    {
        if (value is IEnumerable<KeyValuePair<string, object?>> stringPairs)
        {
            entries = new Dictionary<string, BogValue>(StringComparer.Ordinal);
            foreach (var pair in stringPairs)
                entries[pair.Key] = FromObject(pair.Value);
            return true;
        }

        if (value is IEnumerable<KeyValuePair<object?, object?>> objectPairs)
        {
            entries = new Dictionary<string, BogValue>(StringComparer.Ordinal);
            foreach (var pair in objectPairs)
                entries[TypeCoercionHelper.ToBogDbString(pair.Key) ?? string.Empty] = FromObject(pair.Value);
            return true;
        }

        if (value is IDictionary dictionary)
        {
            entries = new Dictionary<string, BogValue>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
                entries[TypeCoercionHelper.ToBogDbString(entry.Key) ?? string.Empty] = FromObject(entry.Value);
            return true;
        }

        entries = null!;
        return false;
    }

    private static LogicalTypeID InferLogicalType(object? value)
    {
        if (value is null or DBNull)
            return LogicalTypeID.ANY;

        return value switch
        {
            bool => LogicalTypeID.BOOL,
            long => LogicalTypeID.INT64,
            int => LogicalTypeID.INT32,
            short => LogicalTypeID.INT16,
            sbyte => LogicalTypeID.INT8,
            ulong => LogicalTypeID.UINT64,
            uint => LogicalTypeID.UINT32,
            ushort => LogicalTypeID.UINT16,
            byte => LogicalTypeID.UINT8,
            double => LogicalTypeID.DOUBLE,
            float => LogicalTypeID.FLOAT,
            string => LogicalTypeID.STRING,
            DateTimeOffset => LogicalTypeID.TIMESTAMP_TZ,
            DateTime => LogicalTypeID.TIMESTAMP,
            BogDbInterval => LogicalTypeID.INTERVAL,
            InternalID => LogicalTypeID.INTERNAL_ID,
            IDictionary => LogicalTypeID.STRUCT,
            IEnumerable<KeyValuePair<string, object?>> => LogicalTypeID.STRUCT,
            IEnumerable<KeyValuePair<object?, object?>> => LogicalTypeID.MAP,
            IEnumerable sequence when sequence is not string && sequence is not byte[] => LogicalTypeID.LIST,
            _ => LogicalTypeID.ANY
        };
    }
}
