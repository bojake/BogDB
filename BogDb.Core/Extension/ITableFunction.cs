namespace BogDb.Core.Extension;

/// <summary>
/// A table-producing function that extensions can register with the engine.
/// Equivalent to C++ TableFunction — returns rows as key-value dictionaries.
/// </summary>
public interface ITableFunction
{
    /// <summary>Canonical name used in queries (case-insensitive), e.g. "scan_json_array".</summary>
    string Name { get; }

    /// <summary>
    /// Optional schema declaration: ordered (columnName, typeName) pairs.
    /// Null means schema is inferred from row keys. Enables YIELD col1, col2 binding.
    /// </summary>
    IReadOnlyList<(string Name, string Type)>? Schema => null;

    /// <summary>
    /// Invoke the function with the provided arguments and yield result rows.
    /// Each row is a dictionary of column-name → nullable object value.
    /// </summary>
    System.Collections.Generic.IEnumerable<System.Collections.Generic.Dictionary<string, object?>> Invoke(
        System.Collections.Generic.IReadOnlyList<object?> args);
}
