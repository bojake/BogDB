// Golden/GoldenResultSet.cs
// Wire DTO for a single query's golden snapshot result.
// Serialized to/from JSON in parity/query-golden/golden/*.golden.json.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BogDb.Tests.Golden;

/// <summary>
/// Top-level container for one corpus file's golden snapshot.
/// One file = one <see cref="GoldenCorpusResult"/> containing N query results.
/// </summary>
public sealed class GoldenCorpusResult
{
    /// <summary>Corpus file name (e.g. "corpus-basic.cypher").</summary>
    [JsonPropertyName("corpus")]
    public string Corpus { get; init; } = "";

    /// <summary>BogDB version / git SHA when this snapshot was blessed.</summary>
    [JsonPropertyName("blessedAt")]
    public string BlessedAt { get; init; } = "";

    /// <summary>One entry per named QUERY in the corpus file.</summary>
    [JsonPropertyName("queries")]
    public List<GoldenQueryResult> Queries { get; init; } = [];
}

/// <summary>
/// Result of a single named query from the corpus.
/// </summary>
public sealed class GoldenQueryResult
{
    /// <summary>Name after "-- QUERY: " header.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>Exact Cypher text that was executed.</summary>
    [JsonPropertyName("cypher")]
    public string Cypher { get; init; } = "";

    /// <summary>True when the query succeeded (no error).</summary>
    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; init; }

    /// <summary>Error message when IsSuccess is false; null otherwise.</summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Column names in projection order.
    /// Preserved as-is (not sorted) so column-order regression is detectable.
    /// </summary>
    [JsonPropertyName("columnNames")]
    public List<string> ColumnNames { get; init; } = [];

    /// <summary>
    /// Row data. Each row is a list of stringified column values.
    /// Rows are canonically sorted (lexicographic across all column values)
    /// so unordered queries produce stable golden files.
    /// </summary>
    [JsonPropertyName("rows")]
    public List<List<string>> Rows { get; init; } = [];
}
