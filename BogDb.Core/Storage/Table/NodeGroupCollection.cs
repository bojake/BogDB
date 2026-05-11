using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Storage.Stats;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Column-oriented container of node groups used by disk snapshot paths.
/// </summary>
public sealed class NodeGroupCollection
{
    private readonly List<NodeGroup> _nodeGroups = new();
    private readonly int _groupCapacity;
    private readonly TableStats _stats;

    public NodeGroupCollection(int groupCapacity = 1024)
    {
        _groupCapacity = groupCapacity;
        _stats = new TableStats(new List<PhysicalTypeID>());
    }

    public ulong GetNumTotalRows()
    {
        ulong total = 0;
        foreach (var g in _nodeGroups)
            total += g.GetNumRows();
        return total;
    }

    public ulong GetNumNodeGroups() => (ulong)_nodeGroups.Count;
    public NodeGroup GetNodeGroup(ulong index) => _nodeGroups[(int)index];
    public TableStats GetStats() => new TableStats(_stats);

    public void AppendRow(object nodeId, Dictionary<string, object> properties)
    {
        if (_nodeGroups.Count == 0 || _nodeGroups[^1].IsFull)
            _nodeGroups.Add(new NodeGroup(_groupCapacity));

        _nodeGroups[^1].AppendRow(nodeId, properties);
        _stats.IncrementCardinality(1);
    }

    public IEnumerable<KeyValuePair<object, Dictionary<string, object>>> EnumerateRows()
    {
        foreach (var group in _nodeGroups)
        {
            foreach (var row in group.EnumerateRows())
                yield return row;
        }
    }

    public IEnumerable<KeyValuePair<object, Dictionary<string, object>>> EnumerateRows(Transaction.Transaction tx)
    {
        foreach (var group in _nodeGroups)
        {
            foreach (var row in group.EnumerateRows(tx))
                yield return row;
        }
    }
}
