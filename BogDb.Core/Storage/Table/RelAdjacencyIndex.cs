using System.Collections.Generic;
using BogDb.Core.Main;

namespace BogDb.Core.Storage.Table;

internal sealed class RelAdjacencyIndex
{
    private readonly Dictionary<object, List<RelTableData.AdjacencyEntry>> _outAdj;
    private readonly Dictionary<object, List<RelTableData.AdjacencyEntry>> _inAdj;

    public RelAdjacencyIndex(
        Dictionary<object, List<RelTableData.AdjacencyEntry>> outAdj,
        Dictionary<object, List<RelTableData.AdjacencyEntry>> inAdj)
    {
        _outAdj = outAdj;
        _inAdj = inAdj;
    }

    public void Add(EdgeKey key, int rowIndex)
    {
        AddEntry(key.From, _outAdj, rowIndex);
        AddEntry(key.To, _inAdj, rowIndex);
    }

    public void Remove(EdgeKey key, int rowIndex)
    {
        RemoveEntry(key.From, _outAdj, rowIndex);
        RemoveEntry(key.To, _inAdj, rowIndex);
    }

    public void Replace(EdgeKey key, int oldRowIndex, int newRowIndex)
    {
        ReplaceEntry(key.From, _outAdj, oldRowIndex, newRowIndex);
        ReplaceEntry(key.To, _inAdj, oldRowIndex, newRowIndex);
    }

    public void Clear()
    {
        _outAdj.Clear();
        _inAdj.Clear();
    }

    public IReadOnlyList<RelTableData.AdjacencyEntry> GetOutgoing(object nodeId)
        => _outAdj.TryGetValue(nodeId, out var entries) ? entries : [];

    public IReadOnlyList<RelTableData.AdjacencyEntry> GetIncoming(object nodeId)
        => _inAdj.TryGetValue(nodeId, out var entries) ? entries : [];

    private static void AddEntry(
        object nodeId,
        Dictionary<object, List<RelTableData.AdjacencyEntry>> adjacency,
        int rowIndex)
    {
        if (!adjacency.TryGetValue(nodeId, out var entries))
        {
            entries = [];
            adjacency[nodeId] = entries;
        }

        entries.Add(new RelTableData.AdjacencyEntry(rowIndex));
    }

    private static void RemoveEntry(
        object nodeId,
        Dictionary<object, List<RelTableData.AdjacencyEntry>> adjacency,
        int rowIndex)
    {
        if (!adjacency.TryGetValue(nodeId, out var entries))
            return;

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].RowIndex != rowIndex)
                continue;

            entries.RemoveAt(i);
            if (entries.Count == 0)
                adjacency.Remove(nodeId);
            return;
        }
    }

    private static void ReplaceEntry(
        object nodeId,
        Dictionary<object, List<RelTableData.AdjacencyEntry>> adjacency,
        int oldRowIndex,
        int newRowIndex)
    {
        if (!adjacency.TryGetValue(nodeId, out var entries))
            return;

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].RowIndex != oldRowIndex)
                continue;

            entries[i] = new RelTableData.AdjacencyEntry(newRowIndex);
            return;
        }
    }
}
