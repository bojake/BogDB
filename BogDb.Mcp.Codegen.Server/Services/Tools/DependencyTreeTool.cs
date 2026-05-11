using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_dependency_tree — Transitive DEPENDS_ON traversal for package dependencies.
/// </summary>
public static class DependencyTreeTool
{
    public const string Name = "codegen_dependency_tree";
    public const string Description =
        "Explore the transitive dependency tree of a package. " +
        "Answers: 'What does @acme/core pull in?' with configurable depth.";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            packageName = new { type = "string", description = "Package name to start from" },
            depth       = new { type = "integer", description = "Max traversal depth (default 3)", minimum = 1, maximum = 10 },
        },
        required = new[] { "packageName" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var packageName = GetString(arguments, "packageName");
        var depth       = GetOptionalInt(arguments, "depth") ?? 3;

        var cypher = $@"
            MATCH (root:Package)
            WHERE root.name = '{Escape(packageName)}'
            WITH root LIMIT 1
            MATCH (root)-[:DEPENDS_ON*1..{depth}]->(dep:Package)
            RETURN DISTINCT dep.id AS id, dep.name AS name, dep.ecosystem AS ecosystem,
                   dep.version AS version
            ORDER BY dep.name
            LIMIT 200";

        var result = conn.Query(cypher);
        if (!result.IsSuccess)
            return new { success = false, error = result.ErrorMessage, dependencies = Array.Empty<object>() };

        var deps = new List<object>();
        while (result.HasNext())
        {
            var row = result.GetNext().GetAsDictionary();
            deps.Add(new
            {
                id        = row.GetValueOrDefault("id")?.ToString(),
                name      = row.GetValueOrDefault("name")?.ToString(),
                ecosystem = row.GetValueOrDefault("ecosystem")?.ToString(),
                version   = row.GetValueOrDefault("version")?.ToString(),
            });
        }

        // Also get direct dependencies with edge info
        var directCypher = $@"
            MATCH (root:Package)-[d:DEPENDS_ON]->(dep:Package)
            WHERE root.name = '{Escape(packageName)}'
            RETURN dep.name AS name, dep.version AS version,
                   d.versionConstraint AS constraint, d.scope AS scope
            ORDER BY dep.name";

        var directResult = conn.Query(directCypher);
        var directDeps = new List<object>();
        if (directResult.IsSuccess)
        {
            while (directResult.HasNext())
            {
                var row = directResult.GetNext().GetAsDictionary();
                directDeps.Add(new
                {
                    name       = row.GetValueOrDefault("name")?.ToString(),
                    version    = row.GetValueOrDefault("version")?.ToString(),
                    constraint = row.GetValueOrDefault("constraint")?.ToString(),
                    scope      = row.GetValueOrDefault("scope")?.ToString(),
                });
            }
        }

        return new
        {
            success = true,
            rootPackage = packageName,
            depth,
            directDependencies = directDeps,
            transitiveDependencies = deps,
            totalCount = deps.Count,
        };
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static int? GetOptionalInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
