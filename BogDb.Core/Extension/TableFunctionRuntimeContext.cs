using BogDb.Core.Main;
using BogDb.Core.Transaction;

namespace BogDb.Core.Extension;

public static class TableFunctionRuntimeContext
{
    [ThreadStatic] private static BogDatabase? _database;
    [ThreadStatic] private static BogDb.Core.Transaction.Transaction? _transaction;

    public static BogDatabase? CurrentDatabase => _database;
    public static BogDb.Core.Transaction.Transaction? CurrentTransaction => _transaction;

    public static void Set(BogDatabase? database, BogDb.Core.Transaction.Transaction? transaction)
    {
        _database = database;
        _transaction = transaction;
    }

    public static void Clear()
    {
        _database = null;
        _transaction = null;
    }
}
