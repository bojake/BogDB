namespace BogDb.Core.Main.QueryResult;

/// <summary>
/// Host-facing descriptor for a query result column.
/// </summary>
public readonly record struct BogDbColumnDescriptor(
    string Name,
    int Ordinal,
    BogDbLogicalType LogicalType);
