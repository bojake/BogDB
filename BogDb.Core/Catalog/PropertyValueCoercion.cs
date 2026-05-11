using System;
using System.Collections;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Catalog;

internal static class PropertyValueCoercion
{
    public static Dictionary<string, object> CoerceProperties(
        TableCatalogEntry? tableEntry,
        IDictionary<string, object> properties)
    {
        var coerced = new Dictionary<string, object>(properties.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in properties)
        {
            if (tableEntry != null && tableEntry.ContainsProperty(key))
            {
                var property = tableEntry.GetProperty(key);
                var coercedValue = CoercePropertyValue(property, value);
                if (coercedValue is not null)
                    coerced[key] = coercedValue;
                continue;
            }

            coerced[key] = TypeCoercionHelper.Normalize(value) ?? string.Empty;
        }

        return coerced;
    }

    public static object? CoercePropertyValue(PropertyDefinition property, object? value)
    {
        if (value is null)
            return null;

        var descriptor = property.TypeDescriptor;
        if (!descriptor.IsNestedList)
            return TypeCoercionHelper.Normalize(value);

        return CoerceNestedList(property.Name, descriptor.DeclaredType, value, descriptor.LeafType, descriptor.ListDepth);
    }

    private static object? CoerceNestedList(
        string propertyName,
        string declaredType,
        object? value,
        LogicalTypeID leafType,
        int remainingDepth)
    {
        if (value is null)
            return null;

        if (remainingDepth == 0)
            return CoerceLeafValue(leafType, value);

        if (value is string || value is not IEnumerable enumerable)
            throw new InvalidOperationException(
                $"Property '{propertyName}' expects value of declared type {declaredType}.");

        var list = new List<object?>();
        foreach (var item in enumerable)
            list.Add(CoerceNestedList(propertyName, declaredType, item, leafType, remainingDepth - 1));
        return list;
    }

    private static object? CoerceLeafValue(LogicalTypeID leafType, object? value)
    {
        if (value is null)
            return null;

        return leafType switch
        {
            LogicalTypeID.FLOAT => (float)TypeCoercionHelper.ToDouble(value),
            LogicalTypeID.DOUBLE => TypeCoercionHelper.ToDouble(value),
            LogicalTypeID.INT64 => TypeCoercionHelper.ToInt64(value),
            LogicalTypeID.INT32 => checked((int)TypeCoercionHelper.ToInt64(value)),
            LogicalTypeID.INT16 => checked((short)TypeCoercionHelper.ToInt64(value)),
            LogicalTypeID.INT8 => checked((sbyte)TypeCoercionHelper.ToInt64(value)),
            LogicalTypeID.UINT64 => checked((ulong)TypeCoercionHelper.ToInt64(value)),
            LogicalTypeID.UINT32 => checked((uint)TypeCoercionHelper.ToInt64(value)),
            LogicalTypeID.UINT16 => checked((ushort)TypeCoercionHelper.ToInt64(value)),
            LogicalTypeID.UINT8 => checked((byte)TypeCoercionHelper.ToInt64(value)),
            LogicalTypeID.BOOL => TypeCoercionHelper.ToBool(value),
            LogicalTypeID.STRING => TypeCoercionHelper.ToBogDbString(value) ?? string.Empty,
            _ => TypeCoercionHelper.Normalize(value)
        };
    }
}
