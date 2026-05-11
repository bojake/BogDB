using System.Text.Json;
using BogDb.Core.Main;
using BogDb.Mcp.Codegen.Server.Services;
using BogDb.Mcp.Codegen.Server.Services.Ingestion;
using BogDb.Mcp.Codegen.Server.Services.Tools;
using BogDb.Mcp.Server;
using BogDb.Mcp.Server.Services;

namespace BogDb.Mcp.Codegen.Server;

/// <summary>
/// MCP server host that combines the base BogDb tools (bogdb_query, bogdb_schema,
/// bogdb_tables, bogdb_table_info) with codegen-specific semantic tools.
///
/// On first connection, initializes the codegen graph schema in the configured database.
/// </summary>
public sealed class CodegenMcpHost
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly BogDbQueryToolService _queryService = new();
    private readonly BogDbSchemaToolService _schemaService = new();

    /// <summary>Path to the persistent codegen database. Defaults to in-memory.</summary>
    private BogDatabase? _codegenDb;
    private BogConnection? _codegenConn;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var input  = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();

        // Initialize the codegen database (in-memory for now; can be disk-backed via env var)
        var dbPath = Environment.GetEnvironmentVariable("CODEGEN_DB_PATH");
        _codegenDb = string.IsNullOrEmpty(dbPath)
            ? BogDatabase.CreateInMemory()
            : BogDatabase.Open(dbPath);
        _codegenConn = new BogConnection(_codegenDb);

        CodegenSchemaService.EnsureSchema(_codegenConn);

        try
        {
            await RunAsync(input, output, cancellationToken);
        }
        finally
        {
            _codegenConn?.Dispose();
            _codegenDb?.Dispose();
        }
    }

    private async Task RunAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await McpServerHost.ReadMessageAsync(input, cancellationToken);
            if (message == null) break;

            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement))
                continue;

            var method = methodElement.GetString();
            if (string.IsNullOrWhiteSpace(method)) continue;

            if (!root.TryGetProperty("id", out var idElement))
            {
                // Notifications (no response needed)
                continue;
            }

            var id = idElement.Clone();

            try
            {
                object result = method switch
                {
                    "initialize"   => CreateInitializeResult(),
                    "ping"         => new { },
                    "tools/list"   => CreateToolsListResult(),
                    "tools/call"   => await HandleToolsCallAsync(root, cancellationToken),
                    _ => throw new InvalidOperationException($"Unsupported MCP method '{method}'.")
                };

                await McpServerHost.WriteResponseAsync(output, new
                {
                    jsonrpc = "2.0",
                    id,
                    result
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                await McpServerHost.WriteResponseAsync(output, new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32000, message = ex.Message }
                }, cancellationToken);
            }
        }
    }

    // ── Initialize ───────────────────────────────────────────────────────────

    private static object CreateInitializeResult() => new
    {
        protocolVersion = "2025-03-26",
        capabilities = new { tools = new { } },
        serverInfo = new
        {
            name    = "bogdb-ng-codegen",
            version = "1.0.0"
        }
    };

    // ── Tool Dispatch ────────────────────────────────────────────────────────

    private async Task<object> HandleToolsCallAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("params", out var @params))
            throw new InvalidOperationException("tools/call requires params.");

        var name = @params.GetProperty("name").GetString();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("tools/call requires a tool name.");

        var arguments = @params.TryGetProperty("arguments", out var argsElement) ? argsElement : default;

        object toolResult = name switch
        {
            // ── Base BogDb tools (pass-through) ────────────────────────────────
            "bogdb_query"      => await _queryService.ExecuteAsync(arguments, cancellationToken),
            "bogdb_schema"     => _schemaService.GetSchema(arguments),
            "bogdb_tables"     => _schemaService.GetTables(arguments),
            "bogdb_table_info" => _schemaService.GetTableInfo(arguments),

            // ── Codegen read tools ────────────────────────────────────────────
            FindSymbolTool.Name       => FindSymbolTool.Execute(_codegenConn!, arguments),
            CallersTool.Name          => CallersTool.Execute(_codegenConn!, arguments),
            CalleesTool.Name          => CalleesTool.Execute(_codegenConn!, arguments),
            ImpactAnalysisTool.Name   => ImpactAnalysisTool.Execute(_codegenConn!, arguments),
            FileContextTool.Name      => FileContextTool.Execute(_codegenConn!, arguments),
            DependencyTreeTool.Name   => DependencyTreeTool.Execute(_codegenConn!, arguments),
            ApiConsumersTool.Name     => ApiConsumersTool.Execute(_codegenConn!, arguments),
            OwnershipTool.Name        => OwnershipTool.Execute(_codegenConn!, arguments),
            SchemaStatusTool.Name     => SchemaStatusTool.Execute(_codegenConn!, arguments),
            SearchDocsTool.Name       => SearchDocsTool.Execute(_codegenConn!, arguments),
            FeatureCoverageTool.Name  => FeatureCoverageTool.Execute(_codegenConn!, arguments),

            // ── BO enrichment tools ───────────────────────────────────────────
            BoundaryAnalysisTool.Name       => BoundaryAnalysisTool.Execute(_codegenConn!, arguments),
            EffectProfileTool.Name          => EffectProfileTool.Execute(_codegenConn!, arguments),
            ComplexityProfileTool.Name      => ComplexityProfileTool.Execute(_codegenConn!, arguments),
            ResponsibilityProfileTool.Name  => ResponsibilityProfileTool.Execute(_codegenConn!, arguments),
            TypeDependenciesTool.Name       => TypeDependenciesTool.Execute(_codegenConn!, arguments),
            SymbolNeighborhoodTool.Name     => SymbolNeighborhoodTool.Execute(_codegenConn!, arguments),

            // ── Codegen ingestion tools ───────────────────────────────────────
            RepoIngestor.Name         => RepoIngestor.Execute(_codegenConn!, arguments),
            BoIngestor.Name           => BoIngestor.Execute(_codegenConn!, arguments),

            // ── BO pivot analysis ────────────────────────────────────────────
            PivotAnalysisTool.Name    => PivotAnalysisTool.Execute(_codegenConn!, arguments),

            _ => throw new InvalidOperationException($"Unknown tool '{name}'.")
        };

        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(toolResult, JsonOptions)
                }
            },
            structuredContent = toolResult,
            isError = false
        };
    }

    // ── Tool List ────────────────────────────────────────────────────────────

    private static object CreateToolsListResult() => new
    {
        tools = new object[]
        {
            // ── Base BogDb tools ───────────────────────────────────────────────
            ToolDef("bogdb_query",
                "Execute a read-only Cypher query against any BogDB database (escape hatch).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        databasePath = new { type = "string" },
                        cypher       = new { type = "string" },
                        parameters   = new { type = "object" },
                        rowLimit     = new { type = "integer", minimum = 1, maximum = 1000 },
                        timeoutMs    = new { type = "integer", minimum = 1 },
                    },
                    required = new[] { "databasePath", "cypher" }
                }),

            ToolDef("bogdb_schema",
                "Return a compact schema summary for a BogDB database.",
                new
                {
                    type = "object",
                    properties = new { databasePath = new { type = "string" } },
                    required = new[] { "databasePath" }
                }),

            ToolDef("bogdb_tables",
                "List node and relationship tables in a BogDB database.",
                new
                {
                    type = "object",
                    properties = new { databasePath = new { type = "string" } },
                    required = new[] { "databasePath" }
                }),

            ToolDef("bogdb_table_info",
                "Return detailed metadata for one table in a BogDB database.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        databasePath = new { type = "string" },
                        tableName    = new { type = "string" },
                    },
                    required = new[] { "databasePath", "tableName" }
                }),

            // ── Codegen read tools ────────────────────────────────────────────
            ToolDef(FindSymbolTool.Name,      FindSymbolTool.Description,      FindSymbolTool.InputSchema),
            ToolDef(CallersTool.Name,         CallersTool.Description,         CallersTool.InputSchema),
            ToolDef(CalleesTool.Name,         CalleesTool.Description,         CalleesTool.InputSchema),
            ToolDef(ImpactAnalysisTool.Name,  ImpactAnalysisTool.Description,  ImpactAnalysisTool.InputSchema),
            ToolDef(FileContextTool.Name,     FileContextTool.Description,     FileContextTool.InputSchema),
            ToolDef(DependencyTreeTool.Name,  DependencyTreeTool.Description,  DependencyTreeTool.InputSchema),
            ToolDef(ApiConsumersTool.Name,     ApiConsumersTool.Description,     ApiConsumersTool.InputSchema),
            ToolDef(OwnershipTool.Name,       OwnershipTool.Description,       OwnershipTool.InputSchema),
            ToolDef(SchemaStatusTool.Name,    SchemaStatusTool.Description,    SchemaStatusTool.InputSchema),
            ToolDef(SearchDocsTool.Name,      SearchDocsTool.Description,      SearchDocsTool.InputSchema),
            ToolDef(FeatureCoverageTool.Name, FeatureCoverageTool.Description, FeatureCoverageTool.InputSchema),

            // ── BO enrichment tools ───────────────────────────────────────────
            ToolDef(BoundaryAnalysisTool.Name,      BoundaryAnalysisTool.Description,      BoundaryAnalysisTool.InputSchema),
            ToolDef(EffectProfileTool.Name,         EffectProfileTool.Description,         EffectProfileTool.InputSchema),
            ToolDef(ComplexityProfileTool.Name,      ComplexityProfileTool.Description,      ComplexityProfileTool.InputSchema),
            ToolDef(ResponsibilityProfileTool.Name, ResponsibilityProfileTool.Description, ResponsibilityProfileTool.InputSchema),
            ToolDef(TypeDependenciesTool.Name,      TypeDependenciesTool.Description,      TypeDependenciesTool.InputSchema),
            ToolDef(SymbolNeighborhoodTool.Name,    SymbolNeighborhoodTool.Description,    SymbolNeighborhoodTool.InputSchema),

            // ── Codegen ingestion tools ───────────────────────────────────────
            ToolDef(RepoIngestor.Name, RepoIngestor.Description, RepoIngestor.InputSchema),
            ToolDef(BoIngestor.Name,  BoIngestor.Description,  BoIngestor.InputSchema),

            // ── BO pivot analysis ────────────────────────────────────────────
            ToolDef(PivotAnalysisTool.Name, PivotAnalysisTool.Description, PivotAnalysisTool.InputSchema),
        }
    };

    private static object ToolDef(string name, string description, object inputSchema) => new
    {
        name,
        description,
        inputSchema
    };
}
