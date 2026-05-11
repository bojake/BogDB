using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BogDb.Core.Common.FileSystem;
using BogDb.Core.Extension;
using BogDb.Core.Main;

namespace BogDb.Extensions.Json;

/// <summary>
/// Table function that scans a JSON array file and yields one row per element.
/// Registered as "scan_json_array" by <see cref="JsonExtension.Load"/>.
/// Non-array root objects are yielded as a single row.
/// </summary>
public sealed class ScanJsonArrayTableFunction : ITableFunction
{
    private readonly BogDatabase _database;

    public ScanJsonArrayTableFunction(BogDatabase database)
    {
        _database = database;
    }

    public string Name => "scan_json_array";

    public IEnumerable<Dictionary<string, object?>> Invoke(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not string filePath)
            throw new ArgumentException("scan_json_array requires a single string path argument.");

        if (!TryOpenRead(filePath, out var stream))
            throw new FileNotFoundException($"JSON file not found: '{filePath}'", filePath);

        JsonNode? root;
        IEnumerable<Dictionary<string, object?>>? fallbackRows = null;
        try
        {
            using (stream)
            {
                root = JsonNode.Parse(stream);
            }
        }
        catch (JsonException)
        {
            root = null;
            fallbackRows = ParseNewlineDelimited(filePath).ToList();
        }

        if (fallbackRows != null)
        {
            foreach (var row in fallbackRows)
                yield return row;
            yield break;
        }

        if (root is JsonArray array)
        {
            foreach (var element in array)
                yield return JsonNodeToRow(element);
            yield break;
        }

        // Single root object — yield as one row
        yield return JsonNodeToRow(root);
    }

    private IEnumerable<Dictionary<string, object?>> ParseNewlineDelimited(string filePath)
    {
        using var stream = OpenReadRequired(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var node = JsonNode.Parse(line);
            yield return JsonNodeToRow(node);
        }
    }

    private Stream OpenReadRequired(string filePath)
    {
        if (TryOpenRead(filePath, out var stream))
            return stream;

        throw new FileNotFoundException($"JSON file not found: '{filePath}'", filePath);
    }

    private bool TryOpenRead(string filePath, out Stream stream)
    {
        stream = Stream.Null;
        var schemeSeparator = filePath.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator > 0)
        {
            var scheme = filePath[..schemeSeparator];
            if (_database.TryGetFileSystem(scheme, out var fileSystem))
            {
                using var fileInfo = fileSystem.OpenFile(filePath, FileFlags.Read);
                var buffer = new byte[checked((int)fileInfo.GetFileSize())];
                fileInfo.Read(buffer, 0);
                stream = new MemoryStream(buffer, writable: false);
                return true;
            }
        }

        if (!File.Exists(filePath))
            return false;

        stream = File.OpenRead(filePath);
        return true;
    }

    private static Dictionary<string, object?> JsonNodeToRow(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in obj)
                row[key] = JsonValueToClr(value);
            return row;
        }

        // Primitive or null — expose as single "_value" column
        return new Dictionary<string, object?> { ["_value"] = JsonValueToClr(node) };
    }

    private static object? JsonValueToClr(JsonNode? value) => value switch
    {
        null                                           => null,
        JsonValue v when v.TryGetValue<bool>(out var b)     => b,
        JsonValue v when v.TryGetValue<long>(out var l)     => l,
        JsonValue v when v.TryGetValue<double>(out var d)   => d,
        JsonValue v when v.TryGetValue<string>(out var s)   => s,
        JsonArray  arr                                  => arr.ToJsonString(),
        JsonObject jobj                                 => jobj.ToJsonString(),
        _                                               => value.ToJsonString()
    };
}
