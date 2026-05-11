using BogDb.Core.Common;

namespace BogDb.Core.Parser;

public sealed class UseDatabaseStatement : Statement
{
    public string DbName { get; }

    public UseDatabaseStatement(string dbName)
        : base(StatementType.USE_DATABASE)
    {
        DbName = dbName;
    }
}
