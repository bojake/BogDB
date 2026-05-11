using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Binder;

/// <summary>
/// Bound COPY TO statement carrying the source query and export metadata.
/// </summary>
public sealed class BoundCopyTo : BoundStatement
{
    public BoundRegularQuery Query { get; }
    public string FilePath { get; }
    public IReadOnlyList<string> ColumnNames { get; }

    public BoundCopyTo(BoundRegularQuery query, string filePath, IReadOnlyList<string> columnNames)
        : base(StatementType.COPY_TO)
    {
        Query = query;
        FilePath = filePath;
        ColumnNames = columnNames;
    }
}
