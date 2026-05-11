using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.Distinct;

/// <summary>
/// Eliminates duplicate result rows using structural value equality rather than
/// string fingerprints, so DISTINCT preserves type-sensitive semantics and
/// nested value equality more faithfully.
/// C++ parity: distinct.h
/// </summary>
public sealed class PhysicalDistinct : PhysicalOperator
{
    private readonly HashSet<DistinctKey> _seen = new(DistinctKeyComparer.Instance);

    public PhysicalDistinct(PhysicalOperator child, uint id)
        : base(PhysicalOperatorType.DISTINCT, child, id)
    {
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        while (Children[0].GetNextTuple(context))
        {
            var key = DistinctKey.FromContext(context);
            if (_seen.Add(key))
                return true; // first occurrence — emit
            // duplicate — skip and pull next
        }
        return false;
    }

    private enum DistinctKeyKind : byte
    {
        ProjectionRow,
        NodeProperties,
        NodeIdOnly,
        Empty
    }

    private sealed class DistinctKey
    {
        public DistinctKeyKind Kind { get; }
        public string[] Names { get; }
        public object?[] Values { get; }

        private DistinctKey(DistinctKeyKind kind, string[] names, object?[] values)
        {
            Kind = kind;
            Names = names;
            Values = values;
        }

        public static DistinctKey FromContext(ExecutionContext context)
        {
            if (context.CurrentProjectionRow != null)
            {
                return new DistinctKey(
                    DistinctKeyKind.ProjectionRow,
                    Array.Empty<string>(),
                    Array.ConvertAll(context.CurrentProjectionRow, TypeCoercionHelper.Normalize));
            }

            if (context.CurrentNodeProperties != null)
            {
                var ordered = context.CurrentNodeProperties
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToArray();
                var names = ordered.Select(pair => pair.Key).ToArray();
                var values = ordered.Select(pair => TypeCoercionHelper.Normalize(pair.Value)).ToArray();
                return new DistinctKey(DistinctKeyKind.NodeProperties, names, values);
            }

            if (context.CurrentNodeId is not null)
            {
                return new DistinctKey(
                    DistinctKeyKind.NodeIdOnly,
                    Array.Empty<string>(),
                    new object?[] { TypeCoercionHelper.Normalize(context.CurrentNodeId) });
            }

            return new DistinctKey(DistinctKeyKind.Empty, Array.Empty<string>(), Array.Empty<object?>());
        }
    }

    private sealed class DistinctKeyComparer : IEqualityComparer<DistinctKey>
    {
        public static DistinctKeyComparer Instance { get; } = new();

        public bool Equals(DistinctKey? x, DistinctKey? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Kind != y.Kind)
                return false;
            if (x.Names.Length != y.Names.Length || x.Values.Length != y.Values.Length)
                return false;

            for (var i = 0; i < x.Names.Length; i++)
            {
                if (!string.Equals(x.Names[i], y.Names[i], StringComparison.Ordinal))
                    return false;
            }

            for (var i = 0; i < x.Values.Length; i++)
            {
                if (!StructuralValueComparer.AreEqual(x.Values[i], y.Values[i]))
                    return false;
            }

            return true;
        }

        public int GetHashCode(DistinctKey obj)
        {
            var hash = new HashCode();
            hash.Add((byte)obj.Kind);
            foreach (var name in obj.Names)
                hash.Add(name, StringComparer.Ordinal);
            foreach (var value in obj.Values)
                hash.Add(StructuralValueComparer.GetStructuralHashCode(value));
            return hash.ToHashCode();
        }
    }
}
