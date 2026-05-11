using BogDb.Core.Common;

namespace BogDb.Core.Parser;

public sealed class DetachDatabaseStatement : Statement
{
    public string DbName { get; }

    public DetachDatabaseStatement(string dbName)
        : base(StatementType.DETACH_DATABASE)
    {
        DbName = dbName;
    }
}
