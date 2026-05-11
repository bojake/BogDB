using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_ownership — Look up the owner team for a repo, service, or package.
/// </summary>
public static class OwnershipTool
{
    public const string Name = "codegen_ownership";
    public const string Description =
        "Find the owner team, org, and contact channel for a given entity (repo, service, or package). " +
        "Answers: 'Who owns the payments service?'";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            entity     = new { type = "string", description = "Name of the entity to look up" },
            entityKind = new { type = "string", description = "Kind of entity: repo, service, or package" },
        },
        required = new[] { "entity", "entityKind" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var entity     = GetString(arguments, "entity");
        var entityKind = GetString(arguments, "entityKind").ToLowerInvariant();

        var (label, rel) = entityKind switch
        {
            "repo"    => ("Repo",    "REPO_OWNED_BY"),
            "service" => ("Service", "SERVICE_OWNED_BY"),
            "package" => ("Package", "PACKAGE_OWNED_BY"),
            _ => ("", ""),
        };

        if (string.IsNullOrEmpty(label))
            return new { success = false, error = $"Unsupported entityKind '{entityKind}'. Use repo, service, or package." };

        var cypher = $@"
            MATCH (e:{label})-[:{rel}]->(o:Owner)
            WHERE e.name = '{Escape(entity)}'
            RETURN e.name AS entityName, o.team AS team, o.org AS org, o.contactChannel AS contactChannel
            LIMIT 10";

        var result = conn.Query(cypher);
        if (!result.IsSuccess)
            return new { success = false, error = result.ErrorMessage };

        var owners = new List<object>();
        while (result.HasNext())
        {
            var row = result.GetNext().GetAsDictionary();
            owners.Add(new
            {
                entityName     = row.GetValueOrDefault("entityName")?.ToString(),
                team           = row.GetValueOrDefault("team")?.ToString(),
                org            = row.GetValueOrDefault("org")?.ToString(),
                contactChannel = row.GetValueOrDefault("contactChannel")?.ToString(),
            });
        }

        return new { success = true, entity, entityKind, count = owners.Count, owners };
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
