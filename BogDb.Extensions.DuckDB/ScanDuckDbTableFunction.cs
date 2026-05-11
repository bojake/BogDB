using System;
using System.Collections.Generic;
using System.Data.Common;
using DuckDB.NET.Data;
using BogDb.Core.Extension;

namespace BogDb.Extensions.DuckDB;

/// <summary>
/// Table function "scan_duckdb" — scans a table from a DuckDB database file.
///
/// Argument convention:
///   args[0] = "path/to/db.duckdb"           -> scans the first user table
///   args[0] = "path/to/db.duckdb|tablename" -> scans the named table
///
/// Usage via query:
///   LOAD FROM 'sample.duckdb|people' RETURN *
///   CALL scan_duckdb('sample.duckdb|people') RETURN *
/// </summary>
public sealed class ScanDuckDbTableFunction : ITableFunction
{
    public string Name => "scan_duckdb";

    public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not string source)
            throw new ArgumentException("scan_duckdb requires a string path argument, optionally with '|tablename' suffix.");

        var (dbPath, tableName) = ParseSource(source);

        using var conn = Open(dbPath);

        if (string.IsNullOrWhiteSpace(tableName))
            tableName = GetFirstUserTable(conn)
                ?? throw new InvalidOperationException($"No user tables found in DuckDB database '{dbPath}'.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{tableName}\"";

        using var reader = cmd.ExecuteReader();
        return ReadAllRows(reader);
    }

    internal static (string dbPath, string tableName) ParseSource(string source)
    {
        var idx = source.LastIndexOf('|');
        return idx >= 0
            ? (source[..idx], source[(idx + 1)..])
            : (source, string.Empty);
    }

    internal static string? GetFirstUserTable(DuckDBConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema NOT IN ('information_schema', 'pg_catalog')
              AND table_type = 'BASE TABLE'
            ORDER BY table_schema, table_name
            LIMIT 1
            """;
        return cmd.ExecuteScalar() as string;
    }

    internal static DuckDBConnection Open(string dbPath)
    {
        var conn = new DuckDBConnection($"DataSource={dbPath}");
        conn.Open();
        return conn;
    }

    internal static List<Dictionary<string, object?>> ReadAllRows(DbDataReader reader)
    {
        var columns = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            columns[i] = reader.GetName(i);

        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Length; i++)
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }
}
