using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services;

/// <summary>
/// Defines and provisions the code-intelligence graph schema inside a BogDB database.
/// All tables are created idempotently via <c>EnsureNodeTable</c> / <c>EnsureRelTable</c>.
/// </summary>
public sealed class CodegenSchemaService
{
    public const string SchemaVersion = "1.0.0";

    // ── Node Tables ──────────────────────────────────────────────────────────

    public static void EnsureSchema(BogConnection conn)
    {
        conn.BeginWriteTransaction();

        // ── Repo ─────────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Repo", new Dictionary<string, LogicalTypeID>
        {
            ["id"]            = LogicalTypeID.STRING,
            ["name"]          = LogicalTypeID.STRING,
            ["url"]           = LogicalTypeID.STRING,
            ["defaultBranch"] = LogicalTypeID.STRING,
            ["language"]      = LogicalTypeID.STRING,
            ["lastIndexedAt"] = LogicalTypeID.STRING,
        });

        // ── Package ──────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Package", new Dictionary<string, LogicalTypeID>
        {
            ["id"]        = LogicalTypeID.STRING,
            ["name"]      = LogicalTypeID.STRING,
            ["ecosystem"] = LogicalTypeID.STRING,   // npm | nuget | pip | maven
            ["version"]   = LogicalTypeID.STRING,
        });

        // ── Module ───────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Module", new Dictionary<string, LogicalTypeID>
        {
            ["id"]            = LogicalTypeID.STRING,
            ["qualifiedName"] = LogicalTypeID.STRING,
            ["filePath"]      = LogicalTypeID.STRING,
            ["language"]      = LogicalTypeID.STRING,
        });

        // ── File ─────────────────────────────────────────────────────────────
        conn.EnsureNodeTable("File", new Dictionary<string, LogicalTypeID>
        {
            ["id"]           = LogicalTypeID.STRING,
            ["path"]         = LogicalTypeID.STRING,
            ["language"]     = LogicalTypeID.STRING,
            ["hash"]         = LogicalTypeID.STRING,
            ["lastModified"] = LogicalTypeID.STRING,
            ["lineCount"]    = LogicalTypeID.INT64,
        });

        // ── Symbol ───────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Symbol", new Dictionary<string, LogicalTypeID>
        {
            ["id"]            = LogicalTypeID.STRING,
            ["name"]          = LogicalTypeID.STRING,
            ["qualifiedName"] = LogicalTypeID.STRING,
            ["kind"]          = LogicalTypeID.STRING,   // class | function | type | const | interface | enum | method | property
            ["signature"]     = LogicalTypeID.STRING,
            ["docstring"]     = LogicalTypeID.STRING,
            ["startLine"]     = LogicalTypeID.INT64,
            ["endLine"]       = LogicalTypeID.INT64,
        });

        // ── Service ──────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Service", new Dictionary<string, LogicalTypeID>
        {
            ["id"]         = LogicalTypeID.STRING,
            ["name"]       = LogicalTypeID.STRING,
            ["runtime"]    = LogicalTypeID.STRING,
            ["deployUnit"] = LogicalTypeID.STRING,
            ["team"]       = LogicalTypeID.STRING,
        });

        // ── ApiEndpoint ──────────────────────────────────────────────────────
        conn.EnsureNodeTable("ApiEndpoint", new Dictionary<string, LogicalTypeID>
        {
            ["id"]           = LogicalTypeID.STRING,
            ["method"]       = LogicalTypeID.STRING,   // GET | POST | PUT | DELETE | gRPC | GraphQL
            ["path"]         = LogicalTypeID.STRING,
            ["version"]      = LogicalTypeID.STRING,
            ["authRequired"] = LogicalTypeID.BOOL,
        });

        // ── Schema (data contracts) ──────────────────────────────────────────
        conn.EnsureNodeTable("DataSchema", new Dictionary<string, LogicalTypeID>
        {
            ["id"]      = LogicalTypeID.STRING,
            ["name"]    = LogicalTypeID.STRING,
            ["format"]  = LogicalTypeID.STRING,   // protobuf | json-schema | openapi | avro
            ["version"] = LogicalTypeID.STRING,
        });

        // ── Consumer ─────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Consumer", new Dictionary<string, LogicalTypeID>
        {
            ["id"]              = LogicalTypeID.STRING,
            ["name"]            = LogicalTypeID.STRING,
            ["service"]         = LogicalTypeID.STRING,
            ["integrationKind"] = LogicalTypeID.STRING,   // sdk | http | event
        });

        // ── Owner ────────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Owner", new Dictionary<string, LogicalTypeID>
        {
            ["id"]             = LogicalTypeID.STRING,
            ["team"]           = LogicalTypeID.STRING,
            ["org"]            = LogicalTypeID.STRING,
            ["contactChannel"] = LogicalTypeID.STRING,
        });

        // ── DeployUnit ───────────────────────────────────────────────────────
        conn.EnsureNodeTable("DeployUnit", new Dictionary<string, LogicalTypeID>
        {
            ["id"]      = LogicalTypeID.STRING,
            ["name"]    = LogicalTypeID.STRING,
            ["runtime"] = LogicalTypeID.STRING,
            ["infra"]   = LogicalTypeID.STRING,   // k8s | lambda | ecs | azure-app
            ["repo"]    = LogicalTypeID.STRING,
        });

        // ── FeatureFlag ──────────────────────────────────────────────────────
        conn.EnsureNodeTable("FeatureFlag", new Dictionary<string, LogicalTypeID>
        {
            ["id"]           = LogicalTypeID.STRING,
            ["key"]          = LogicalTypeID.STRING,
            ["provider"]     = LogicalTypeID.STRING,
            ["defaultState"] = LogicalTypeID.STRING,
            ["segments"]     = LogicalTypeID.STRING,
        });

        // ── Migration ────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Migration", new Dictionary<string, LogicalTypeID>
        {
            ["id"]        = LogicalTypeID.STRING,
            ["direction"] = LogicalTypeID.STRING,   // up | down
            ["appliedAt"] = LogicalTypeID.STRING,
            ["breaking"]  = LogicalTypeID.BOOL,
        });

        // ── Runbook ──────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Runbook", new Dictionary<string, LogicalTypeID>
        {
            ["id"]           = LogicalTypeID.STRING,
            ["title"]        = LogicalTypeID.STRING,
            ["url"]          = LogicalTypeID.STRING,
            ["scope"]        = LogicalTypeID.STRING,
            ["lastVerified"] = LogicalTypeID.STRING,
        });

        // ── ArchDoc ──────────────────────────────────────────────────────────
        conn.EnsureNodeTable("ArchDoc", new Dictionary<string, LogicalTypeID>
        {
            ["id"]     = LogicalTypeID.STRING,
            ["title"]  = LogicalTypeID.STRING,
            ["url"]    = LogicalTypeID.STRING,
            ["scope"]  = LogicalTypeID.STRING,
            ["format"] = LogicalTypeID.STRING,   // adr | rfc | design-doc
        });

        // ══════════════════════════════════════════════════════════════════════
        // BO (BeyondOrdinary) enrichment tables — deep code-intelligence nodes
        // ══════════════════════════════════════════════════════════════════════

        // ── Contract ─────────────────────────────────────────────────────────
        conn.EnsureNodeTable("Contract", new Dictionary<string, LogicalTypeID>
        {
            ["id"]                       = LogicalTypeID.STRING,
            ["symbol_id"]                = LogicalTypeID.STRING,
            ["input_types_json"]         = LogicalTypeID.STRING,
            ["output_types_json"]        = LogicalTypeID.STRING,
            ["generic_constraints_json"] = LogicalTypeID.STRING,
            ["throws_or_error_modes_json"] = LogicalTypeID.STRING,
            ["schema_shapes_json"]       = LogicalTypeID.STRING,
            ["nullability_json"]         = LogicalTypeID.STRING,
            ["async_mode"]               = LogicalTypeID.STRING,
            ["confidence"]               = LogicalTypeID.DOUBLE,
        });

        // ── BoundaryInteraction ──────────────────────────────────────────────
        conn.EnsureNodeTable("BoundaryInteraction", new Dictionary<string, LogicalTypeID>
        {
            ["id"]             = LogicalTypeID.STRING,
            ["file_id"]        = LogicalTypeID.STRING,
            ["boundary_type"]  = LogicalTypeID.STRING,   // db | http | filesystem | queue | cache | auth | logging | metrics | config
            ["operation_type"] = LogicalTypeID.STRING,   // read | write | publish | consume | authenticate | log | measure
            ["target_name"]    = LogicalTypeID.STRING,
            ["effect_mode"]    = LogicalTypeID.STRING,
            ["confidence"]     = LogicalTypeID.DOUBLE,
        });

        // ── EffectProfile ────────────────────────────────────────────────────
        conn.EnsureNodeTable("EffectProfile", new Dictionary<string, LogicalTypeID>
        {
            ["id"]                      = LogicalTypeID.STRING,
            ["target_id"]               = LogicalTypeID.STRING,
            ["target_kind"]             = LogicalTypeID.STRING,
            ["reads_state"]             = LogicalTypeID.BOOL,
            ["writes_state"]            = LogicalTypeID.BOOL,
            ["emits_events"]            = LogicalTypeID.BOOL,
            ["calls_external_service"]  = LogicalTypeID.BOOL,
            ["mutates_input"]           = LogicalTypeID.BOOL,
            ["has_retry_logic"]         = LogicalTypeID.BOOL,
            ["has_transaction_logic"]   = LogicalTypeID.BOOL,
            ["has_auth_logic"]          = LogicalTypeID.BOOL,
            ["has_validation_logic"]    = LogicalTypeID.BOOL,
            ["has_caching_logic"]       = LogicalTypeID.BOOL,
            ["has_logging_logic"]       = LogicalTypeID.BOOL,
            ["side_effect_classes_json"] = LogicalTypeID.STRING,
            ["confidence"]              = LogicalTypeID.DOUBLE,
        });

        // ── ComplexityProfile ────────────────────────────────────────────────
        conn.EnsureNodeTable("ComplexityProfile", new Dictionary<string, LogicalTypeID>
        {
            ["id"]                     = LogicalTypeID.STRING,
            ["target_id"]              = LogicalTypeID.STRING,
            ["target_kind"]            = LogicalTypeID.STRING,
            ["loc"]                    = LogicalTypeID.INT64,
            ["cognitive_complexity"]    = LogicalTypeID.INT64,
            ["cyclomatic_complexity"]   = LogicalTypeID.INT64,
            ["nesting_depth"]          = LogicalTypeID.INT64,
            ["parameter_count"]        = LogicalTypeID.INT64,
            ["branch_count"]           = LogicalTypeID.INT64,
            ["side_effect_count"]      = LogicalTypeID.INT64,
            ["fan_in"]                 = LogicalTypeID.INT64,
            ["fan_out"]                = LogicalTypeID.INT64,
            ["confidence"]             = LogicalTypeID.DOUBLE,
        });

        // ── ResponsibilityProfile ───────────────────────────────────────────
        conn.EnsureNodeTable("ResponsibilityProfile", new Dictionary<string, LogicalTypeID>
        {
            ["id"]                             = LogicalTypeID.STRING,
            ["target_id"]                      = LogicalTypeID.STRING,
            ["target_kind"]                    = LogicalTypeID.STRING,
            ["boundary_type_count"]             = LogicalTypeID.INT64,
            ["dependency_category_count"]       = LogicalTypeID.INT64,
            ["capability_cluster_count"]        = LogicalTypeID.INT64,
            ["side_effect_class_count"]         = LogicalTypeID.INT64,
            ["responsibility_spread_score"]     = LogicalTypeID.DOUBLE,
            ["dominant_responsibilities_json"]  = LogicalTypeID.STRING,
            ["confidence"]                     = LogicalTypeID.DOUBLE,
        });

        // ── Relationship Tables ──────────────────────────────────────────────

        // Repo hierarchy
        conn.EnsureRelTable("CONTAINS_PACKAGE", "Repo", "Package",
            new Dictionary<string, LogicalTypeID>());

        conn.EnsureRelTable("CONTAINS_MODULE", "Package", "Module",
            new Dictionary<string, LogicalTypeID>());

        conn.EnsureRelTable("CONTAINS_FILE", "Module", "File",
            new Dictionary<string, LogicalTypeID>());

        // Symbol relationships
        conn.EnsureRelTable("DEFINES_SYMBOL", "File", "Symbol",
            new Dictionary<string, LogicalTypeID>
            {
                ["startLine"] = LogicalTypeID.INT64,
                ["endLine"]   = LogicalTypeID.INT64,
            });

        conn.EnsureRelTable("REFERENCES_SYMBOL", "Symbol", "Symbol",
            new Dictionary<string, LogicalTypeID>
            {
                ["kind"] = LogicalTypeID.STRING,   // call | import | extend | implement
                ["line"] = LogicalTypeID.INT64,
            });

        // Package dependencies
        conn.EnsureRelTable("DEPENDS_ON", "Package", "Package",
            new Dictionary<string, LogicalTypeID>
            {
                ["versionConstraint"] = LogicalTypeID.STRING,
                ["scope"]             = LogicalTypeID.STRING,   // direct | dev | peer
            });

        // Service / API relationships
        conn.EnsureRelTable("EXPOSES_API", "Service", "ApiEndpoint",
            new Dictionary<string, LogicalTypeID>());

        conn.EnsureRelTable("GOVERNED_BY_SCHEMA", "ApiEndpoint", "DataSchema",
            new Dictionary<string, LogicalTypeID>
            {
                ["role"] = LogicalTypeID.STRING,   // request | response
            });

        conn.EnsureRelTable("CONSUMES_API", "Consumer", "ApiEndpoint",
            new Dictionary<string, LogicalTypeID>
            {
                ["via"] = LogicalTypeID.STRING,   // sdk | http | event
            });

        // Ownership — multiple source types can have owners
        conn.EnsureRelTable("REPO_OWNED_BY", "Repo", "Owner",
            new Dictionary<string, LogicalTypeID>());

        conn.EnsureRelTable("SERVICE_OWNED_BY", "Service", "Owner",
            new Dictionary<string, LogicalTypeID>());

        conn.EnsureRelTable("PACKAGE_OWNED_BY", "Package", "Owner",
            new Dictionary<string, LogicalTypeID>());

        // Deploy
        conn.EnsureRelTable("DEPLOYED_AS", "Service", "DeployUnit",
            new Dictionary<string, LogicalTypeID>());

        // Feature flags
        conn.EnsureRelTable("GATED_BY", "Symbol", "FeatureFlag",
            new Dictionary<string, LogicalTypeID>());

        // Migrations
        conn.EnsureRelTable("HAS_MIGRATION", "Repo", "Migration",
            new Dictionary<string, LogicalTypeID>());

        // Documentation links
        conn.EnsureRelTable("SERVICE_DOCUMENTED_IN", "Service", "Runbook",
            new Dictionary<string, LogicalTypeID>());

        conn.EnsureRelTable("SERVICE_ARCH_DOC", "Service", "ArchDoc",
            new Dictionary<string, LogicalTypeID>());

        conn.EnsureRelTable("SYMBOL_DOCUMENTED_IN", "Symbol", "ArchDoc",
            new Dictionary<string, LogicalTypeID>());

        conn.EnsureRelTable("API_DOCUMENTED_IN", "ApiEndpoint", "Runbook",
            new Dictionary<string, LogicalTypeID>());

        // ArchDoc → ArchDoc links
        conn.EnsureRelTable("LINKED_TO", "ArchDoc", "ArchDoc",
            new Dictionary<string, LogicalTypeID>
            {
                ["kind"] = LogicalTypeID.STRING,   // supersedes | relates | implements
            });

        // ══════════════════════════════════════════════════════════════════════
        // BO enrichment relationships
        // ══════════════════════════════════════════════════════════════════════

        conn.EnsureRelTable("HAS_CONTRACT", "Symbol", "Contract",
            new Dictionary<string, LogicalTypeID>
            {
                ["confidence"] = LogicalTypeID.DOUBLE,
            });

        conn.EnsureRelTable("CROSSES_BOUNDARY", "File", "BoundaryInteraction",
            new Dictionary<string, LogicalTypeID>
            {
                ["boundary_type"]  = LogicalTypeID.STRING,
                ["operation_type"] = LogicalTypeID.STRING,
                ["effect_mode"]    = LogicalTypeID.STRING,
                ["confidence"]     = LogicalTypeID.DOUBLE,
            });

        conn.EnsureRelTable("HAS_EFFECT_PROFILE", "File", "EffectProfile",
            new Dictionary<string, LogicalTypeID>
            {
                ["target_kind"] = LogicalTypeID.STRING,
                ["confidence"]  = LogicalTypeID.DOUBLE,
            });

        conn.EnsureRelTable("HAS_COMPLEXITY_PROFILE", "File", "ComplexityProfile",
            new Dictionary<string, LogicalTypeID>
            {
                ["target_kind"] = LogicalTypeID.STRING,
                ["confidence"]  = LogicalTypeID.DOUBLE,
            });

        conn.EnsureRelTable("HAS_RESPONSIBILITY_PROFILE", "File", "ResponsibilityProfile",
            new Dictionary<string, LogicalTypeID>
            {
                ["target_kind"] = LogicalTypeID.STRING,
                ["confidence"]  = LogicalTypeID.DOUBLE,
            });

        // BO symbol-level dependency edges (resolved by tree-sitter)
        conn.EnsureRelTable("BO_CALLS", "Symbol", "Symbol",
            new Dictionary<string, LogicalTypeID>
            {
                ["evidence"]   = LogicalTypeID.STRING,
                ["confidence"] = LogicalTypeID.DOUBLE,
            });

        conn.EnsureRelTable("BO_INSTANTIATES", "Symbol", "Symbol",
            new Dictionary<string, LogicalTypeID>
            {
                ["evidence"]   = LogicalTypeID.STRING,
                ["confidence"] = LogicalTypeID.DOUBLE,
            });

        conn.EnsureRelTable("BO_USES_TYPE", "Symbol", "Symbol",
            new Dictionary<string, LogicalTypeID>
            {
                ["evidence"]   = LogicalTypeID.STRING,
                ["confidence"] = LogicalTypeID.DOUBLE,
            });

        conn.EnsureRelTable("BO_IMPORTS", "File", "File",
            new Dictionary<string, LogicalTypeID>
            {
                ["import_text"]    = LogicalTypeID.STRING,
                ["is_runtime"]     = LogicalTypeID.BOOL,
                ["is_compile_time"] = LogicalTypeID.BOOL,
            });

        conn.Commit();
    }

    /// <summary>
    /// Returns the schema version and table counts for health checks.
    /// </summary>
    public static object GetSchemaStatus(BogConnection conn)
    {
        var tableCounts = new Dictionary<string, long>();
        string[] nodeTableNames =
        {
            "Repo", "Package", "Module", "File", "Symbol",
            "Service", "ApiEndpoint", "DataSchema", "Consumer", "Owner",
            "DeployUnit", "FeatureFlag", "Migration", "Runbook", "ArchDoc",
            // BO enrichment tables
            "Contract", "BoundaryInteraction", "EffectProfile",
            "ComplexityProfile", "ResponsibilityProfile"
        };

        foreach (var tableName in nodeTableNames)
        {
            try
            {
                var result = conn.Query($"MATCH (n:{tableName}) RETURN count(n) AS c");
                if (result.IsSuccess && result.HasNext())
                {
                    var row = result.GetNext();
                    var raw = row.GetAsDictionary();
                    tableCounts[tableName] = Convert.ToInt64(raw["c"] ?? 0L);
                }
                else
                {
                    tableCounts[tableName] = 0;
                }
            }
            catch
            {
                tableCounts[tableName] = -1;  // table may not exist yet
            }
        }

        return new
        {
            schemaVersion = SchemaVersion,
            tableCounts,
            healthy = tableCounts.Values.All(c => c >= 0),
        };
    }
}
