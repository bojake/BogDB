using System.Text;
using System.Text.Json;

namespace BogDb.Core.Extraction;

/// <summary>
/// Canonical JSON serializer for extracted graph payloads.
/// </summary>
public static class GraphShardJson
{
    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(writeIndented: true);

    public static string Serialize(GraphShard shard, bool writeIndented = false)
    {
        ArgumentNullException.ThrowIfNull(shard);
        var normalized = GraphShardNormalizer.Normalize(shard);
        GraphShardValidator.Validate(normalized);
        return JsonSerializer.Serialize(normalized, writeIndented ? IndentedOptions : CompactOptions);
    }

    public static byte[] SerializeToUtf8Bytes(GraphShard shard, bool writeIndented = false)
    {
        ArgumentNullException.ThrowIfNull(shard);
        var normalized = GraphShardNormalizer.Normalize(shard);
        GraphShardValidator.Validate(normalized);
        return JsonSerializer.SerializeToUtf8Bytes(normalized, writeIndented ? IndentedOptions : CompactOptions);
    }

    public static void WriteToStream(Stream stream, GraphShard shard, bool writeIndented = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(shard);
        stream.Write(SerializeToUtf8Bytes(shard, writeIndented));
    }

    public static void WriteToFile(string path, GraphShard shard, bool writeIndented = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(shard);
        File.WriteAllBytes(path, SerializeToUtf8Bytes(shard, writeIndented));
    }

    public static GraphShard Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var shard = JsonSerializer.Deserialize<GraphShard>(json, CompactOptions)
            ?? throw new InvalidOperationException("Failed to deserialize GraphShard JSON payload.");
        var normalized = GraphShardNormalizer.Normalize(shard);
        GraphShardValidator.Validate(normalized);
        return normalized;
    }

    public static GraphShard Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        var shard = JsonSerializer.Deserialize<GraphShard>(utf8Json, CompactOptions)
            ?? throw new InvalidOperationException("Failed to deserialize GraphShard JSON payload.");
        var normalized = GraphShardNormalizer.Normalize(shard);
        GraphShardValidator.Validate(normalized);
        return normalized;
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
        => new()
        {
            WriteIndented = writeIndented,
            PropertyNamingPolicy = null,
            DictionaryKeyPolicy = null,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
}
