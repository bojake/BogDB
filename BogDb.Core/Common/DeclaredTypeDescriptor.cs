using System;

namespace BogDb.Core.Common;

/// <summary>
/// Structured metadata for declared schema types such as FLOAT[] or INT64[][].
/// Runtime execution still lowers nested list forms to LogicalTypeID.LIST, but
/// the declared shape is preserved here for validation/coercion/persistence work.
/// </summary>
public sealed record DeclaredTypeDescriptor(
    string DeclaredType,
    LogicalTypeID RuntimeType,
    LogicalTypeID LeafType,
    int ListDepth)
{
    public bool IsNestedList => ListDepth > 0;

    public static DeclaredTypeDescriptor Parse(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new InvalidOperationException("Cannot parse empty datatype.");

        var trimmed = typeName.Trim();
        var remaining = trimmed;
        var listDepth = 0;

        while (remaining.EndsWith("[]", StringComparison.Ordinal))
        {
            listDepth++;
            remaining = remaining[..^2].TrimEnd();
        }

        if (!Enum.TryParse<LogicalTypeID>(remaining, true, out var leafType))
            throw new InvalidOperationException($"Cannot parse datatype: {typeName}");

        return new DeclaredTypeDescriptor(
            DeclaredType: trimmed,
            RuntimeType: listDepth > 0 ? LogicalTypeID.LIST : leafType,
            LeafType: leafType,
            ListDepth: listDepth);
    }
}
