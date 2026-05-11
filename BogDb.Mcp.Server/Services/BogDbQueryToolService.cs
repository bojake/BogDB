using System.Text.Json;
using BogDb.Core.Main;
using BogDb.Core.Main.QueryResult;

namespace BogDb.Mcp.Server.Services;

public sealed class BogDbQueryToolService
{
    private const int DefaultRowLimit = 100;
    private const int MaxRowLimit = 1000;
    private const int DefaultTimeoutMs = 10_000;

    public async Task<QueryToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var cypher = JsonArgumentReader.GetRequiredString(arguments, "cypher");
        var rowLimit = Math.Clamp(JsonArgumentReader.GetOptionalInt32(arguments, "rowLimit") ?? DefaultRowLimit, 1, MaxRowLimit);
        var timeoutMs = Math.Max(1, JsonArgumentReader.GetOptionalInt32(arguments, "timeoutMs") ?? DefaultTimeoutMs);
        var parameters = JsonArgumentReader.GetOptionalObject(arguments, "parameters");

        return await ExecuteAsync(databasePath, cypher, parameters, rowLimit, timeoutMs, cancellationToken);
    }

    public async Task<QueryToolResult> ExecuteAsync(
        string databasePath,
        string cypher,
        IReadOnlyDictionary<string, object?>? parameters,
        int rowLimit,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        rowLimit = Math.Clamp(rowLimit, 1, MaxRowLimit);
        timeoutMs = Math.Max(1, timeoutMs);

        ReadOnlyQueryGuard.EnsureReadOnly(cypher);

        using var database = BogDatabase.Open(databasePath, new BogDatabaseOptions().WithReadOnly());
        using var connection = new BogConnection(database);

        var queryTask = Task.Run(() => ExecuteQuery(connection, cypher, parameters, rowLimit), cancellationToken);
        var completedTask = await Task.WhenAny(queryTask, Task.Delay(timeoutMs, cancellationToken));
        if (completedTask != queryTask)
            throw new TimeoutException($"Query exceeded timeout of {timeoutMs} ms.");

        return await queryTask;
    }

    private static QueryToolResult ExecuteQuery(
        BogConnection connection,
        string cypher,
        IReadOnlyDictionary<string, object?>? parameters,
        int rowLimit)
    {
        var startedAt = DateTime.UtcNow;
        var result = connection.Query(cypher, parameters);
        var elapsedMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;

        if (!result.IsSuccess)
        {
            return new QueryToolResult(
                Success: false,
                Columns: Array.Empty<string>(),
                Rows: Array.Empty<IReadOnlyDictionary<string, object?>>(),
                RowCount: 0,
                Truncated: false,
                ElapsedMs: elapsedMs,
                Error: result.ErrorMessage);
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (result.HasNext() && rows.Count < rowLimit)
        {
            var row = result.GetNext();
            rows.Add(NormalizeRow(row));
        }

        return new QueryToolResult(
            Success: true,
            Columns: result.ColumnNames.ToArray(),
            Rows: rows,
            RowCount: rows.Count,
            Truncated: result.HasNext(),
            ElapsedMs: elapsedMs,
            Error: null);
    }

    private static IReadOnlyDictionary<string, object?> NormalizeRow(BogRow row)
    {
        var raw = row.GetAsDictionary();
        var normalized = new Dictionary<string, object?>(raw.Count, StringComparer.Ordinal);
        foreach (var (key, value) in raw)
            normalized[key] = value;
        return normalized;
    }
}

public sealed record QueryToolResult(
    bool Success,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    int RowCount,
    bool Truncated,
    long ElapsedMs,
    string? Error);
