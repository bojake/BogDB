using BogDb.Core.Common;

namespace BogDb.Core.Parser;

/// <summary>
/// AST node for COPY (<query>) TO 'file.csv'.
/// </summary>
public sealed class CopyTo : Statement
{
    public RegularQuery Query { get; }
    public string FilePath { get; }

    public CopyTo(RegularQuery query, string filePath)
        : base(StatementType.COPY_TO)
    {
        Query = query;
        FilePath = filePath;
    }
}
