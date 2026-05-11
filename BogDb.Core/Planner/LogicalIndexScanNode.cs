using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Planner;

/// <summary>
/// Logical operator for an index-based node scan.
/// Created by the planner when an equality predicate exists on an indexed property.
/// C++ parity: src/planner/operator/logical_index_scan_node.h
/// </summary>
public sealed class LogicalIndexScanNode : LogicalOperator
{
    /// <summary>Node table to scan (e.g. "Person").</summary>
    public string TableName { get; }

    /// <summary>Indexed property name (e.g. "id").</summary>
    public string PropertyName { get; }

    /// <summary>
    /// The equality value to look up, or the prefix string for STARTS WITH scans.
    /// This is usually a normalized literal, but may also be a bound expression
    /// such as a parameter that can be evaluated at execution time.
    /// </summary>
    public object? LookupKey { get; }

    /// <summary>Cypher binding variable (e.g. "n").</summary>
    public string VariableName { get; }

    /// <summary>When true, the scan uses prefix matching (STARTS WITH) instead of exact equality.</summary>
    public bool IsPrefixScan { get; }

    public LogicalIndexScanNode(
        string tableName,
        string propertyName,
        object? lookupKey,
        string variableName,
        bool isPrefixScan = false)
        : base(LogicalOperatorType.LOGICAL_INDEX_SCAN_NODE)
    {
        TableName    = tableName;
        PropertyName = propertyName;
        LookupKey    = lookupKey;
        VariableName = variableName;
        IsPrefixScan = isPrefixScan;
    }

    public override string GetExpressionsForPrinting()
        => IsPrefixScan
            ? $"INDEX_PREFIX_SCAN {TableName}.{PropertyName} STARTS WITH {LookupKey} AS {VariableName}"
            : $"INDEX_SCAN {TableName}.{PropertyName} = {LookupKey} AS {VariableName}";
}
