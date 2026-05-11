using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_file_context — Get the full graph context for a file:
/// its module, package, repo, defined symbols, and ownership.
/// </summary>
public static class FileContextTool
{
    public const string Name = "codegen_file_context";
    public const string Description =
        "Get full context for a source file: its parent module/package/repo, all symbols defined in it, " +
        "and ownership information. The agent's primary tool for understanding a file's place in the codebase.";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            filePath = new { type = "string", description = "File path (full or partial)" },
            repo     = new { type = "string", description = "Repo name to disambiguate" },
        },
        required = new[] { "filePath" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var filePath = GetString(arguments, "filePath");
        var repo     = GetOptionalString(arguments, "repo");

        // 1. File metadata + hierarchy
        var hierarchyCypher = $@"
            MATCH (r:Repo)-[:CONTAINS_PACKAGE]->(pkg:Package)-[:CONTAINS_MODULE]->(mod:Module)-[:CONTAINS_FILE]->(f:File)
            WHERE f.path CONTAINS '{Escape(filePath)}'
            {(string.IsNullOrEmpty(repo) ? "" : $"AND r.name = '{Escape(repo)}'")}
            RETURN f.id AS fileId, f.path AS path, f.language AS language, f.lineCount AS lineCount,
                   f.hash AS hash, f.lastModified AS lastModified,
                   mod.qualifiedName AS moduleName, pkg.name AS packageName, r.name AS repoName
            LIMIT 1";

        var hierarchyResult = conn.Query(hierarchyCypher);
        object? fileInfo = null;
        string? fileId = null;

        if (hierarchyResult.IsSuccess && hierarchyResult.HasNext())
        {
            var row = hierarchyResult.GetNext().GetAsDictionary();
            fileId = row.GetValueOrDefault("fileId")?.ToString();
            fileInfo = new
            {
                path         = row.GetValueOrDefault("path")?.ToString(),
                language     = row.GetValueOrDefault("language")?.ToString(),
                lineCount    = row.GetValueOrDefault("lineCount"),
                hash         = row.GetValueOrDefault("hash")?.ToString(),
                lastModified = row.GetValueOrDefault("lastModified")?.ToString(),
                module       = row.GetValueOrDefault("moduleName")?.ToString(),
                package      = row.GetValueOrDefault("packageName")?.ToString(),
                repo         = row.GetValueOrDefault("repoName")?.ToString(),
            };
        }

        if (fileInfo == null)
            return new { success = false, error = $"File not found: {filePath}" };

        // 2. Symbols defined in this file
        var symbolsCypher = $@"
            MATCH (f:File)-[:DEFINES_SYMBOL]->(s:Symbol)
            WHERE f.path CONTAINS '{Escape(filePath)}'
            RETURN s.name AS name, s.qualifiedName AS qualifiedName, s.kind AS kind,
                   s.signature AS signature, s.startLine AS startLine, s.endLine AS endLine
            ORDER BY s.startLine";

        var symbolResult = conn.Query(symbolsCypher);
        var symbols = new List<object>();
        if (symbolResult.IsSuccess)
        {
            while (symbolResult.HasNext())
            {
                var row = symbolResult.GetNext().GetAsDictionary();
                symbols.Add(new
                {
                    name          = row.GetValueOrDefault("name")?.ToString(),
                    qualifiedName = row.GetValueOrDefault("qualifiedName")?.ToString(),
                    kind          = row.GetValueOrDefault("kind")?.ToString(),
                    signature     = row.GetValueOrDefault("signature")?.ToString(),
                    startLine     = row.GetValueOrDefault("startLine"),
                    endLine       = row.GetValueOrDefault("endLine"),
                });
            }
        }

        // 3. Ownership
        var ownerCypher = $@"
            MATCH (r:Repo)-[:CONTAINS_PACKAGE]->(:Package)-[:CONTAINS_MODULE]->(:Module)-[:CONTAINS_FILE]->(f:File)
            WHERE f.path CONTAINS '{Escape(filePath)}'
            MATCH (r)-[:REPO_OWNED_BY]->(o:Owner)
            RETURN o.team AS team, o.org AS org, o.contactChannel AS contactChannel
            LIMIT 5";

        var ownerResult = conn.Query(ownerCypher);
        var owners = new List<object>();
        if (ownerResult.IsSuccess)
        {
            while (ownerResult.HasNext())
            {
                var row = ownerResult.GetNext().GetAsDictionary();
                owners.Add(new
                {
                    team           = row.GetValueOrDefault("team")?.ToString(),
                    org            = row.GetValueOrDefault("org")?.ToString(),
                    contactChannel = row.GetValueOrDefault("contactChannel")?.ToString(),
                });
            }
        }

        return new
        {
            success = true,
            file = fileInfo,
            symbolCount = symbols.Count,
            symbols,
            owners,
        };
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
