using System.Collections.Generic;
using BogDb.Core.Binder;
using BogDb.Core.GraphDataScience;

namespace BogDb.Core.Processor.Operator.Scan;

/// <summary>
/// Physical operator that scans the active nodes of a GDS frontier.
/// For each active node offset, materializes the node's properties from the
/// underlying node table and yields a tuple.
///
/// C++ parity: In C++, frontier scanning is embedded inside <c>GDSTask</c>.
/// BogDB exposes it as a standalone physical operator for pipeline visibility.
///
/// Lifecycle:
///   1. Seed the frontier (via algorithm setup or external assignment)
///   2. GetNextTuple() iterates active frontier offsets
///   3. For each offset, resolves the node's properties from the table
/// </summary>
public sealed class PhysicalScanFrontier : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly IReadOnlyList<string> _tableNames;
    private readonly string _variableName;
    private readonly GdsFrontier _frontier;

    private IEnumerator<ulong>? _offsetIterator;
    private int _currentTableIndex;

    public PhysicalScanFrontier(
        Main.BogDatabase database,
        IReadOnlyList<string> tableNames,
        string variableName,
        GdsFrontier frontier,
        uint id)
        : base(PhysicalOperatorType.SCAN_FRONTIER, id)
    {
        _database = database;
        _tableNames = tableNames;
        _variableName = variableName;
        _frontier = frontier;
    }

    /// <summary>
    /// Provides read access to the frontier for external algorithms to seed/modify.
    /// </summary>
    public GdsFrontier Frontier => _frontier;

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_offsetIterator == null)
        {
            _offsetIterator = _frontier.ActiveOffsets().GetEnumerator();
        }

        while (_offsetIterator.MoveNext())
        {
            var offset = _offsetIterator.Current;

            // Try to resolve the node from each table name until found.
            foreach (var tableName in _tableNames)
            {
                if (!_database.NodeTables.TryGetValue(tableName, out var table))
                    continue;

                if (!table.TryGetByOffset((long)offset, out var nodeId, out var props))
                    continue;

                if (props == null)
                    continue;

                var normalizedProps = _database.NormalizeNodePropertiesForRead(tableName, props);
                if (!normalizedProps.ContainsKey("id"))
                    normalizedProps["id"] = nodeId;

                context.CurrentNodeId = nodeId;
                context.CurrentNodeProperties = normalizedProps;
                context.CurrentVariableIds = new Dictionary<string, object> { [_variableName] = nodeId };
                context.CurrentVariableProperties = new Dictionary<string, Dictionary<string, object>>
                {
                    [_variableName] = normalizedProps
                };
                context.CurrentProjectionRow = null;
                return true;
            }

            // Offset not found in any table — skip (node may have been deleted)
        }

        return false;
    }
}
