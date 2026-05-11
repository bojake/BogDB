namespace BogDb.Core.Extraction;

/// <summary>
/// Compact in-memory read view over a normalized GraphShard payload.
/// </summary>
public sealed class GraphShardRuntimeView
{
    private readonly GraphShard _shard;
    private readonly Dictionary<string, (string TableName, NodeShardRow Row)> _nodesById;
    private readonly Dictionary<string, (string RelType, RelShardRow Row)> _relsById;

    private GraphShardRuntimeView(
        GraphShard shard,
        Dictionary<string, (string TableName, NodeShardRow Row)> nodesById,
        Dictionary<string, (string RelType, RelShardRow Row)> relsById)
    {
        _shard = shard;
        _nodesById = nodesById;
        _relsById = relsById;
    }

    public GraphShard Shard => _shard;

    public int NodeCount => _nodesById.Count;
    public int EdgeCount => _relsById.Count;

    public static GraphShardRuntimeView Load(GraphShard shard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        var normalized = GraphShardNormalizer.Normalize(shard);
        GraphShardValidator.Validate(normalized);

        var nodesById = new Dictionary<string, (string TableName, NodeShardRow Row)>(StringComparer.Ordinal);
        foreach (var (tableName, table) in normalized.NodeTables)
        {
            foreach (var row in table.Rows)
                nodesById[row.ExternalId] = (tableName, row);
        }

        var relsById = new Dictionary<string, (string RelType, RelShardRow Row)>(StringComparer.Ordinal);
        foreach (var (relType, table) in normalized.RelTables)
        {
            foreach (var row in table.Rows)
                relsById[row.RelId] = (relType, row);
        }

        return new GraphShardRuntimeView(normalized, nodesById, relsById);
    }

    public bool HasNode(string externalId)
        => _nodesById.ContainsKey(externalId);

    public bool TryGetNode(string externalId, out string tableName, out NodeShardRow row)
    {
        if (_nodesById.TryGetValue(externalId, out var value))
        {
            tableName = value.TableName;
            row = value.Row;
            return true;
        }

        tableName = string.Empty;
        row = null!;
        return false;
    }

    public IReadOnlyDictionary<string, object?> GetNodeProperties(string externalId)
    {
        if (!TryGetNode(externalId, out _, out var row))
            throw new KeyNotFoundException($"Node '{externalId}' was not found in the runtime view.");

        return row.Properties;
    }

    public bool HasRelationship(string relId)
        => _relsById.ContainsKey(relId);

    public bool TryGetRelationship(string relId, out string relType, out RelShardRow row)
    {
        if (_relsById.TryGetValue(relId, out var value))
        {
            relType = value.RelType;
            row = value.Row;
            return true;
        }

        relType = string.Empty;
        row = null!;
        return false;
    }

    public IReadOnlyList<ShardEdgeRef> GetOutgoing(string externalId, string? relType = null)
        => GetEdges(_shard.Adjacency.Outgoing, externalId, relType);

    public IReadOnlyList<ShardEdgeRef> GetIncoming(string externalId, string? relType = null)
        => GetEdges(_shard.Adjacency.Incoming, externalId, relType);

    public IReadOnlyList<string> Expand(
        string externalId,
        bool includeOutgoing = true,
        bool includeIncoming = false,
        string? relType = null)
    {
        var neighbors = new SortedSet<string>(StringComparer.Ordinal);

        if (includeOutgoing)
        {
            foreach (var edge in GetOutgoing(externalId, relType))
                neighbors.Add(edge.NeighborNodeId);
        }

        if (includeIncoming)
        {
            foreach (var edge in GetIncoming(externalId, relType))
                neighbors.Add(edge.NeighborNodeId);
        }

        return neighbors.ToList();
    }

    private static IReadOnlyList<ShardEdgeRef> GetEdges(
        IReadOnlyDictionary<string, List<ShardEdgeRef>> map,
        string externalId,
        string? relType)
    {
        if (!map.TryGetValue(externalId, out var edges))
            return [];

        return string.IsNullOrWhiteSpace(relType)
            ? edges
            : edges.Where(edge => string.Equals(edge.RelType, relType, StringComparison.Ordinal)).ToList();
    }
}
