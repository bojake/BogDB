using System.Collections.Concurrent;
using BogDb.Core.Common;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// A sparse or dense mask identifying active execution footprints natively across pipeline boundaries.
/// Frontiers isolate iterative algorithms (PageRank, SSSP) cleanly bypassing redundant cyclic evaluations dynamically.
/// </summary>
public sealed class Frontier
{
    private readonly ConcurrentDictionary<ulong, bool> _activeNodes;
    private long _activeCount;

    public bool HasActiveNodes => _activeCount > 0;
    public long ActiveCount => _activeCount;

    public Frontier()
    {
        _activeNodes = new ConcurrentDictionary<ulong, bool>();
        _activeCount = 0;
    }

    public void AddActive(InternalID nodeID)
    {
        if (_activeNodes.TryAdd(nodeID.Offset, true))
        {
            System.Threading.Interlocked.Increment(ref _activeCount);
        }
    }

    public void RemoveActive(InternalID nodeID)
    {
        if (_activeNodes.TryRemove(nodeID.Offset, out _))
        {
            System.Threading.Interlocked.Decrement(ref _activeCount);
        }
    }

    public bool IsActive(InternalID nodeID)
    {
        return _activeNodes.ContainsKey(nodeID.Offset);
    }

    public void Clear()
    {
        _activeNodes.Clear();
        _activeCount = 0;
    }
}
