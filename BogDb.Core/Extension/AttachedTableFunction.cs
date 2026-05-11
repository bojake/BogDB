using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Main;

namespace BogDb.Core.Extension;

internal sealed class AttachedTableFunction : ITableFunction
{
    private readonly BogDatabase _database;
    private readonly string _alias;
    private readonly string _tableName;
    private readonly IReadOnlyList<(string Name, string Type)> _schema;

    public AttachedTableFunction(
        BogDatabase database,
        string alias,
        AttachedTableInfo tableInfo)
    {
        _database = database;
        _alias = alias;
        _tableName = tableInfo.Name;
        _schema = tableInfo.Columns;
    }

    public string Name => $"{_alias}.{_tableName}";

    public IReadOnlyList<(string Name, string Type)>? Schema => _schema;

    public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
    {
        if (!_database.TryGetAttachedDatabase(_alias, out var attachedDatabase))
            throw new InvalidOperationException($"Attached database '{_alias}' is not registered.");
        if (!_database.TryResolveStorageExtension(attachedDatabase.DbType, out var storageExtension))
            throw new InvalidOperationException(
                $"No storage extension is available for attached database type '{attachedDatabase.DbType}'.");

        return storageExtension.Scan(attachedDatabase, _tableName).ToList();
    }
}
