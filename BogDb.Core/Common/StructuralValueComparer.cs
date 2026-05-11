using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BogDb.Core.Common;

internal sealed class StructuralValueComparer : IEqualityComparer<object?>
{
    public static StructuralValueComparer Instance { get; } = new();

    public new bool Equals(object? x, object? y)
        => AreEqual(x, y);

    public int GetHashCode(object obj)
        => GetStructuralHashCode(obj);

    public static bool AreEqual(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;

        left = TypeCoercionHelper.Normalize(left);
        right = TypeCoercionHelper.Normalize(right);

        if (TryAsDictionary(left, out var leftEntries) && TryAsDictionary(right, out var rightEntries))
        {
            if (leftEntries.Count != rightEntries.Count)
                return false;

            for (var i = 0; i < leftEntries.Count; i++)
            {
                if (!string.Equals(leftEntries[i].Key, rightEntries[i].Key, StringComparison.Ordinal))
                    return false;
                if (!AreEqual(leftEntries[i].Value, rightEntries[i].Value))
                    return false;
            }

            return true;
        }

        if (TryAsSequence(left, out var leftItems) && TryAsSequence(right, out var rightItems))
        {
            if (leftItems.Count != rightItems.Count)
                return false;

            for (var i = 0; i < leftItems.Count; i++)
            {
                if (!AreEqual(leftItems[i], rightItems[i]))
                    return false;
            }

            return true;
        }

        if (TryGetNumericValue(left, out var leftNumber) && TryGetNumericValue(right, out var rightNumber))
            return leftNumber.Equals(rightNumber);

        return object.Equals(left, right);
    }

    public static int GetStructuralHashCode(object? value)
    {
        if (value is null)
            return 0;

        value = TypeCoercionHelper.Normalize(value);

        if (TryAsDictionary(value, out var entries))
        {
            var hash = new HashCode();
            hash.Add(0xD1C710A);
            foreach (var entry in entries)
            {
                hash.Add(entry.Key, StringComparer.Ordinal);
                hash.Add(GetStructuralHashCode(entry.Value));
            }
            return hash.ToHashCode();
        }

        if (TryAsSequence(value, out var items))
        {
            var hash = new HashCode();
            hash.Add(0x51F15EED);
            foreach (var item in items)
                hash.Add(GetStructuralHashCode(item));
            return hash.ToHashCode();
        }

        if (TryGetNumericValue(value, out var number))
            return number.GetHashCode();

        return value.GetHashCode();
    }

    private static bool TryAsSequence(object value, out IReadOnlyList<object?> items)
    {
        if (value is string || value is byte[] || value is IDictionary ||
            value is IEnumerable<KeyValuePair<string, object?>> ||
            value is IEnumerable<KeyValuePair<object?, object?>> ||
            value is not IEnumerable enumerable)
        {
            items = Array.Empty<object?>();
            return false;
        }

        if (value is IReadOnlyList<object?> readOnlyList)
        {
            items = readOnlyList;
            return true;
        }

        if (value is List<object?> typedList)
        {
            items = typedList;
            return true;
        }

        var materialized = new List<object?>();
        foreach (var item in enumerable)
            materialized.Add(item);
        items = materialized;
        return true;
    }

    private static bool TryAsDictionary(object value, out IReadOnlyList<KeyValuePair<string, object?>> items)
    {
        if (value is IEnumerable<KeyValuePair<string, object?>> stringPairs)
        {
            items = stringPairs
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))
                .ToArray();
            return true;
        }

        if (value is IEnumerable<KeyValuePair<object?, object?>> objectPairs)
        {
            items = objectPairs
                .Select(pair => new KeyValuePair<string, object?>(
                    TypeCoercionHelper.ToBogDbString(pair.Key) ?? string.Empty,
                    pair.Value))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToArray();
            return true;
        }

        if (value is IDictionary dictionary)
        {
            var list = new List<KeyValuePair<string, object?>>();
            foreach (DictionaryEntry entry in dictionary)
            {
                list.Add(new KeyValuePair<string, object?>(
                    TypeCoercionHelper.ToBogDbString(entry.Key) ?? string.Empty,
                    entry.Value));
            }

            items = list.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
            return true;
        }

        items = Array.Empty<KeyValuePair<string, object?>>();
        return false;
    }

    private static bool TryGetNumericValue(object value, out decimal numeric)
    {
        switch (TypeCoercionHelper.Normalize(value))
        {
            case byte byteValue:
                numeric = byteValue;
                return true;
            case sbyte sbyteValue:
                numeric = sbyteValue;
                return true;
            case short shortValue:
                numeric = shortValue;
                return true;
            case ushort ushortValue:
                numeric = ushortValue;
                return true;
            case int intValue:
                numeric = intValue;
                return true;
            case uint uintValue:
                numeric = uintValue;
                return true;
            case long longValue:
                numeric = longValue;
                return true;
            case ulong ulongValue:
                numeric = ulongValue;
                return true;
            case float floatValue:
                numeric = Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
                return true;
            case double doubleValue:
                numeric = Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
                return true;
            case decimal decimalValue:
                numeric = decimalValue;
                return true;
            default:
                numeric = default;
                return false;
        }
    }
}
