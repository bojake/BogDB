// Golden/GoldenBlessCommand.cs
// Re-generates all golden snapshot files from the current C# engine output.
// Run: dotnet test --filter "FullyQualifiedName~GoldenBlessCommand"

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace BogDb.Tests.Golden;

[Trait("Category", "GoldenBless")]
public class GoldenBlessCommand(ITestOutputHelper output)
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string CorpusDir = ResolveDirectoryOverride(
        "BOGDB_GOLDEN_CORPUS_DIR",
        Path.Combine(RepoRoot, "parity", "query-golden"));
    private static readonly string GoldenDir = Path.Combine(CorpusDir, "golden");
    private static readonly string ReportDir = ResolveDirectoryOverride(
        "BOGDB_GOLDEN_REPORT_DIR",
        Path.Combine(RepoRoot, "parity", "reports", "query-golden"));

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly (string Corpus, string Golden)[] Corpora =
    [
        ("corpus-basic.cypher", "basic.golden.json"),
        ("corpus-agg.cypher", "agg.golden.json"),
        ("corpus-recursive.cypher", "recursive.golden.json"),
        ("corpus-filter.cypher", "filter.golden.json"),
        ("corpus-projection.cypher", "projection.golden.json"),
        ("corpus-multitable.cypher", "multitable.golden.json"),
        ("corpus-multitable-advanced.cypher", "multitable-advanced.golden.json"),
        ("corpus-multitable-recursive.cypher", "multitable-recursive.golden.json"),
        ("corpus-multitable-undirected.cypher", "multitable-undirected.golden.json"),
        ("corpus-nulls.cypher", "nulls.golden.json"),
        ("corpus-types.cypher", "types.golden.json"),
        ("corpus-types-extended.cypher", "types-extended.golden.json"),
        ("corpus-types-nested.cypher", "types-nested.golden.json"),
        ("corpus-paths.cypher", "paths.golden.json"),
        ("corpus-functions.cypher", "functions.golden.json"),
        ("corpus-functions-advanced.cypher", "functions-advanced.golden.json"),
        ("corpus-functions-json-vector.cypher", "functions-json-vector.golden.json"),
        ("corpus-functions-vector-advanced.cypher", "functions-vector-advanced.golden.json"),
        ("corpus-temporal.cypher", "temporal.golden.json"),
        ("corpus-errors.cypher", "errors.golden.json"),
        ("corpus-transactional.cypher", "transactional.golden.json"),
        ("corpus-transactional-graph.cypher", "transactional-graph.golden.json"),
        ("corpus-path-endpoints.cypher", "path-endpoints.golden.json"),
        ("corpus-errors-extended.cypher", "errors-extended.golden.json"),
        ("corpus-string-functions.cypher", "string-functions.golden.json"),
        ("corpus-with-chaining.cypher", "with-chaining.golden.json"),
        ("corpus-merge.cypher", "merge.golden.json"),
        ("corpus-temporal-arithmetic.cypher", "temporal-arithmetic.golden.json"),
        ("corpus-blob-functions.cypher", "blob-functions.golden.json"),
        ("corpus-array-vector-functions.cypher", "array-vector-functions.golden.json"),
        ("corpus-cast-depth.cypher", "cast-depth.golden.json"),
        ("corpus-boolean-operators.cypher", "boolean-operators.golden.json"),
        ("corpus-regex-and-string-advanced.cypher", "regex-and-string-advanced.golden.json"),
        ("corpus-uuid-and-hash.cypher", "uuid-and-hash.golden.json"),
        ("corpus-scalar-macro.cypher", "scalar-macro.golden.json"),
        ("corpus-serial-sequence.cypher", "serial-sequence.golden.json"),
        ("corpus-subquery.cypher", "subquery.golden.json"),
        ("corpus-cyclic-patterns.cypher", "cyclic-patterns.golden.json"),
        ("corpus-copy-and-reader.cypher", "copy-and-reader.golden.json"),
        ("corpus-gds-shortest-path.cypher", "gds-shortest-path.golden.json"),
        ("corpus-gds-pagerank-wcc.cypher", "gds-pagerank-wcc.golden.json"),
        ("corpus-gds-variable-length.cypher", "gds-variable-length.golden.json"),
        ("corpus-nested-types-depth.cypher", "nested-types-depth.golden.json"),
        ("corpus-explain.cypher", "explain.golden.json"),
        ("corpus-hint.cypher", "hint.golden.json"),
        ("corpus-ldbc.cypher", "ldbc.golden.json"),
        ("corpus-lsqb.cypher", "lsqb.golden.json"),
        ("corpus-parquet.cypher", "parquet.golden.json"),
        ("corpus-reader.cypher", "reader.golden.json"),
        ("corpus-rel-group.cypher", "rel-group.golden.json"),
        ("corpus-tck.cypher", "tck.golden.json"),
        ("corpus-user-defined-types.cypher", "user-defined-types.golden.json"),
    ];

    [Fact]
    public void BlessAll_RegeneratesAllGoldenFiles()
    {
        Directory.CreateDirectory(GoldenDir);
        Directory.CreateDirectory(ReportDir);

        var blessedAt = DateTime.UtcNow.ToString("O");
        var report = new StringBuilder();
        report.AppendLine("# Golden Bless Report");
        report.AppendLine($"Blessed at: {blessedAt}");
        report.AppendLine();

        var anySnapshotChanged = false;

        foreach (var (corpusFile, goldenFile) in Corpora)
        {
            var corpusPath = Path.Combine(CorpusDir, corpusFile);
            var goldenPath = Path.Combine(GoldenDir, goldenFile);

            output.WriteLine($"[bless] {corpusFile} -> {goldenFile}");

            var corpus = GoldenTestRunner.ParseCorpus(corpusPath);
            var results = GoldenTestRunner.RunCorpus(corpus);

            var changed = 0;
            var added = 0;
            GoldenCorpusResult? existing = null;
            if (File.Exists(goldenPath))
            {
                var existingJson = File.ReadAllText(goldenPath, Encoding.UTF8);
                existing = JsonSerializer.Deserialize<GoldenCorpusResult>(existingJson, JsonOpts)!;
                var existingMap = new Dictionary<string, GoldenQueryResult>();
                foreach (var q in existing.Queries)
                {
                    existingMap[q.Name] = q;
                }

                foreach (var result in results)
                {
                    if (!existingMap.TryGetValue(result.Name, out var previous))
                    {
                        added++;
                        continue;
                    }

                    if (GoldenTestRunner.Diff(result, previous).Count > 0)
                    {
                        changed++;
                    }
                }
            }
            else
            {
                added = results.Count;
            }

            if (existing is null || added > 0 || changed > 0)
            {
                var snapshot = new GoldenCorpusResult
                {
                    Corpus = corpusFile,
                    BlessedAt = blessedAt,
                    Queries = results,
                };

                var json = JsonSerializer.Serialize(snapshot, JsonOpts);
                File.WriteAllText(goldenPath, json, Encoding.UTF8);
                anySnapshotChanged = true;
            }

            var status = added > 0 || changed > 0
                ? $"{added} added, {changed} changed"
                : "no changes";

            output.WriteLine($"  -> {results.Count} queries captured. {status}");
            report.AppendLine($"## {corpusFile}");
            report.AppendLine($"- Queries: {results.Count}");
            report.AppendLine($"- New: {added}, Changed: {changed}");
            report.AppendLine();
        }

        var reportPath = Path.Combine(ReportDir, "bless-report.md");
        if (anySnapshotChanged || !File.Exists(reportPath))
        {
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
            output.WriteLine($"[bless] Report written to {reportPath}");
        }
        else
        {
            output.WriteLine("[bless] No golden snapshot changes detected; report left unchanged.");
        }

        Assert.True(true);
    }

    private static string ResolveDirectoryOverride(string envVarName, string defaultPath)
    {
        var overridePath = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrWhiteSpace(overridePath))
        {
            return defaultPath;
        }

        return Path.IsPathRooted(overridePath)
            ? overridePath
            : Path.GetFullPath(Path.Combine(RepoRoot, overridePath));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BogDb.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repo root (BogDb.slnx not found).");
    }
}
