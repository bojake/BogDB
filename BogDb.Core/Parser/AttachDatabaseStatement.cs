using BogDb.Core.Common;

namespace BogDb.Core.Parser;

public sealed class AttachDatabaseStatement : Statement
{
    public string Path { get; }
    public string Alias { get; }
    public string DbType { get; }
    public IReadOnlyDictionary<string, object?> Options { get; }

    public AttachDatabaseStatement(
        string path,
        string alias,
        string dbType,
        IReadOnlyDictionary<string, object?> options)
        : base(StatementType.ATTACH_DATABASE)
    {
        Path = path;
        Alias = alias;
        DbType = dbType;
        Options = options;
    }
}
