using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BogDb.Core.Common;

internal sealed class StructuralValueOrderComparer : IComparer<object?>
{
    public static StructuralValueOrderComparer Instance { get; } = new();

    public int Compare(object? left, object? right)
        => CompareValues(left, right);

    public static int CompareValues(object? left, object? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        left = TypeCoercionHelper.Normalize(left);
        right = TypeCoercionHelper.Normalize(right);

        if (TryAsDictionary(left, out var leftDict) && TryAsDictionary(right, out var rightDict))
            return CompareDictionaries(leftDict, rightDict);

        if (TryAsSequence(left, out var leftItems) && TryAsSequence(right, out var rightItems))
            return CompareSequences(leftItems, rightItems);

        if (TryGetNumericValue(left, out var leftNumber) && TryGetNumericValue(right, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);

        if (left.GetType() == right.GetType() && left is IComparable comparable)
            return comparable.CompareTo(right);

        var leftText = TypeCoercionHelper.ToBogDbString(left) ?? string.Empty;
        var rightText = TypeCoercionHelper.ToBogDbString(right) ?? string.Empty;
        return string.CompareOrdinal(leftText, rightText);
    }

    private static int CompareSequences(IReadOnlyList<object?> left, IReadOnlyList<object?> right)
    {
        var count = Math.Min(left.Count, right.Count);
        for (var i = 0; i < count; i++)
        {
            var cmp = CompareValues(left[i], right[i]);
            if (cmp != 0)
                return cmp;
        }

        return left.Count.CompareTo(right.Count);
    }

    private static int CompareDictionaries(
        IReadOnlyList<KeyValuePair<string, object?>> left,
        IReadOnlyList<KeyValuePair<string, object?>> right)
    {
        var count = Math.Min(left.Count, right.Count);
        for (var i = 0; i < count; i++)
        {
            var keyCmp = string.CompareOrdinal(left[i].Key, right[i].Key);
            if (keyCmp != 0)
                return keyCmp;

            var valueCmp = CompareValues(left[i].Value, right[i].Value);
            if (valueCmp != 0)
                return valueCmp;
        }

        return left.Count.CompareTo(right.Count);
    }

    private static bool TryAsSequence(object value, out IReadOnlyList<object?> items)
    {
        if (value is string || value is byte[] || value is IDictionary)
        {
            items = Array.Empty<object?>();
            return false;
        }

        if (value is IEnumerable<KeyValuePair<string, object?>> || value is IEnumerable<KeyValuePair<object?, object?>>)
        {
            items = Array.Empty<object?>();
            return false;
        }

        if (value is not IEnumerable enumerable)
        {
            items = Array.Empty<object?>();
            return false;
        }

        if (value is IReadOnlyList<object?> readOnlyList)
        {
            items = readOnlyList;
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
