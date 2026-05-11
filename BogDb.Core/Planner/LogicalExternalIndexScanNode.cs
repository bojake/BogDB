using BogDb.Core.Planner.Operator;
using BogDb.Core.Extension;

namespace BogDb.Core.Planner;

/// <summary>
/// Logical operator for an extension-backed index prefilter scan.
/// </summary>
public sealed class LogicalExternalIndexScanNode : LogicalOperator
{
    public string IndexKind { get; }
    public string TableName { get; }
    public string PropertyName { get; }
    public object? LookupKey { get; }
    public string VariableName { get; }

    public ExternalIndexLookup Lookup { get; }

    public LogicalExternalIndexScanNode(
        ExternalIndexLookup lookup,
        string variableName)
        : base(LogicalOperatorType.LOGICAL_EXTERNAL_INDEX_SCAN_NODE)
    {
        Lookup = lookup;
        IndexKind = lookup.IndexKind;
        TableName = lookup.TableName;
        PropertyName = lookup.PropertyName;
        LookupKey = lookup.LookupKey;
        VariableName = variableName;
    }

    public override string GetExpressionsForPrinting()
        => $"EXTERNAL_INDEX_SCAN[{IndexKind}] {TableName}.{PropertyName} = {LookupKey} AS {VariableName}";
}
