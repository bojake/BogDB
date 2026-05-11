using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Extension;

public static class ExternalIndexServiceNames
{
    public const string Provider = "externalindex.provider";
}

public sealed record ExternalIndexLookup(
    string IndexKind,
    string TableName,
    string PropertyName,
    object? LookupKey);

/// <summary>
/// Database-owned hook used by runtime-loaded extensions to bind planner
/// predicates into extension-backed index scans and resolve candidate row
/// offsets for those scans during execution.
/// </summary>
public interface IExternalIndexProvider
{
    bool TryBindPredicate(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        out ExternalIndexLookup lookup);

    IReadOnlyList<long> LookupCandidateNodeOffsets(
        ExternalIndexLookup lookup,
        object? resolvedLookupKey,
        BogDb.Core.Processor.ExecutionContext context);
}
