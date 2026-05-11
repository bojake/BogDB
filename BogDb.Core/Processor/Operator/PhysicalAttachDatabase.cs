using System;
using BogDb.Core.Binder;

namespace BogDb.Core.Processor.Operator;

public sealed class PhysicalAttachDatabase : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly BoundAttachDatabase _statement;
    private bool _executed;

    public PhysicalAttachDatabase(Main.BogDatabase database, BoundAttachDatabase statement, uint id)
        : base(PhysicalOperatorType.STANDALONE_CALL, id)
    {
        _database = database;
        _statement = statement;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_executed)
            return false;

        _executed = true;

        if (!_database.TryResolveStorageExtension(_statement.DbType, out var storageExtension))
        {
            throw new InvalidOperationException(
                $"No loaded extension can handle database type: {_statement.DbType}.\n" +
                $"Did you forget to load {_statement.DbType} extension?\n" +
                $"You can load it by: load extension {_statement.DbType};");
        }

        var handle = storageExtension.Attach(_database, _statement.Alias, _statement.Path, _statement.Options);
        _database.AddAttachedDatabase(handle);
        return false;
    }
}
