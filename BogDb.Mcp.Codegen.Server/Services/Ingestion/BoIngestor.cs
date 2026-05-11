using System.Diagnostics;
using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Ingestion;

/// <summary>
/// codegen_ingest_bo — Indexes a TS/JS repository using BeyondOrdinary's tree-sitter-backed
/// deep indexing pipeline, then maps the results into the codegen graph.
///
/// This produces far richer data than the regex-based RepoIngestor:
/// symbols + contracts + boundary interactions + effect/complexity/responsibility profiles +
/// resolved CALLS/INSTANTIATES/USES_TYPE edges.
///
/// Invocation: shells out to <c>bo index --json --full</c> and parses the structured output.
/// </summary>
public static class BoIngestor
{
    public const string Name = "codegen_ingest_bo";
    public const string Description =
        "Index a TypeScript/JavaScript repository using BeyondOrdinary's deep analysis pipeline (tree-sitter-backed). " +
        "Produces richer data than standard ingestion: contracts, boundary analysis, effect/complexity/responsibility " +
        "profiles, and resolved symbol dependency edges. Requires BO CLI to be buildable.";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            repoPath = new { type = "string", description = "Absolute path to the TS/JS repository root" },
            boCliPath = new { type = "string", description = "Path to BO.Cli project (defaults to env var BO_CLI_PATH)" },
        },
        required = new[] { "repoPath" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var repoPath  = GetString(arguments, "repoPath");
        var boCliPath = GetOptionalString(arguments, "boCliPath")
                     ?? Environment.GetEnvironmentVariable("BO_CLI_PATH")
                     ?? "";

        if (!Directory.Exists(repoPath))
            return new { success = false, error = $"Repository directory does not exist: {repoPath}" };

        if (string.IsNullOrEmpty(boCliPath))
            return new { success = false, error = "BO CLI path not specified. Set BO_CLI_PATH env var or pass boCliPath parameter." };

        // Shell out to BO CLI for deep indexing
        string boOutput;
        try
        {
            boOutput = RunBoIndex(boCliPath, repoPath);
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"BO CLI execution failed: {ex.Message}" };
        }

        // Parse the BO JSON output
        JsonDocument boDoc;
        try
        {
            boDoc = JsonDocument.Parse(boOutput);
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to parse BO output: {ex.Message}", rawOutput = boOutput.Length > 2000 ? boOutput[..2000] : boOutput };
        }

        var root = boDoc.RootElement;
        if (!root.TryGetProperty("data", out var data))
            return new { success = false, error = "BO output missing 'data' property.", rawOutput = boOutput.Length > 2000 ? boOutput[..2000] : boOutput };

        // Write into the codegen graph
        conn.BeginWriteTransaction();
        try
        {
            var stats = new IngestStats();
            IngestBoData(conn, data, repoPath, stats);
            conn.Commit();

            return new
            {
                success = true,
                repoPath,
                source = "beyondordinary",
                stats = new
                {
                    stats.Repos,
                    stats.Modules,
                    stats.Files,
                    stats.Symbols,
                    stats.Contracts,
                    stats.BoundaryInteractions,
                    stats.EffectProfiles,
                    stats.ComplexityProfiles,
                    stats.ResponsibilityProfiles,
                    stats.SymbolDependencyEdges,
                    stats.FileDependencyEdges,
                },
            };
        }
        catch (Exception ex)
        {
            try { conn.Rollback(); } catch { /* best effort */ }
            return new { success = false, error = $"Graph write failed: {ex.Message}" };
        }
    }

    private static void IngestBoData(BogConnection conn, JsonElement data, string repoPath, IngestStats stats)
    {
        var repoName = Path.GetFileName(repoPath.TrimEnd(Path.DirectorySeparatorChar));

        // Repo node
        if (data.TryGetProperty("repo", out var repo))
        {
            var repoId = GetJsonString(repo, "id") ?? $"repo:{repoName}";
            conn.UpsertNodeById("Repo", repoId, new()
            {
                ["id"]            = repoId,
                ["name"]          = GetJsonString(repo, "name") ?? repoName,
                ["url"]           = repoPath,
                ["defaultBranch"] = "main",
                ["language"]      = "typescript",
                ["lastIndexedAt"] = DateTime.UtcNow.ToString("O"),
            });
            stats.Repos++;

            // Create a synthetic Package node (BO uses Module directly)
            var pkgId = $"pkg:{repoName}/root";
            conn.UpsertNodeById("Package", pkgId, new()
            {
                ["id"]        = pkgId,
                ["name"]      = repoName,
                ["ecosystem"] = "npm",
                ["version"]   = "0.0.0",
            });
            conn.UpsertRelationshipById("CONTAINS_PACKAGE", repoId, pkgId, new());

            // Modules
            if (data.TryGetProperty("modules", out var modules))
            {
                foreach (var mod in modules.EnumerateArray())
                {
                    var modId = GetJsonString(mod, "id") ?? "";
                    if (string.IsNullOrEmpty(modId)) continue;
                    conn.UpsertNodeById("Module", modId, new()
                    {
                        ["id"]            = modId,
                        ["qualifiedName"] = GetJsonString(mod, "qualified_name") ?? modId,
                        ["filePath"]      = "",
                        ["language"]      = "typescript",
                    });
                    conn.UpsertRelationshipById("CONTAINS_MODULE", pkgId, modId, new());
                    stats.Modules++;
                }
            }
        }

        // Files
        if (data.TryGetProperty("files", out var files))
        {
            foreach (var file in files.EnumerateArray())
            {
                var fileId = GetJsonString(file, "id") ?? "";
                if (string.IsNullOrEmpty(fileId)) continue;
                var moduleId = GetJsonString(file, "module_id") ?? "";
                conn.UpsertNodeById("File", fileId, new()
                {
                    ["id"]           = fileId,
                    ["path"]         = GetJsonString(file, "path") ?? "",
                    ["language"]     = GetJsonString(file, "language") ?? "typescript",
                    ["hash"]         = "",
                    ["lastModified"] = DateTime.UtcNow.ToString("O"),
                    ["lineCount"]    = 0L,
                });
                if (!string.IsNullOrEmpty(moduleId))
                    conn.UpsertRelationshipById("CONTAINS_FILE", moduleId, fileId, new());
                stats.Files++;
            }
        }

        // Symbols
        if (data.TryGetProperty("symbols", out var symbols))
        {
            foreach (var sym in symbols.EnumerateArray())
            {
                var symId  = GetJsonString(sym, "id") ?? "";
                var fileId = GetJsonString(sym, "file_id") ?? "";
                if (string.IsNullOrEmpty(symId)) continue;
                conn.UpsertNodeById("Symbol", symId, new()
                {
                    ["id"]            = symId,
                    ["name"]          = GetJsonString(sym, "display_name") ?? "",
                    ["qualifiedName"] = GetJsonString(sym, "qualified_name") ?? "",
                    ["kind"]          = GetJsonString(sym, "kind") ?? "",
                    ["signature"]     = GetJsonString(sym, "signature") ?? "",
                    ["docstring"]     = "",
                    ["startLine"]     = (long)(GetJsonInt(sym, "declaration_line") ?? 0),
                    ["endLine"]       = (long)(GetJsonInt(sym, "declaration_line") ?? 0),
                });
                if (!string.IsNullOrEmpty(fileId))
                    conn.UpsertRelationshipById("DEFINES_SYMBOL", fileId, symId, new()
                    {
                        ["startLine"] = (long)(GetJsonInt(sym, "declaration_line") ?? 0),
                        ["endLine"]   = (long)(GetJsonInt(sym, "declaration_line") ?? 0),
                    });
                stats.Symbols++;
            }
        }

        // Contracts
        if (data.TryGetProperty("contracts", out var contracts))
        {
            foreach (var c in contracts.EnumerateArray())
            {
                var cId    = GetJsonString(c, "id") ?? "";
                var symId  = GetJsonString(c, "symbol_id") ?? "";
                if (string.IsNullOrEmpty(cId)) continue;
                conn.UpsertNodeById("Contract", cId, new()
                {
                    ["id"]                         = cId,
                    ["symbol_id"]                  = symId,
                    ["input_types_json"]           = GetJsonString(c, "input_types_json") ?? "",
                    ["output_types_json"]          = GetJsonString(c, "output_types_json") ?? "",
                    ["generic_constraints_json"]   = GetJsonString(c, "generic_constraints_json") ?? "",
                    ["throws_or_error_modes_json"] = GetJsonString(c, "throws_or_error_modes_json") ?? "",
                    ["schema_shapes_json"]         = GetJsonString(c, "schema_shapes_json") ?? "",
                    ["nullability_json"]           = GetJsonString(c, "nullability_json") ?? "",
                    ["async_mode"]                 = GetJsonString(c, "async_mode") ?? "",
                    ["confidence"]                 = GetJsonDouble(c, "confidence") ?? 0.0,
                });
                if (!string.IsNullOrEmpty(symId))
                    conn.UpsertRelationshipById("HAS_CONTRACT", symId, cId, new()
                    {
                        ["confidence"] = GetJsonDouble(c, "confidence") ?? 0.0,
                    });
                stats.Contracts++;
            }
        }

        // Boundary Interactions
        if (data.TryGetProperty("boundary_interactions", out var boundaries))
        {
            foreach (var bi in boundaries.EnumerateArray())
            {
                var biId   = GetJsonString(bi, "id") ?? "";
                var fileId = GetJsonString(bi, "file_id") ?? "";
                if (string.IsNullOrEmpty(biId)) continue;
                conn.UpsertNodeById("BoundaryInteraction", biId, new()
                {
                    ["id"]             = biId,
                    ["file_id"]        = fileId,
                    ["boundary_type"]  = GetJsonString(bi, "boundary_type") ?? "",
                    ["operation_type"] = GetJsonString(bi, "operation_type") ?? "",
                    ["target_name"]    = GetJsonString(bi, "target_name") ?? "",
                    ["effect_mode"]    = GetJsonString(bi, "effect_mode") ?? "",
                    ["confidence"]     = GetJsonDouble(bi, "confidence") ?? 0.0,
                });
                if (!string.IsNullOrEmpty(fileId))
                    conn.UpsertRelationshipById("CROSSES_BOUNDARY", fileId, biId, new()
                    {
                        ["boundary_type"]  = GetJsonString(bi, "boundary_type") ?? "",
                        ["operation_type"] = GetJsonString(bi, "operation_type") ?? "",
                        ["effect_mode"]    = GetJsonString(bi, "effect_mode") ?? "",
                        ["confidence"]     = GetJsonDouble(bi, "confidence") ?? 0.0,
                    });
                stats.BoundaryInteractions++;
            }
        }

        // Effect Profiles
        if (data.TryGetProperty("effect_profiles", out var effectProfiles))
        {
            foreach (var ep in effectProfiles.EnumerateArray())
            {
                var epId     = GetJsonString(ep, "id") ?? "";
                var targetId = GetJsonString(ep, "target_id") ?? "";
                if (string.IsNullOrEmpty(epId)) continue;
                conn.UpsertNodeById("EffectProfile", epId, new()
                {
                    ["id"]                      = epId,
                    ["target_id"]               = targetId,
                    ["target_kind"]             = GetJsonString(ep, "target_kind") ?? "",
                    ["reads_state"]             = GetJsonBool(ep, "reads_state"),
                    ["writes_state"]            = GetJsonBool(ep, "writes_state"),
                    ["emits_events"]            = GetJsonBool(ep, "emits_events"),
                    ["calls_external_service"]  = GetJsonBool(ep, "calls_external_service"),
                    ["mutates_input"]           = GetJsonBool(ep, "mutates_input"),
                    ["has_retry_logic"]         = GetJsonBool(ep, "has_retry_logic"),
                    ["has_transaction_logic"]   = GetJsonBool(ep, "has_transaction_logic"),
                    ["has_auth_logic"]          = GetJsonBool(ep, "has_auth_logic"),
                    ["has_validation_logic"]    = GetJsonBool(ep, "has_validation_logic"),
                    ["has_caching_logic"]       = GetJsonBool(ep, "has_caching_logic"),
                    ["has_logging_logic"]       = GetJsonBool(ep, "has_logging_logic"),
                    ["side_effect_classes_json"] = GetJsonString(ep, "side_effect_classes_json") ?? "",
                    ["confidence"]              = GetJsonDouble(ep, "confidence") ?? 0.0,
                });
                if (!string.IsNullOrEmpty(targetId))
                    conn.UpsertRelationshipById("HAS_EFFECT_PROFILE", targetId, epId, new()
                    {
                        ["target_kind"] = GetJsonString(ep, "target_kind") ?? "",
                        ["confidence"]  = GetJsonDouble(ep, "confidence") ?? 0.0,
                    });
                stats.EffectProfiles++;
            }
        }

        // Complexity Profiles
        if (data.TryGetProperty("complexity_profiles", out var complexityProfiles))
        {
            foreach (var cp in complexityProfiles.EnumerateArray())
            {
                var cpId     = GetJsonString(cp, "id") ?? "";
                var targetId = GetJsonString(cp, "target_id") ?? "";
                if (string.IsNullOrEmpty(cpId)) continue;
                conn.UpsertNodeById("ComplexityProfile", cpId, new()
                {
                    ["id"]                    = cpId,
                    ["target_id"]             = targetId,
                    ["target_kind"]           = GetJsonString(cp, "target_kind") ?? "",
                    ["loc"]                   = (long)(GetJsonInt(cp, "loc") ?? 0),
                    ["cognitive_complexity"]   = (long)(GetJsonInt(cp, "cognitive_complexity") ?? 0),
                    ["cyclomatic_complexity"]  = (long)(GetJsonInt(cp, "cyclomatic_complexity") ?? 0),
                    ["nesting_depth"]         = (long)(GetJsonInt(cp, "nesting_depth") ?? 0),
                    ["parameter_count"]       = (long)(GetJsonInt(cp, "parameter_count") ?? 0),
                    ["branch_count"]          = (long)(GetJsonInt(cp, "branch_count") ?? 0),
                    ["side_effect_count"]     = (long)(GetJsonInt(cp, "side_effect_count") ?? 0),
                    ["fan_in"]               = (long)(GetJsonInt(cp, "fan_in") ?? 0),
                    ["fan_out"]              = (long)(GetJsonInt(cp, "fan_out") ?? 0),
                    ["confidence"]            = GetJsonDouble(cp, "confidence") ?? 0.0,
                });
                if (!string.IsNullOrEmpty(targetId))
                    conn.UpsertRelationshipById("HAS_COMPLEXITY_PROFILE", targetId, cpId, new()
                    {
                        ["target_kind"] = GetJsonString(cp, "target_kind") ?? "",
                        ["confidence"]  = GetJsonDouble(cp, "confidence") ?? 0.0,
                    });
                stats.ComplexityProfiles++;
            }
        }

        // Responsibility Profiles
        if (data.TryGetProperty("responsibility_profiles", out var responsibilityProfiles))
        {
            foreach (var rp in responsibilityProfiles.EnumerateArray())
            {
                var rpId     = GetJsonString(rp, "id") ?? "";
                var targetId = GetJsonString(rp, "target_id") ?? "";
                if (string.IsNullOrEmpty(rpId)) continue;
                conn.UpsertNodeById("ResponsibilityProfile", rpId, new()
                {
                    ["id"]                            = rpId,
                    ["target_id"]                     = targetId,
                    ["target_kind"]                   = GetJsonString(rp, "target_kind") ?? "",
                    ["boundary_type_count"]            = (long)(GetJsonInt(rp, "boundary_type_count") ?? 0),
                    ["dependency_category_count"]      = (long)(GetJsonInt(rp, "dependency_category_count") ?? 0),
                    ["capability_cluster_count"]       = (long)(GetJsonInt(rp, "capability_cluster_count") ?? 0),
                    ["side_effect_class_count"]        = (long)(GetJsonInt(rp, "side_effect_class_count") ?? 0),
                    ["responsibility_spread_score"]    = GetJsonDouble(rp, "responsibility_spread_score") ?? 0.0,
                    ["dominant_responsibilities_json"] = GetJsonString(rp, "dominant_responsibilities_json") ?? "",
                    ["confidence"]                    = GetJsonDouble(rp, "confidence") ?? 0.0,
                });
                if (!string.IsNullOrEmpty(targetId))
                    conn.UpsertRelationshipById("HAS_RESPONSIBILITY_PROFILE", targetId, rpId, new()
                    {
                        ["target_kind"] = GetJsonString(rp, "target_kind") ?? "",
                        ["confidence"]  = GetJsonDouble(rp, "confidence") ?? 0.0,
                    });
                stats.ResponsibilityProfiles++;
            }
        }

        // Symbol Dependencies (CALLS / INSTANTIATES / USES_TYPE)
        if (data.TryGetProperty("symbol_dependencies", out var symDeps))
        {
            foreach (var dep in symDeps.EnumerateArray())
            {
                var fromId   = GetJsonString(dep, "from_symbol_id") ?? "";
                var toId     = GetJsonString(dep, "to_symbol_id") ?? "";
                var relType  = GetJsonString(dep, "relation_type") ?? "calls";
                if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) continue;

                var edgeLabel = relType switch
                {
                    "instantiates" => "BO_INSTANTIATES",
                    "uses_type"    => "BO_USES_TYPE",
                    _              => "BO_CALLS",
                };
                conn.UpsertRelationshipById(edgeLabel, fromId, toId, new()
                {
                    ["evidence"]   = GetJsonString(dep, "evidence") ?? "",
                    ["confidence"] = GetJsonDouble(dep, "confidence") ?? 0.0,
                });
                stats.SymbolDependencyEdges++;
            }
        }

        // File Dependencies (IMPORTS)
        if (data.TryGetProperty("file_dependencies", out var fileDeps))
        {
            foreach (var dep in fileDeps.EnumerateArray())
            {
                var fromId = GetJsonString(dep, "from_file_id") ?? "";
                var toId   = GetJsonString(dep, "to_file_id") ?? "";
                if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) continue;

                conn.UpsertRelationshipById("BO_IMPORTS", fromId, toId, new()
                {
                    ["import_text"]    = GetJsonString(dep, "import_text") ?? "",
                    ["is_runtime"]     = GetJsonBool(dep, "is_runtime"),
                    ["is_compile_time"] = GetJsonBool(dep, "is_compile_time"),
                });
                stats.FileDependencyEdges++;
            }
        }
    }

    // ── BO CLI Execution ────────────────────────────────────────────────────

    private static string RunBoIndex(string boCliPath, string repoPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = "dotnet",
            Arguments              = $"run --project \"{boCliPath}\" -- index --json --full",
            WorkingDirectory       = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start BO CLI process.");

        var output = process.StandardOutput.ReadToEnd();
        var error  = process.StandardError.ReadToEnd();

        process.WaitForExit(TimeSpan.FromMinutes(5));

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"BO CLI exited with code {process.ExitCode}. stderr: {error}");

        return output;
    }

    // ── JSON Helpers ─────────────────────────────────────────────────────────

    private static string? GetJsonString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetJsonInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static double? GetJsonDouble(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetDouble(out var d) ? d : null;

    private static bool GetJsonBool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ── Internal Types ──────────────────────────────────────────────────────

    private sealed class IngestStats
    {
        public int Repos { get; set; }
        public int Modules { get; set; }
        public int Files { get; set; }
        public int Symbols { get; set; }
        public int Contracts { get; set; }
        public int BoundaryInteractions { get; set; }
        public int EffectProfiles { get; set; }
        public int ComplexityProfiles { get; set; }
        public int ResponsibilityProfiles { get; set; }
        public int SymbolDependencyEdges { get; set; }
        public int FileDependencyEdges { get; set; }
    }
}
