using System;
using System.Collections.Generic;
using BogDb.Core.Extension;
using Microsoft.Data.Sqlite;

namespace BogDb.Extensions.SQLite;

/// <summary>
/// Table function "scan_sqlite" — scans a table from an SQLite database file.
///
/// Argument convention (mirrors C++ bogdb sqlite extension):
///   args[0] = "path/to/db.sqlite"              → scans the first user table
///   args[0] = "path/to/db.sqlite|tablename"    → scans the named table
///
/// Usage via query:
///   LOAD FROM 'chinook.sqlite|artists' RETURN *
/// </summary>
public sealed class ScanSqliteTableFunction : ITableFunction
{
    public string Name => "scan_sqlite";

    public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not string source)
            throw new ArgumentException("scan_sqlite requires a string path argument, optionally with '|tablename' suffix.");

        var (dbPath, tableName) = ParseSource(source);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        // Resolve table name if not specified
        if (string.IsNullOrEmpty(tableName))
            tableName = GetFirstUserTable(conn)
                ?? throw new InvalidOperationException($"No user tables found in SQLite database '{dbPath}'.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{tableName}\"";

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

    private static (string dbPath, string tableName) ParseSource(string source)
    {
        var idx = source.LastIndexOf('|');
        return idx >= 0
            ? (source[..idx], source[(idx + 1)..])
            : (source, string.Empty);
    }

    private static string? GetFirstUserTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name LIMIT 1";
        return cmd.ExecuteScalar() as string;
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
