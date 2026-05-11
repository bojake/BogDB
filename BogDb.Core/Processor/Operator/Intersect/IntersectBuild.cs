using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.Intersect;

/// <summary>
/// Builds a hash set of node IDs from a pipeline scan for use by the Intersect probe.
/// C++ parity: intersect_build.h — accumulates keys from the adjacency lists of one
/// relation type so the probe side can test membership across multiple build sides.
/// </summary>
public sealed class IntersectBuild : PhysicalOperator
{
    private readonly int _keyDataPos;
    // Keyed by node ID (long). Value list holds full projection rows if needed downstream.
    private readonly Dictionary<long, List<object[]>> _hashTable;
    private bool _built;

    public IntersectBuild(PhysicalOperator child, int keyDataPos, uint id)
        : base(PhysicalOperatorType.INTERSECT_BUILD, child, id)
    {
        _keyDataPos = keyDataPos;
        _hashTable = new Dictionary<long, List<object[]>>();
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_built) return false;
        _built = true;

        // Drain entire child pipeline and hash every output node ID
        while (Children[0].GetNextTuple(context))
        {
            var key = ResolveProbeKey(context);
            if (key.HasValue)
            {
                if (!_hashTable.TryGetValue(key.Value, out var bucket))
                {
                    bucket = new List<object[]>();
                    _hashTable[key.Value] = bucket;
                }
                // Store projection row snapshot if available
                if (context.CurrentProjectionRow != null)
                    bucket.Add(context.CurrentProjectionRow);
            }
        }
        return false; // sink — never emits tuples upward
    }

    private long? ResolveProbeKey(ExecutionContext context)
    {
        // First try the specific key data position from the projection row
        if (context.CurrentProjectionRow != null && _keyDataPos < context.CurrentProjectionRow.Length)
        {
            var val = context.CurrentProjectionRow[_keyDataPos];
            if (val is long l) return l;
            if (val is int i)  return i;
        }
        // Fallback: use the current node ID from scan
        if (context.CurrentNodeId is long nodeId) return nodeId;
        return null;
    }

    /// <summary>Exposed to <see cref="Intersect"/> for probe-side membership testing.</summary>
    public bool ContainsKey(long key) => _hashTable.ContainsKey(key);
    public IReadOnlyDictionary<long, List<object[]>> GetHashTable() => _hashTable;
}
