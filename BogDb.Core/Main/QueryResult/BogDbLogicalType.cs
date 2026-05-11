using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Main.QueryResult;

/// <summary>
/// Lightweight host-facing descriptor for a BogDb logical type.
/// </summary>
public sealed record BogDbLogicalType(
    LogicalTypeID Id,
    BogDbLogicalType? ElementType = null,
    BogDbLogicalType? KeyType = null,
    BogDbLogicalType? ValueType = null,
    IReadOnlyList<BogDbTypeFieldDescriptor>? Fields = null)
{
    public string Name => Id.ToString();

    public bool IsScalar => Id is
        LogicalTypeID.BOOL or
        LogicalTypeID.INT64 or
        LogicalTypeID.INT32 or
        LogicalTypeID.INT16 or
        LogicalTypeID.INT8 or
        LogicalTypeID.UINT64 or
        LogicalTypeID.UINT32 or
        LogicalTypeID.UINT16 or
        LogicalTypeID.UINT8 or
        LogicalTypeID.INT128 or
        LogicalTypeID.UINT128 or
        LogicalTypeID.DOUBLE or
        LogicalTypeID.FLOAT or
        LogicalTypeID.DECIMAL or
        LogicalTypeID.STRING or
        LogicalTypeID.BLOB or
        LogicalTypeID.DATE or
        LogicalTypeID.TIMESTAMP or
        LogicalTypeID.TIMESTAMP_SEC or
        LogicalTypeID.TIMESTAMP_MS or
        LogicalTypeID.TIMESTAMP_NS or
        LogicalTypeID.TIMESTAMP_TZ or
        LogicalTypeID.INTERVAL or
        LogicalTypeID.UUID or
        LogicalTypeID.INTERNAL_ID;

    public bool IsNested => Id is LogicalTypeID.LIST or LogicalTypeID.ARRAY or LogicalTypeID.STRUCT or LogicalTypeID.MAP or LogicalTypeID.UNION;

    public bool IsTemporal => Id is
        LogicalTypeID.DATE or
        LogicalTypeID.TIMESTAMP or
        LogicalTypeID.TIMESTAMP_SEC or
        LogicalTypeID.TIMESTAMP_MS or
        LogicalTypeID.TIMESTAMP_NS or
        LogicalTypeID.TIMESTAMP_TZ or
        LogicalTypeID.INTERVAL;

    public bool IsNumeric => Id is
        LogicalTypeID.INT64 or
        LogicalTypeID.INT32 or
        LogicalTypeID.INT16 or
        LogicalTypeID.INT8 or
        LogicalTypeID.UINT64 or
        LogicalTypeID.UINT32 or
        LogicalTypeID.UINT16 or
        LogicalTypeID.UINT8 or
        LogicalTypeID.INT128 or
        LogicalTypeID.UINT128 or
        LogicalTypeID.DOUBLE or
        LogicalTypeID.FLOAT or
        LogicalTypeID.DECIMAL;

    public bool IsIntegral => Id is
        LogicalTypeID.INT64 or
        LogicalTypeID.INT32 or
        LogicalTypeID.INT16 or
        LogicalTypeID.INT8 or
        LogicalTypeID.UINT64 or
        LogicalTypeID.UINT32 or
        LogicalTypeID.UINT16 or
        LogicalTypeID.UINT8 or
        LogicalTypeID.INT128 or
        LogicalTypeID.UINT128;

    public bool IsAny => Id == LogicalTypeID.ANY;

    public static BogDbLogicalType FromId(LogicalTypeID id) => new(id);

    public static BogDbLogicalType FromValue(object? value)
    {
        value = Common.TypeCoercionHelper.Normalize(value);
        var id = QueryResult.InferLogicalTypeForMetadata(value);

        return id switch
        {
            LogicalTypeID.LIST or LogicalTypeID.ARRAY => new(
                id,
                ElementType: InferSequenceElementType(value)),
            LogicalTypeID.STRUCT => new(
                id,
                Fields: InferStructFields(value)),
            LogicalTypeID.MAP => InferMapType(value),
            _ => new(id)
        };
    }

    private static BogDbLogicalType InferSequenceElementType(object? value)
    {
        if (value is not System.Collections.IEnumerable sequence || value is string || value is byte[])
            return FromId(LogicalTypeID.ANY);

        BogDbLogicalType? elementType = null;
        foreach (var item in sequence)
            elementType = Merge(elementType, FromValue(item));

        return elementType ?? FromId(LogicalTypeID.ANY);
    }

    private static IReadOnlyList<BogDbTypeFieldDescriptor> InferStructFields(object? value)
    {
        var fields = new List<BogDbTypeFieldDescriptor>();

        if (value is IEnumerable<KeyValuePair<string, object?>> stringPairs)
        {
            foreach (var pair in stringPairs)
                fields.Add(new BogDbTypeFieldDescriptor(pair.Key, FromValue(pair.Value)));
            return fields;
        }

        if (value is System.Collections.IDictionary dictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                var key = Common.TypeCoercionHelper.ToBogDbString(entry.Key) ?? string.Empty;
                fields.Add(new BogDbTypeFieldDescriptor(key, FromValue(entry.Value)));
            }
        }

        return fields;
    }

    private static BogDbLogicalType InferMapType(object? value)
    {
        BogDbLogicalType? keyType = null;
        BogDbLogicalType? valueType = null;

        if (value is IEnumerable<KeyValuePair<object?, object?>> objectPairs)
        {
            foreach (var pair in objectPairs)
            {
                keyType = Merge(keyType, FromValue(pair.Key));
                valueType = Merge(valueType, FromValue(pair.Value));
            }
        }
        else if (value is System.Collections.IDictionary dictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                keyType = Merge(keyType, FromValue(entry.Key));
                valueType = Merge(valueType, FromValue(entry.Value));
            }
        }

        return new(
            LogicalTypeID.MAP,
            KeyType: keyType ?? FromId(LogicalTypeID.ANY),
            ValueType: valueType ?? FromId(LogicalTypeID.ANY));
    }

    private static BogDbLogicalType Merge(BogDbLogicalType? current, BogDbLogicalType next)
    {
        if (current == null)
            return next;

        return current.Id == next.Id
            ? current
            : FromId(LogicalTypeID.ANY);
    }

    public override string ToString() => Name;
}
