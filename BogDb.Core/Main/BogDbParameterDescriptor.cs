using BogDb.Core.Main.QueryResult;

namespace BogDb.Core.Main;

/// <summary>
/// Host-facing descriptor for a prepared statement parameter.
/// </summary>
public readonly record struct BogDbParameterDescriptor(
    string Name,
    int Ordinal,
    bool IsBound,
    BogDbLogicalType ExpectedLogicalType,
    BogDbLogicalType LogicalType,
    object? Value);
