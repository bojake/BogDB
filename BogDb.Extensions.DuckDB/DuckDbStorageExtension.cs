using System;
using System.Collections.Generic;
using System.Linq;
using DuckDB.NET.Data;
using BogDb.Core.Extension;
using BogDb.Core.Main;

namespace BogDb.Extensions.DuckDB;

public sealed class DuckDbStorageExtension : IStorageExtension
{
    public string Name => "duckdb";

    public bool CanHandle(string dbType)
        => string.Equals(dbType, "duckdb", StringComparison.OrdinalIgnoreCase);

    public AttachedDatabaseHandle Attach(
        BogDatabase database,
        string alias,
        string path,
        IReadOnlyDictionary<string, object?> options)
    {
        using var conn = Open(path);
        var tables = DiscoverTables(conn);
        return new AttachedDatabaseHandle(alias, "duckdb", path, Name, tables);
    }

    public IEnumerable<Dictionary<string, object?>> Scan(
        AttachedDatabaseHandle attachedDatabase,
        string? tableName)
    {
        using var conn = Open(attachedDatabase.Path);
        var resolvedTable = tableName;
        if (string.IsNullOrWhiteSpace(resolvedTable))
            resolvedTable = attachedDatabase.Tables.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(resolvedTable))
            throw new InvalidOperationException($"No user tables found in DuckDB database '{attachedDatabase.Path}'.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{resolvedTable}\"";
        using var reader = cmd.ExecuteReader();
        return ScanDuckDbTableFunction.ReadAllRows(reader);
    }

    private static DuckDBConnection Open(string path)
    {
        var conn = new DuckDBConnection($"DataSource={path}");
        conn.Open();
        return conn;
    }

    private static IReadOnlyDictionary<string, AttachedTableInfo> DiscoverTables(DuckDBConnection conn)
    {
        var tables = new Dictionary<string, AttachedTableInfo>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT table_name, column_name, data_type
            FROM information_schema.columns
            WHERE table_schema NOT IN ('information_schema', 'pg_catalog')
            ORDER BY table_name, ordinal_position
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var tableName = reader.GetString(0);
            var columnName = reader.GetString(1);
            var dataType = reader.GetString(2);
            if (!tables.TryGetValue(tableName, out var table))
            {
                table = new AttachedTableInfo(tableName, new List<(string Name, string Type)>());
                tables[tableName] = table;
            }

            ((List<(string Name, string Type)>)table.Columns).Add((columnName, dataType));
        }

        return tables;
    }
}
