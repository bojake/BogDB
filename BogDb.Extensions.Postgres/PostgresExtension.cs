using System;
using System.Collections.Generic;
using System.Data.Common;
using BogDb.Core.Extension;
using BogDb.Core.Main;
using Npgsql;

namespace BogDb.Extensions.Postgres;

/// <summary>
/// Postgres extension — C++ parity with bogdb-master/extension/postgres.
/// Provides scan_postgres table function and Postgres storage extension for ATTACH.
/// </summary>
public class PostgresExtension : IExtension
{
    public string Name => "postgres";

    public void Load(BogDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var scanPostgres = new ScanPostgresTableFunction();
        database.FunctionRegistry.Register(scanPostgres);
        database.StandaloneTableFunctionRegistry.Register(scanPostgres);
        database.RegisterStorageExtension("postgres", new PostgresStorageExtension());
    }

    // Kept for direct programmatic use outside the query path
    public NpgsqlConnection CreateConnection(string connectionString)
        => new NpgsqlConnection(connectionString);
}

/// <summary>
/// Table function "scan_postgres" — scans a table from a PostgreSQL database.
///
/// Argument convention:
///   args[0] = "Host=...;Database=..." connection string
///   args[1] = optional table name (default: first public table)
///
/// Usage via query:
///   CALL scan_postgres('Host=localhost;Database=mydb', 'users') RETURN *
/// </summary>
public sealed class ScanPostgresTableFunction : ITableFunction
{
    public string Name => "scan_postgres";

    public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not string connectionString)
            throw new ArgumentException("scan_postgres requires a connection string argument.");

        var tableName = args.Count > 1 ? args[1]?.ToString() : null;

        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();

        if (string.IsNullOrWhiteSpace(tableName))
            tableName = GetFirstPublicTable(conn)
                ?? throw new InvalidOperationException("No public tables found in the PostgreSQL database.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{tableName}\"";
        using var reader = cmd.ExecuteReader();
        return ReadAllRows(reader);
    }

    internal static string? GetFirstPublicTable(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name
            LIMIT 1
            """;
        return cmd.ExecuteScalar() as string;
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

/// <summary>
/// Postgres storage extension — enables ATTACH 'connstring' AS alias (dbtype := 'postgres').
/// Discovers tables and columns from the public schema.
/// </summary>
public sealed class PostgresStorageExtension : IStorageExtension
{
    public string Name => "postgres";

    public bool CanHandle(string dbType)
        => string.Equals(dbType, "postgres", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(dbType, "postgresql", StringComparison.OrdinalIgnoreCase);

    public AttachedDatabaseHandle Attach(
        BogDatabase database,
        string alias,
        string path,
        IReadOnlyDictionary<string, object?> options)
    {
        // 'path' is actually the connection string for Postgres
        var schema = "public";
        if (options.TryGetValue("schema", out var schemaVal) && schemaVal is string s)
            schema = s;

        using var conn = new NpgsqlConnection(path);
        conn.Open();
        var tables = DiscoverTables(conn, schema);
        return new AttachedDatabaseHandle(alias, "postgres", path, Name, tables);
    }

    public IEnumerable<Dictionary<string, object?>> Scan(
        AttachedDatabaseHandle attachedDatabase,
        string? tableName)
    {
        using var conn = new NpgsqlConnection(attachedDatabase.Path);
        conn.Open();

        var resolvedTable = tableName;
        if (string.IsNullOrWhiteSpace(resolvedTable))
            resolvedTable = attachedDatabase.Tables.Keys
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(resolvedTable))
            throw new InvalidOperationException("No tables found in the attached PostgreSQL database.");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{resolvedTable}\"";
        using var reader = cmd.ExecuteReader();
        return ScanPostgresTableFunction.ReadAllRows(reader);
    }

    private static IReadOnlyDictionary<string, AttachedTableInfo> DiscoverTables(
        NpgsqlConnection conn, string schema)
    {
        var tables = new Dictionary<string, AttachedTableInfo>(StringComparer.OrdinalIgnoreCase);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT table_name, column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = '{schema}'
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
            ((List<(string Name, string Type)>)table.Columns).Add((columnName, MapPgType(dataType)));
        }
        return tables;
    }

    private static string MapPgType(string pgType)
    {
        return pgType.ToLowerInvariant() switch
        {
            "integer" or "int4" => "INT32",
            "bigint" or "int8" => "INT64",
            "smallint" or "int2" => "INT16",
            "real" or "float4" => "FLOAT",
            "double precision" or "float8" => "DOUBLE",
            "boolean" or "bool" => "BOOL",
            "text" or "character varying" or "varchar" => "STRING",
            "date" => "DATE",
            "timestamp without time zone" or "timestamp with time zone" => "TIMESTAMP",
            "uuid" => "STRING",
            "bytea" => "BLOB",
            "json" or "jsonb" => "STRING",
            _ => "STRING"
        };
    }
}
