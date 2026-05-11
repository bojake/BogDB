using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_api_consumers — Find services/consumers that call a given API endpoint.
/// </summary>
public static class ApiConsumersTool
{
    public const string Name = "codegen_api_consumers";
    public const string Description =
        "Find all services and consumers that depend on a given API endpoint or service. " +
        "Answers: 'Who depends on POST /users?' or 'Who consumes the payments service?'";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            serviceName  = new { type = "string", description = "Service name to find consumers of" },
            endpointPath = new { type = "string", description = "API endpoint path (e.g. '/users')" },
        },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var serviceName  = GetOptionalString(arguments, "serviceName");
        var endpointPath = GetOptionalString(arguments, "endpointPath");

        if (string.IsNullOrEmpty(serviceName) && string.IsNullOrEmpty(endpointPath))
            return new { success = false, error = "At least one of serviceName or endpointPath is required." };

        string cypher;
        if (!string.IsNullOrEmpty(endpointPath))
        {
            cypher = $@"
                MATCH (c:Consumer)-[ca:CONSUMES_API]->(api:ApiEndpoint)
                WHERE api.path CONTAINS '{Escape(endpointPath)}'
                OPTIONAL MATCH (svc:Service)-[:EXPOSES_API]->(api)
                RETURN c.name AS consumerName, c.service AS consumerService,
                       c.integrationKind AS integrationKind, ca.via AS via,
                       api.method AS method, api.path AS path, api.version AS apiVersion,
                       svc.name AS producerService
                ORDER BY c.name
                LIMIT 100";
        }
        else
        {
            cypher = $@"
                MATCH (svc:Service)-[:EXPOSES_API]->(api:ApiEndpoint)
                WHERE svc.name = '{Escape(serviceName!)}'
                OPTIONAL MATCH (c:Consumer)-[ca:CONSUMES_API]->(api)
                RETURN c.name AS consumerName, c.service AS consumerService,
                       c.integrationKind AS integrationKind, ca.via AS via,
                       api.method AS method, api.path AS path, api.version AS apiVersion,
                       svc.name AS producerService
                ORDER BY api.path, c.name
                LIMIT 100";
        }

        var result = conn.Query(cypher);
        if (!result.IsSuccess)
            return new { success = false, error = result.ErrorMessage, consumers = Array.Empty<object>() };

        var consumers = new List<object>();
        while (result.HasNext())
        {
            var row = result.GetNext().GetAsDictionary();
            consumers.Add(new
            {
                consumerName    = row.GetValueOrDefault("consumerName")?.ToString(),
                consumerService = row.GetValueOrDefault("consumerService")?.ToString(),
                integrationKind = row.GetValueOrDefault("integrationKind")?.ToString(),
                via             = row.GetValueOrDefault("via")?.ToString(),
                apiMethod       = row.GetValueOrDefault("method")?.ToString(),
                apiPath         = row.GetValueOrDefault("path")?.ToString(),
                apiVersion      = row.GetValueOrDefault("apiVersion")?.ToString(),
                producerService = row.GetValueOrDefault("producerService")?.ToString(),
            });
        }

        return new { success = true, count = consumers.Count, consumers };
    }

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");
}
