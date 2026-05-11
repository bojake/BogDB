using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Core.Extraction;

/// <summary>
/// Builds transport-safe graph extraction payloads over bounded neighborhoods and explicit node sets.
/// </summary>
public sealed class GraphShardExtractor
{
    private readonly BogDatabase _database;

    public GraphShardExtractor(BogDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public GraphShard ExtractNeighborhood(
        string seedTable,
        object seedNodeId,
        GraphShardExtractionOptions? options = null,
        BogDb.Core.Transaction.Transaction? tx = null)
        => ExtractNeighborhood([new GraphNodeSelector { TableName = seedTable, NodeId = seedNodeId }], options, tx);

    public GraphShard ExtractNeighborhood(
        IEnumerable<GraphNodeSelector> seeds,
        GraphShardExtractionOptions? options = null,
        BogDb.Core.Transaction.Transaction? tx = null)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        var normalizedOptions = options ?? new GraphShardExtractionOptions();
        var state = new ExtractionState(_database, normalizedOptions, tx);

        var queue = new Queue<WorkItem>();
        foreach (var seed in seeds)
        {
            if (!state.TryIncludeSeedNode(seed.TableName, seed.NodeId, depth: 0, out var seedExternalId))
                continue;

            queue.Enqueue(new WorkItem(seed.TableName, seed.NodeId, seedExternalId, 0));
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!state.MarkExpanded(current.ExternalId))
                continue;

            foreach (var relTable in state.GetOrderedRelTables())
            {
                if (!state.IsRelTypeAllowed(relTable.Key))
                    continue;

                if (normalizedOptions.IncludeOutgoing)
                {
                    foreach (var (edgeKey, props) in relTable.Value.EnumerateOutgoingRows(current.NodeId, tx))
                    {
                        if (!state.TryResolveEdgeTables(relTable.Value, current.TableName, edgeKey.To, outgoing: true, out var fromTable, out var toTable, out var neighborTable))
                            continue;

                        ProcessDiscoveredEdge(
                            state,
                            relTable.Key,
                            current,
                            currentIsSource: true,
                            fromTable,
                            toTable,
                            neighborId: edgeKey.To,
                            neighborTable,
                            props,
                            nextDepth: current.Depth + 1,
                            queue);
                    }
                }

                if (normalizedOptions.IncludeIncoming)
                {
                    foreach (var (edgeKey, props) in relTable.Value.EnumerateIncomingRows(current.NodeId, tx))
                    {
                        if (!state.TryResolveEdgeTables(relTable.Value, current.TableName, edgeKey.From, outgoing: false, out var fromTable, out var toTable, out var neighborTable))
                            continue;

                        ProcessDiscoveredEdge(
                            state,
                            relTable.Key,
                            current,
                            currentIsSource: false,
                            fromTable,
                            toTable,
                            neighborId: edgeKey.From,
                            neighborTable,
                            props,
                            nextDepth: current.Depth + 1,
                            queue);
                    }
                }
            }
        }

        return state.BuildShard();
    }

    public GraphShard ExtractNodeSet(
        IEnumerable<GraphNodeSelector> nodes,
        GraphShardExtractionOptions? options = null,
        BogDb.Core.Transaction.Transaction? tx = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var normalizedOptions = options ?? new GraphShardExtractionOptions();
        var state = new ExtractionState(_database, normalizedOptions, tx);

        foreach (var node in nodes)
            state.TryIncludeSeedNode(node.TableName, node.NodeId, depth: 0, out _);

        var included = state.GetIncludedNodes().Values
            .OrderBy(n => n.TableName, StringComparer.Ordinal)
            .ThenBy(n => n.ExternalId, StringComparer.Ordinal)
            .ToList();

        foreach (var current in included)
        {
            foreach (var relTable in state.GetOrderedRelTables())
            {
                if (!state.IsRelTypeAllowed(relTable.Key))
                    continue;

                if (normalizedOptions.IncludeOutgoing)
                {
                    foreach (var (edgeKey, props) in relTable.Value.EnumerateOutgoingRows(current.NodeId, tx))
                    {
                        if (!state.TryResolveEdgeTables(relTable.Value, current.TableName, edgeKey.To, outgoing: true, out var fromTable, out var toTable, out var neighborTable))
                            continue;
                        if (!state.TryBuildExternalNodeId(neighborTable, edgeKey.To, out var neighborExternalId))
                            continue;
                        if (!state.IsNodeIncluded(neighborExternalId))
                            continue;

                        state.TryIncludeEdge(
                            relTable.Key,
                            fromTable,
                            toTable,
                            current.ExternalId,
                            neighborExternalId,
                            props,
                            addOutgoingAdjacency: normalizedOptions.IncludeAdjacency,
                            addIncomingAdjacency: normalizedOptions.IncludeAdjacency);
                    }
                }

                if (normalizedOptions.IncludeIncoming)
                {
                    foreach (var (edgeKey, props) in relTable.Value.EnumerateIncomingRows(current.NodeId, tx))
                    {
                        if (!state.TryResolveEdgeTables(relTable.Value, current.TableName, edgeKey.From, outgoing: false, out var fromTable, out var toTable, out var neighborTable))
                            continue;
                        if (!state.TryBuildExternalNodeId(neighborTable, edgeKey.From, out var neighborExternalId))
                            continue;
                        if (!state.IsNodeIncluded(neighborExternalId))
                            continue;

                        state.TryIncludeEdge(
                            relTable.Key,
                            fromTable,
                            toTable,
                            neighborExternalId,
                            current.ExternalId,
                            props,
                            addOutgoingAdjacency: normalizedOptions.IncludeAdjacency,
                            addIncomingAdjacency: normalizedOptions.IncludeAdjacency);
                    }
                }
            }
        }

        return state.BuildShard();
    }

    private static void ProcessDiscoveredEdge(
        ExtractionState state,
        string relType,
        WorkItem current,
        bool currentIsSource,
        string fromTable,
        string toTable,
        object neighborId,
        string neighborTable,
        Dictionary<string, object> props,
        int nextDepth,
        Queue<WorkItem> queue)
    {
        var overDepth = state.IsBeyondDepth(nextDepth);
        if (overDepth && state.Options.StopAtBoundary)
        {
            state.RecordDepthBoundary();
            state.TryRecordBoundaryNode(neighborTable, neighborId);
            state.RecordEdgeBoundary();
            return;
        }

        var neighborIncluded = state.TryIncludeNode(neighborTable, neighborId, nextDepth, out var neighborExternalId);
        if (!neighborIncluded)
        {
            state.TryRecordBoundaryNode(neighborTable, neighborId);
            state.RecordEdgeBoundary();
            return;
        }

        var edgeIncluded = state.TryIncludeEdge(
            relType,
            fromTable,
            toTable,
            currentIsSource ? current.ExternalId : neighborExternalId,
            currentIsSource ? neighborExternalId : current.ExternalId,
            props,
            addOutgoingAdjacency: state.Options.IncludeAdjacency,
            addIncomingAdjacency: state.Options.IncludeAdjacency);

        if (!edgeIncluded)
        {
            state.TryRecordBoundaryNode(neighborTable, neighborId);
            state.RecordEdgeBoundary();
            return;
        }

        if (!overDepth)
            queue.Enqueue(new WorkItem(neighborTable, neighborId, neighborExternalId, nextDepth));
        else
        {
            state.RecordDepthBoundary();
            state.TryRecordBoundaryNode(neighborTable, neighborId);
        }
    }

    private sealed record WorkItem(string TableName, object NodeId, string ExternalId, int Depth);

    private sealed class ExtractionState
    {
        private readonly BogDatabase _database;
        private readonly BogDb.Core.Transaction.Transaction? _tx;
        private readonly StringComparer _comparer = StringComparer.Ordinal;
        private readonly Dictionary<string, ExtractedNode> _nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RelShardRow> _edges = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NodeShardTable> _nodeTables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RelShardTable> _relTables = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _nodeTableColumns = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _relTableColumns = new(StringComparer.Ordinal);
        private readonly HashSet<string> _boundaryNodeIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _expandedNodes = new(StringComparer.Ordinal);
        private readonly List<GraphShardSeedRecord> _seedRecords = [];

        public ExtractionState(
            BogDatabase database,
            GraphShardExtractionOptions options,
            BogDb.Core.Transaction.Transaction? tx)
        {
            _database = database;
            Options = options;
            _tx = tx;
        }

        public GraphShardExtractionOptions Options { get; }

        public IEnumerable<KeyValuePair<string, RelTableData>> GetOrderedRelTables()
            => _database.RelTables.OrderBy(kvp => kvp.Key, _comparer);

        public IReadOnlyDictionary<string, ExtractedNode> GetIncludedNodes() => _nodes;

        public bool MarkExpanded(string externalId) => _expandedNodes.Add(externalId);

        public bool IsNodeIncluded(string externalId) => _nodes.ContainsKey(externalId);

        public bool IsRelTypeAllowed(string relType)
            => Options.RelTypes.Count == 0 || Options.RelTypes.Contains(relType, _comparer);

        public bool IsBeyondDepth(int depth)
            => Options.MaxDepth.HasValue && depth > Options.MaxDepth.Value;

        public void RecordEdgeBoundary() => BoundaryHasEdge = true;
        public void RecordDepthBoundary() => TruncatedByDepth = true;

        public bool BoundaryHasEdge { get; private set; }
        public bool TruncatedByDepth { get; private set; }
        public bool TruncatedByNodeLimit { get; private set; }
        public bool TruncatedByEdgeLimit { get; private set; }

        public bool TryResolveNodeTable(object nodeId, string? preferredTable, out string tableName)
        {
            if (!string.IsNullOrWhiteSpace(preferredTable) &&
                _database.NodeTables.TryGetValue(preferredTable, out var preferredTableData) &&
                NodeExists(preferredTableData, nodeId))
            {
                tableName = preferredTable;
                return true;
            }

            foreach (var (candidateName, tableData) in _database.NodeTables.OrderBy(kvp => kvp.Key, _comparer))
            {
                if (NodeExists(tableData, nodeId))
                {
                    tableName = candidateName;
                    return true;
                }
            }

            tableName = string.Empty;
            return false;
        }

        public bool TryBuildExternalNodeId(string tableName, object nodeId, out string externalId)
        {
            try
            {
                externalId = GraphExternalIdFormatter.FormatNodeId(tableName, nodeId);
                return true;
            }
            catch
            {
                externalId = string.Empty;
                return false;
            }
        }

        public bool TryResolveEdgeTables(
            RelTableData relTable,
            string currentTableName,
            object neighborNodeId,
            bool outgoing,
            out string fromTable,
            out string toTable,
            out string neighborTable)
        {
            if (relTable.TryGetEndpointTables(out var declaredFrom, out var declaredTo))
            {
                var expectedCurrent = outgoing ? declaredFrom : declaredTo;
                var expectedNeighbor = outgoing ? declaredTo : declaredFrom;
                if (!_comparer.Equals(currentTableName, expectedCurrent))
                {
                    fromTable = string.Empty;
                    toTable = string.Empty;
                    neighborTable = string.Empty;
                    return false;
                }

                if (!TryResolveNodeTable(neighborNodeId, expectedNeighbor, out neighborTable) ||
                    !_comparer.Equals(neighborTable, expectedNeighbor))
                {
                    fromTable = string.Empty;
                    toTable = string.Empty;
                    neighborTable = string.Empty;
                    return false;
                }

                fromTable = declaredFrom;
                toTable = declaredTo;
                return true;
            }

            if (!TryResolveNodeTable(neighborNodeId, currentTableName, out neighborTable))
            {
                fromTable = string.Empty;
                toTable = string.Empty;
                return false;
            }

            fromTable = outgoing ? currentTableName : neighborTable;
            toTable = outgoing ? neighborTable : currentTableName;
            return true;
        }

        public bool TryIncludeNode(string tableName, object nodeId, int depth, out string externalId)
            => TryIncludeNodeInternal(tableName, nodeId, depth, out externalId, out _);

        public bool TryIncludeSeedNode(string tableName, object nodeId, int depth, out string externalId)
        {
            var included = TryIncludeNodeInternal(tableName, nodeId, depth, out externalId, out var reason);
            _seedRecords.Add(new GraphShardSeedRecord
            {
                TableName = tableName,
                RequestedNodeId = NormalizeRequestedNodeId(nodeId),
                ExternalId = externalId,
                Status = included ? "included" : "excluded",
                Reason = included ? string.Empty : reason
            });
            return included;
        }

        private bool TryIncludeNodeInternal(
            string tableName,
            object nodeId,
            int depth,
            out string externalId,
            out string reason)
        {
            externalId = string.Empty;
            reason = string.Empty;

            if (!NodePassesFilter(tableName))
            {
                reason = "filtered";
                return false;
            }

            if (!TryReadNodeProperties(tableName, nodeId, out var props))
            {
                reason = "missing";
                return false;
            }

            if (!TryBuildExternalNodeId(tableName, nodeId, out externalId))
            {
                reason = "invalid_id";
                return false;
            }

            if (_nodes.ContainsKey(externalId))
                return true;

            if (Options.MaxNodes.HasValue && _nodes.Count >= Options.MaxNodes.Value)
            {
                TruncatedByNodeLimit = true;
                reason = "node_limit";
                return false;
            }

            var properties = Options.IncludeNodeProperties
                ? ToNullableDictionary(props!)
                : new Dictionary<string, object?>(StringComparer.Ordinal);

            _nodes[externalId] = new ExtractedNode(tableName, nodeId, externalId, depth);
            var nodeTable = GetOrCreateNodeTable(tableName);
            nodeTable.Rows.Add(new NodeShardRow
            {
                ExternalId = externalId,
                Properties = properties
            });

            foreach (var column in properties.Keys.OrderBy(k => k, _comparer))
                _nodeTableColumns[tableName].Add(column);

            return true;
        }

        public bool TryRecordBoundaryNode(string tableName, object nodeId)
        {
            if (!Options.IncludeBoundaryMetadata)
                return false;

            if (!TryBuildExternalNodeId(tableName, nodeId, out var externalId))
                return false;

            _boundaryNodeIds.Add(externalId);
            return true;
        }

        public bool TryIncludeEdge(
            string relType,
            string fromTable,
            string toTable,
            string sourceExternalId,
            string targetExternalId,
            Dictionary<string, object> props,
            bool addOutgoingAdjacency,
            bool addIncomingAdjacency)
        {
            var properties = Options.IncludeRelProperties
                ? ToNullableDictionary(props)
                : new Dictionary<string, object?>(StringComparer.Ordinal);

            var relId = BuildRelId(relType, fromTable, toTable, sourceExternalId, targetExternalId, properties);
            if (_edges.ContainsKey(relId))
                return true;

            if (Options.MaxEdges.HasValue && _edges.Count >= Options.MaxEdges.Value)
            {
                TruncatedByEdgeLimit = true;
                return false;
            }

            _edges[relId] = new RelShardRow
            {
                RelId = relId,
                SourceNodeId = sourceExternalId,
                TargetNodeId = targetExternalId,
                Properties = properties
            };

            var relTable = GetOrCreateRelTable(relType, fromTable, toTable);
            relTable.Rows.Add(_edges[relId]);

            foreach (var column in properties.Keys.OrderBy(k => k, _comparer))
                _relTableColumns[relType].Add(column);

            if (addOutgoingAdjacency)
                AddAdjacency(relType, relId, sourceExternalId, targetExternalId, "out", outgoing: true);
            if (addIncomingAdjacency)
                AddAdjacency(relType, relId, targetExternalId, sourceExternalId, "in", outgoing: false);

            return true;
        }

        public GraphShard BuildShard()
        {
            foreach (var (tableName, nodeTable) in _nodeTables)
                nodeTable.PropertyColumns = _nodeTableColumns[tableName].OrderBy(k => k, _comparer).ToList();
            foreach (var (relType, relTable) in _relTables)
                relTable.PropertyColumns = _relTableColumns[relType].OrderBy(k => k, _comparer).ToList();

            var truncationReasons = new List<string>();
            if (TruncatedByNodeLimit) truncationReasons.Add("node_limit");
            if (TruncatedByEdgeLimit) truncationReasons.Add("edge_limit");
            if (TruncatedByDepth) truncationReasons.Add("depth_limit");

            var isTruncated = truncationReasons.Count > 0;
            var boundaryNodeIds = _boundaryNodeIds.OrderBy(id => id, _comparer).ToList();
            var fetchHints = BuildFetchHints(isTruncated, truncationReasons, boundaryNodeIds);

            return new GraphShard
            {
                FormatVersion = GraphShard.CurrentFormatVersion,
                GraphVersionToken = BuildGraphVersionToken(_database.Catalog.Version),
                ExtractorVersion = GraphShard.CurrentExtractorVersion,
                ExtractedAtUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ExtractionPolicy = BuildExtractionPolicy(Options),
                IsComplete = !isTruncated,
                NodeTables = _nodeTables,
                RelTables = _relTables,
                Adjacency = Options.IncludeAdjacency ? _adjacency : new GraphShardAdjacency(),
                Boundary = new GraphShardBoundary
                {
                    IsTruncated = isTruncated,
                    HasNodeBoundary = boundaryNodeIds.Count > 0,
                    HasEdgeBoundary = BoundaryHasEdge,
                    TruncatedByNodeLimit = TruncatedByNodeLimit,
                    TruncatedByEdgeLimit = TruncatedByEdgeLimit,
                    TruncatedByDepth = TruncatedByDepth,
                    TruncationReasons = truncationReasons,
                    BoundaryNodeIds = boundaryNodeIds,
                    FetchHints = fetchHints
                },
                Stats = new GraphShardStats
                {
                    NodeCount = _nodes.Count,
                    EdgeCount = _edges.Count,
                    BoundaryNodeCount = _boundaryNodeIds.Count,
                    NodeTableCount = _nodeTables.Count,
                    RelTableCount = _relTables.Count
                },
                Options = Options,
                SeedProvenance = new GraphShardSeedProvenance
                {
                    RequestedCount = _seedRecords.Count,
                    IncludedCount = _seedRecords.Count(r => string.Equals(r.Status, "included", StringComparison.Ordinal)),
                    ExcludedCount = _seedRecords.Count(r => string.Equals(r.Status, "excluded", StringComparison.Ordinal)),
                    RequestedSeeds = _seedRecords.ToList()
                },
                Metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["catalogVersion"] = _database.Catalog.Version,
                    ["extractorVersion"] = GraphShard.CurrentExtractorVersion
                }
            };
        }

        private GraphShardBoundaryFetchHints BuildFetchHints(
            bool isTruncated,
            List<string> truncationReasons,
            List<string> boundaryNodeIds)
        {
            var canResumeFromBoundary = boundaryNodeIds.Count > 0;
            var suggestedMaxDepth = TruncatedByDepth
                ? (Options.MaxDepth.HasValue ? Options.MaxDepth.Value + 1 : 1)
                : Options.MaxDepth;
            var suggestedMaxNodes = TruncatedByNodeLimit
                ? Math.Max(Options.MaxNodes ?? _nodes.Count, _nodes.Count + Math.Max(1, boundaryNodeIds.Count))
                : Options.MaxNodes;
            var suggestedMaxEdges = TruncatedByEdgeLimit
                ? Math.Max(Options.MaxEdges ?? _edges.Count, _edges.Count + Math.Max(1, boundaryNodeIds.Count))
                : Options.MaxEdges;

            return new GraphShardBoundaryFetchHints
            {
                ShouldFetchMore = isTruncated,
                CanResumeFromBoundary = canResumeFromBoundary,
                RecommendedSeedNodeIds = canResumeFromBoundary ? boundaryNodeIds : [],
                SuggestedMaxDepth = suggestedMaxDepth,
                SuggestedMaxNodes = suggestedMaxNodes,
                SuggestedMaxEdges = suggestedMaxEdges,
                Reasons = truncationReasons.ToList()
            };
        }

        private readonly GraphShardAdjacency _adjacency = new()
        {
            Outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal),
            Incoming = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal)
        };

        private NodeShardTable GetOrCreateNodeTable(string tableName)
        {
            if (!_nodeTables.TryGetValue(tableName, out var table))
            {
                table = new NodeShardTable { TableName = tableName };
                _nodeTables[tableName] = table;
                _nodeTableColumns[tableName] = new HashSet<string>(StringComparer.Ordinal);
            }

            return table;
        }

        private RelShardTable GetOrCreateRelTable(string relType, string fromTable, string toTable)
        {
            if (!_relTables.TryGetValue(relType, out var table))
            {
                table = new RelShardTable
                {
                    RelType = relType,
                    FromTable = fromTable,
                    ToTable = toTable
                };
                _relTables[relType] = table;
                _relTableColumns[relType] = new HashSet<string>(StringComparer.Ordinal);
            }
            else
            {
                if (string.IsNullOrEmpty(table.FromTable) && !string.IsNullOrEmpty(fromTable))
                    table.FromTable = fromTable;
                if (string.IsNullOrEmpty(table.ToTable) && !string.IsNullOrEmpty(toTable))
                    table.ToTable = toTable;
            }

            return table;
        }

        private void AddAdjacency(
            string relType,
            string relId,
            string ownerNodeId,
            string neighborNodeId,
            string direction,
            bool outgoing)
        {
            var target = outgoing ? _adjacency.Outgoing : _adjacency.Incoming;
            if (!target.TryGetValue(ownerNodeId, out var list))
            {
                list = new List<ShardEdgeRef>();
                target[ownerNodeId] = list;
            }

            list.Add(new ShardEdgeRef
            {
                RelId = relId,
                RelType = relType,
                NeighborNodeId = neighborNodeId,
                Direction = direction
            });
        }

        private bool NodePassesFilter(string tableName)
            => Options.NodeTables.Count == 0 || Options.NodeTables.Contains(tableName, _comparer);

        private bool TryReadNodeProperties(string tableName, object nodeId, out Dictionary<string, object>? props)
        {
            props = null;
            if (!_database.NodeTables.TryGetValue(tableName, out var table))
                return false;

            return _tx is null
                ? table.TryGetProperties(nodeId, out props)
                : table.TryGetProperties(_tx, nodeId, out props);
        }

        private bool NodeExists(NodeTableData table, object nodeId)
            => _tx is null
                ? table.TryGetProperties(nodeId, out _)
                : table.TryGetProperties(_tx, nodeId, out _);

        private static Dictionary<string, object?> ToNullableDictionary(Dictionary<string, object> props)
            => props.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);

        private static string NormalizeRequestedNodeId(object nodeId)
            => nodeId switch
            {
                string s => s,
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => nodeId.ToString() ?? string.Empty
            };

        private static string BuildRelId(
            string relType,
            string fromTable,
            string toTable,
            string sourceExternalId,
            string targetExternalId,
            IReadOnlyDictionary<string, object?> properties)
        {
            var propertyJson = JsonSerializer.Serialize(
                properties.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));
            var fingerprintInput =
                $"{relType}\n{fromTable}\n{toTable}\n{sourceExternalId}\n{targetExternalId}\n{propertyJson}";
            var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput)))
                .ToLowerInvariant();
            return $"rel:{fingerprint}";
        }

        private static string BuildGraphVersionToken(ulong catalogVersion) => $"catalog-v{catalogVersion}";

        private static string BuildExtractionPolicy(GraphShardExtractionOptions options)
        {
            var parts = new List<string>
            {
                $"depth={options.MaxDepth?.ToString() ?? "*"}",
                $"nodes={options.MaxNodes?.ToString() ?? "*"}",
                $"edges={options.MaxEdges?.ToString() ?? "*"}",
                $"outgoing={options.IncludeOutgoing.ToString().ToLowerInvariant()}",
                $"incoming={options.IncludeIncoming.ToString().ToLowerInvariant()}",
                $"adjacency={options.IncludeAdjacency.ToString().ToLowerInvariant()}",
                $"stopAtBoundary={options.StopAtBoundary.ToString().ToLowerInvariant()}"
            };

            if (options.RelTypes.Count > 0)
                parts.Add($"relTypes={string.Join(",", options.RelTypes.OrderBy(x => x, StringComparer.Ordinal))}");
            if (options.NodeTables.Count > 0)
                parts.Add($"nodeTables={string.Join(",", options.NodeTables.OrderBy(x => x, StringComparer.Ordinal))}");

            return string.Join(";", parts);
        }
    }

    private sealed record ExtractedNode(string TableName, object NodeId, string ExternalId, int Depth);
}
