using BogDb.Core.Common;

namespace BogDb.Core.Extension;

/// <summary>
/// Database-owned definition for an extension option.
/// </summary>
public sealed record ExtensionOption(
    string Name,
    LogicalTypeID Type,
    object? DefaultValue,
    bool IsConfidential);
