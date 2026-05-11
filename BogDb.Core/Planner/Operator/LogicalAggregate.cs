using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical operator that computes aggregate functions over a child plan.
/// <para>
/// When <see cref="KeyItems"/> is non-empty the aggregation is grouped —
/// one output row per distinct key combination (implicit Cypher GROUP BY).
/// When <see cref="KeyItems"/> is empty the aggregation is global (single result row).
/// </para>
/// C++ parity: LOGICAL_AGGREGATE in bogdb-cpp.
/// </summary>
public sealed class LogicalAggregate : LogicalOperator
{
    private readonly List<BoundProjectionItem> _allProjectionItems;
    private List<BoundProjectionItem> _keyItems;
    private List<BoundProjectionItem> _dependentKeyItems = new();

    /// <summary>The original full projection list (key + aggregate items, in declaration order).</summary>
    public IReadOnlyList<BoundProjectionItem> ProjectionItems => _allProjectionItems;

    /// <summary>Non-aggregate items that form the implicit GROUP BY key.</summary>
    public IReadOnlyList<BoundProjectionItem> KeyItems => _keyItems;

    /// <summary>Items containing aggregate function calls.</summary>
    public IReadOnlyList<BoundProjectionItem> AggregateItems { get; }

    /// <summary>
    /// Keys moved here by <see cref="BogDb.Core.Optimizer.AggKeyDependencyRule"/> when
    /// they are functionally determined by a primary-key member of the same variable.
    /// The physical aggregate carries these as payload columns rather than hash dimensions.
    /// </summary>
    public IReadOnlyList<BoundProjectionItem> DependentKeyItems => _dependentKeyItems;

    /// <summary>True when there is at least one group key (implicit GROUP BY).</summary>
    public bool HasGroupBy => _keyItems.Count > 0;

    public LogicalAggregate(
        IReadOnlyList<BoundProjectionItem> allItems,
        IReadOnlyList<BoundProjectionItem> keyItems,
        IReadOnlyList<BoundProjectionItem> aggregateItems,
        LogicalOperator child)
        : base(LogicalOperatorType.LOGICAL_AGGREGATE, child)
    {
        _allProjectionItems = new List<BoundProjectionItem>(allItems);
        _keyItems           = new List<BoundProjectionItem>(keyItems);
        AggregateItems      = aggregateItems;
    }

    /// <summary>
    /// Called by <see cref="BogDb.Core.Optimizer.AggKeyDependencyRule"/> to move
    /// functionally-dependent group keys into <see cref="DependentKeyItems"/>.
    /// </summary>
    public void SetDependentKeys(
        List<BoundProjectionItem> mainKeys,
        List<BoundProjectionItem> dependentKeys)
    {
        _keyItems = mainKeys;
        _dependentKeyItems = dependentKeys;
    }

    public override string GetExpressionsForPrinting()
    {
        var keys = _keyItems.Count > 0
            ? $"GROUP BY [{string.Join(", ", _keyItems.Select(k => k.ColumnName))}] "
            : string.Empty;
        return keys + string.Join(", ", AggregateItems.Select(a => a.ColumnName));
    }
}
