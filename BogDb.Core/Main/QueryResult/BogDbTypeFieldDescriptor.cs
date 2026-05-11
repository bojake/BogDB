namespace BogDb.Core.Main.QueryResult;

/// <summary>
/// Host-facing descriptor for a named nested field inside a STRUCT-like value.
/// </summary>
public readonly record struct BogDbTypeFieldDescriptor(
    string Name,
    BogDbLogicalType LogicalType);
