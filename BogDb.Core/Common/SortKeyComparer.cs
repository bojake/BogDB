using System;
using System.Collections.Generic;

namespace BogDb.Core.Common;

internal sealed class SortKeyComparer
{
    private readonly IReadOnlyList<bool> _isAscending;
    private readonly PrimitiveKeyKind[] _keyKinds;

    public SortKeyComparer(IReadOnlyList<bool> isAscending)
    {
        _isAscending = isAscending;
        _keyKinds = new PrimitiveKeyKind[isAscending.Count];
    }

    public int Compare(object?[] left, object?[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var cmp = CompareValue(i, left[i], right[i]);
            if (cmp != 0)
            {
                return _isAscending[i] ? cmp : -cmp;
            }
        }

        return 0;
    }

    public int CompareWithSequence(object?[] left, object?[] right, long leftSequence, long rightSequence)
    {
        var cmp = Compare(left, right);
        return cmp != 0 ? cmp : leftSequence.CompareTo(rightSequence);
    }

    private int CompareValue(int index, object? left, object? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var kind = _keyKinds[index];
        if (TryComparePrimitive(kind, left, right, out var cmp))
        {
            return cmp;
        }

        if (kind == PrimitiveKeyKind.Unknown && TryInferPrimitiveKind(left, right, out kind))
        {
            _keyKinds[index] = kind;
            if (TryComparePrimitive(kind, left, right, out cmp))
            {
                return cmp;
            }
        }

        return StructuralValueOrderComparer.CompareValues(left, right);
    }

    private static bool TryInferPrimitiveKind(object left, object right, out PrimitiveKeyKind kind)
    {
        switch (left)
        {
            case bool when right is bool:
                kind = PrimitiveKeyKind.Bool;
                return true;
            case long when right is long:
                kind = PrimitiveKeyKind.Int64;
                return true;
            case int when right is int:
                kind = PrimitiveKeyKind.Int32;
                return true;
            case short when right is short:
                kind = PrimitiveKeyKind.Int16;
                return true;
            case sbyte when right is sbyte:
                kind = PrimitiveKeyKind.Int8;
                return true;
            case ulong when right is ulong:
                kind = PrimitiveKeyKind.UInt64;
                return true;
            case uint when right is uint:
                kind = PrimitiveKeyKind.UInt32;
                return true;
            case ushort when right is ushort:
                kind = PrimitiveKeyKind.UInt16;
                return true;
            case byte when right is byte:
                kind = PrimitiveKeyKind.UInt8;
                return true;
            case decimal when right is decimal:
                kind = PrimitiveKeyKind.Decimal;
                return true;
            case string when right is string:
                kind = PrimitiveKeyKind.String;
                return true;
            case DateOnly when right is DateOnly:
                kind = PrimitiveKeyKind.DateOnly;
                return true;
            case DateTime when right is DateTime:
                kind = PrimitiveKeyKind.DateTime;
                return true;
            case DateTimeOffset when right is DateTimeOffset:
                kind = PrimitiveKeyKind.DateTimeOffset;
                return true;
            case Guid when right is Guid:
                kind = PrimitiveKeyKind.Guid;
                return true;
            case BogDbInterval when right is BogDbInterval:
                kind = PrimitiveKeyKind.Interval;
                return true;
            case Int128 when right is Int128:
                kind = PrimitiveKeyKind.Int128;
                return true;
            case UInt128 when right is UInt128:
                kind = PrimitiveKeyKind.UInt128;
                return true;
            default:
                kind = PrimitiveKeyKind.Unknown;
                return false;
        }
    }

    private static bool TryComparePrimitive(
        PrimitiveKeyKind kind,
        object left,
        object right,
        out int cmp)
    {
        switch (kind)
        {
            case PrimitiveKeyKind.Bool when left is bool lb && right is bool rb:
                cmp = lb.CompareTo(rb);
                return true;
            case PrimitiveKeyKind.Int64 when left is long ll && right is long rl:
                cmp = ll.CompareTo(rl);
                return true;
            case PrimitiveKeyKind.Int32 when left is int li && right is int ri:
                cmp = li.CompareTo(ri);
                return true;
            case PrimitiveKeyKind.Int16 when left is short ls && right is short rs:
                cmp = ls.CompareTo(rs);
                return true;
            case PrimitiveKeyKind.Int8 when left is sbyte lsb && right is sbyte rsb:
                cmp = lsb.CompareTo(rsb);
                return true;
            case PrimitiveKeyKind.UInt64 when left is ulong lul && right is ulong rul:
                cmp = lul.CompareTo(rul);
                return true;
            case PrimitiveKeyKind.UInt32 when left is uint lui && right is uint rui:
                cmp = lui.CompareTo(rui);
                return true;
            case PrimitiveKeyKind.UInt16 when left is ushort lus && right is ushort rus:
                cmp = lus.CompareTo(rus);
                return true;
            case PrimitiveKeyKind.UInt8 when left is byte lub && right is byte rub:
                cmp = lub.CompareTo(rub);
                return true;
            case PrimitiveKeyKind.Decimal when left is decimal ld && right is decimal rd:
                cmp = ld.CompareTo(rd);
                return true;
            case PrimitiveKeyKind.String when left is string lstr && right is string rstr:
                cmp = string.CompareOrdinal(lstr, rstr);
                return true;
            case PrimitiveKeyKind.DateOnly when left is DateOnly ldate && right is DateOnly rdate:
                cmp = ldate.CompareTo(rdate);
                return true;
            case PrimitiveKeyKind.DateTime when left is DateTime ldt && right is DateTime rdt:
                cmp = ldt.CompareTo(rdt);
                return true;
            case PrimitiveKeyKind.DateTimeOffset when left is DateTimeOffset ldto && right is DateTimeOffset rdto:
                cmp = ldto.CompareTo(rdto);
                return true;
            case PrimitiveKeyKind.Guid when left is Guid lg && right is Guid rg:
                cmp = lg.CompareTo(rg);
                return true;
            case PrimitiveKeyKind.Interval when left is BogDbInterval linterval && right is BogDbInterval rinterval:
                cmp = linterval.CompareTo(rinterval);
                return true;
            case PrimitiveKeyKind.Int128 when left is Int128 li128 && right is Int128 ri128:
                cmp = li128.CompareTo(ri128);
                return true;
            case PrimitiveKeyKind.UInt128 when left is UInt128 lu128 && right is UInt128 ru128:
                cmp = lu128.CompareTo(ru128);
                return true;
            default:
                cmp = 0;
                return false;
        }
    }

    private enum PrimitiveKeyKind : byte
    {
        Unknown = 0,
        Bool,
        Int64,
        Int32,
        Int16,
        Int8,
        UInt64,
        UInt32,
        UInt16,
        UInt8,
        Decimal,
        String,
        DateOnly,
        DateTime,
        DateTimeOffset,
        Guid,
        Interval,
        Int128,
        UInt128
    }
}
