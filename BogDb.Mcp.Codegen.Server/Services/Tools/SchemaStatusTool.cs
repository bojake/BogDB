using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_schema_status — Health check and status of the codegen graph.
/// </summary>
public static class SchemaStatusTool
{
    public const string Name = "codegen_schema_status";
    public const string Description =
        "Check the health and status of the codegen graph: schema version, table counts, " +
        "and whether the graph is populated. Use this before querying to confirm the graph is ready.";

    public static object InputSchema => new
    {
        type = "object",
        properties = new { },
    };

    public static object Execute(BogConnection conn, JsonElement _)
    {
        return CodegenSchemaService.GetSchemaStatus(conn);
    }
}
