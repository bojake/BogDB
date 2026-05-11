using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Extension;
using BogDb.Core.Main;
using Microsoft.Data.Sqlite;

namespace BogDb.Extensions.SQLite;

/// <summary>
/// SQLite extension — C++ parity with bogdb-master/extension/sqlite.
/// Provides scan_sqlite table function and SQLite storage extension for ATTACH.
/// </summary>
public class SQLiteExtension : IExtension
{
    public string Name => "sqlite";

    public void Load(BogDatabase database)
    {
        var scanSqlite = new ScanSqliteTableFunction();
        database.FunctionRegistry.Register(scanSqlite);
        database.StandaloneTableFunctionRegistry.Register(scanSqlite);
        database.RegisterStorageExtension("sqlite", new SqliteStorageExtension());
    }

    // Kept for direct programmatic use outside the query path
    public SqliteConnection CreateConnection(string connectionString)
        => new SqliteConnection(connectionString);
}

/// <summary>
/// SQLite storage extension — enables ATTACH 'path.sqlite' AS alias (dbtype := 'sqlite').
/// Discovers tables, columns, and supports scanning via ATTACH'd table projection.
/// </summary>
public sealed class SqliteStorageExtension : IStorageExtension
{
    public string Name => "sqlite";

    public bool CanHandle(string dbType)
        => string.Equals(dbType, "sqlite", StringComparison.OrdinalIgnoreCase);

    public AttachedDatabaseHandle Attach(
        BogDatabase database,
        string alias,
        string path,
        IReadOnlyDictionary<string, object?> options)
    {
        using var conn = Open(path);
        var tables = DiscoverTables(conn);
        return new AttachedDatabaseHandle(alias, "sqlite", path, Name, tables);
    }

    public IEnumerable<Dictionary<string, object?>> Scan(
        AttachedDatabaseHandle attachedDatabase,
        string? tableName)
    {
        using var conn = Open(attachedDatabase.Path);
        var resolvedTable = tableName;
        if (string.IsNullOrWhiteSpace(resolvedTable))
            resolvedTable = attachedDatabase.Tables.Keys
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(resolvedTable))
            throw new InvalidOperationException(
                $"No user tables found in SQLite database '{attachedDatabase.Path}'.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{resolvedTable}\"";
        using var reader = cmd.ExecuteReader();

        var columns = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            columns[i] = reader.GetName(i);

        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Length; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : MapValue(reader, i);
            rows.Add(row);
        }
        return rows;
    }

    private static SqliteConnection Open(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private static IReadOnlyDictionary<string, AttachedTableInfo> DiscoverTables(SqliteConnection conn)
    {
        var tables = new Dictionary<string, AttachedTableInfo>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var tableName = reader.GetString(0);
            var columns = DiscoverColumns(conn, tableName);
            tables[tableName] = new AttachedTableInfo(tableName, columns);
        }
        return tables;
    }

    private static List<(string Name, string Type)> DiscoverColumns(SqliteConnection conn, string tableName)
    {
        var columns = new List<(string, string)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var columnName = reader.GetString(1); // name
            var columnType = reader.IsDBNull(2) ? "TEXT" : reader.GetString(2); // type
            columns.Add((columnName, MapSqliteType(columnType)));
        }
        return columns;
    }

    private static string MapSqliteType(string sqliteType)
    {
        var upper = sqliteType.ToUpperInvariant();
        if (upper.Contains("INT")) return "INT64";
        if (upper.Contains("REAL") || upper.Contains("FLOAT") || upper.Contains("DOUBLE")) return "DOUBLE";
        if (upper.Contains("BOOL")) return "BOOL";
        if (upper.Contains("BLOB")) return "BLOB";
        return "STRING";
    }

    private static object? MapValue(SqliteDataReader reader, int ordinal)
    {
        return reader.GetFieldType(ordinal).FullName switch
        {
            "System.Int64"   => reader.GetInt64(ordinal),
            "System.Double"  => reader.GetDouble(ordinal),
            "System.Boolean" => reader.GetBoolean(ordinal),
            _                => reader.GetString(ordinal)
        };
    }
}
