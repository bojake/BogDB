using System.Collections.Generic;
using System.Text;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical Node property scanner hitting the Graph Storage indices.
/// Phase 8: carries TableName and bound PropertyExpression list.
/// </summary>
public sealed class LogicalScanNodeProperty : LogicalOperator
{
    public NodeExpression Node { get; }
    public string TableName { get; }
    public IReadOnlyList<PropertyExpression> Properties { get; }
    public IReadOnlyList<(string Key, Expression Value)> InlineProperties { get; }
    public Expression? InlinePropertyBag { get; }

    /// <summary>
    /// Predicate pushed down from a LogicalFilter by the FilterPushDownRule optimizer.
    /// When set, the physical scan evaluates this predicate during row enumeration,
    /// skipping non-matching rows before they reach the pipeline.
    /// </summary>
    public Expression? PushedPredicate { get; set; }

    /// <summary>Phase 8 full constructor.</summary>
    public LogicalScanNodeProperty(NodeExpression node, string tableName,
        IReadOnlyList<PropertyExpression> properties,
        IReadOnlyList<(string Key, Expression Value)>? inlineProperties = null,
        Expression? inlinePropertyBag = null)
        : base(LogicalOperatorType.LOGICAL_SCAN_NODE)
    {
        Node = node;
        TableName = tableName;
        Properties = properties;
        InlineProperties = inlineProperties ?? new List<(string Key, Expression Value)>();
        InlinePropertyBag = inlinePropertyBag;
    }

    /// <summary>Backward-compat constructor used by legacy callers.</summary>
    public LogicalScanNodeProperty(NodeExpression node)
        : this(node, string.Empty, new List<PropertyExpression>()) { }

    public override string GetExpressionsForPrinting()
        => $"SCAN {Node.VariableName} FROM {TableName}";
}
