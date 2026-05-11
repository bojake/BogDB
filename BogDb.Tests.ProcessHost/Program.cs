using System.Text.Json;
using BogDb.Core.Main;

if (args.Length == 0)
{
    Console.Error.WriteLine("No command provided.");
    return 1;
}

try
{
    switch (args[0])
    {
        case "writer-hold":
            return await RunWriterHoldAsync(args);
        case "readonly-query":
            return await RunReadOnlyQueryAsync(args);
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.");
            return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 1;
}

static async Task<int> RunWriterHoldAsync(string[] args)
{
    if (args.Length < 4)
        throw new ArgumentException("writer-hold requires: <dbPath> <readyFile> <releaseFile>");

    var dbPath = args[1];
    var readyFile = args[2];
    var releaseFile = args[3];

    using var db = BogDatabase.Open(dbPath);
    using var _ = new BogConnection(db);

    Directory.CreateDirectory(Path.GetDirectoryName(readyFile)!);
    await File.WriteAllTextAsync(readyFile, "ready");

    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (!File.Exists(releaseFile))
    {
        if (sw.ElapsedMilliseconds > 30000)
            throw new TimeoutException($"Timed out waiting for release file '{releaseFile}'.");

        await Task.Delay(100);
    }

    return 0;
}

static async Task<int> RunReadOnlyQueryAsync(string[] args)
{
    if (args.Length < 4)
        throw new ArgumentException("readonly-query requires: <dbPath> <resultFile> <readCommittedRecoveryState>");

    var dbPath = args[1];
    var resultFile = args[2];
    var readCommittedRecoveryState = bool.Parse(args[3]);
    var options = new BogDatabaseOptions()
        .WithReadOnly()
        .WithReadCommittedRecoveryState(readCommittedRecoveryState);

    using var db = BogDatabase.Open(dbPath, options);
    using var conn = new BogConnection(db);
    var result = conn.Query("MATCH (p:Person) RETURN p.id AS id, p.name AS name ORDER BY id");
    if (!result.IsSuccess)
        throw new InvalidOperationException(result.ErrorMessage);

    var rows = new List<Dictionary<string, object?>>();
    while (result.HasNext())
    {
        var row = result.GetNext().GetAsDictionary();
        rows.Add(new Dictionary<string, object?>(row, StringComparer.Ordinal));
    }

    Directory.CreateDirectory(Path.GetDirectoryName(resultFile)!);
    await File.WriteAllTextAsync(resultFile, JsonSerializer.Serialize(new
    {
        Columns = result.ColumnNames.ToArray(),
        Rows = rows,
    }));

    return 0;
}