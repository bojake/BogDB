using System.Diagnostics;
using System.Text.Json;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Tools;

/// <summary>
/// codegen_pivot_analysis — Run refactor pivot analysis on a file.
/// Shells out to <c>bo pivot &lt;file&gt; --json</c> and returns structured
/// refactoring intelligence: RPS scores, seam candidates, boundary adapters,
/// and symbol-level complexity hotspots.
/// </summary>
public static class PivotAnalysisTool
{
    public const string Name = "codegen_pivot_analysis";
    public const string Description =
        "Run BeyondOrdinary refactor pivot analysis on a file. " +
        "Returns Refactor Pressure Score (RPS), refactoring recommendation, " +
        "candidate seams for extraction (boundary adapters), and symbol-level " +
        "complexity hotspots. Use this to identify what parts of a monster class " +
        "should be extracted and in what order.";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            filePath = new { type = "string", description = "File name or path to analyze (e.g., 'RunExecutionService.cs' or 'src/Workers/RunExecutionService.cs')" },
            repoPath = new { type = "string", description = "Absolute path to the repository root" },
            boCliPath = new { type = "string", description = "Path to BO.Cli project (defaults to env var BO_CLI_PATH)" },
        },
        required = new[] { "filePath", "repoPath" },
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var filePath  = GetString(arguments, "filePath");
        var repoPath  = GetString(arguments, "repoPath");
        var boCliPath = GetOptionalString(arguments, "boCliPath")
                     ?? Environment.GetEnvironmentVariable("BO_CLI_PATH")
                     ?? "";

        if (string.IsNullOrEmpty(filePath))
            return new { success = false, error = "filePath is required." };
        if (string.IsNullOrEmpty(repoPath))
            return new { success = false, error = "repoPath is required." };
        if (!Directory.Exists(repoPath))
            return new { success = false, error = $"Repository directory does not exist: {repoPath}" };
        if (string.IsNullOrEmpty(boCliPath))
            return new { success = false, error = "BO CLI path not specified. Set BO_CLI_PATH env var or pass boCliPath parameter." };

        // Shell out to BO CLI for pivot analysis
        string boOutput;
        try
        {
            boOutput = RunBoPivot(boCliPath, repoPath, filePath);
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"BO CLI pivot execution failed: {ex.Message}" };
        }

        // Parse the BO JSON output
        JsonDocument boDoc;
        try
        {
            boDoc = JsonDocument.Parse(boOutput);
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Failed to parse BO pivot output: {ex.Message}", rawOutput = Truncate(boOutput, 2000) };
        }

        var root = boDoc.RootElement;

        // Check for BO-level errors
        if (root.TryGetProperty("status", out var status) &&
            status.GetString() == "error")
        {
            var errorMsg = root.TryGetProperty("error", out var err) ? err.GetString() : "Unknown BO error";
            return new { success = false, error = errorMsg };
        }

        return new
        {
            success     = true,
            filePath,
            repoPath,
            source      = "beyondordinary_pivot",
            pivotResult = root,
        };
    }

    private static string RunBoPivot(string boCliPath, string repoPath, string filePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = "dotnet",
            Arguments              = $"run --project \"{boCliPath}\" -- pivot \"{filePath}\" --json",
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

        process.WaitForExit(TimeSpan.FromMinutes(3));

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"BO CLI exited with code {process.ExitCode}. stderr: {error}");

        return output;
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Truncate(string s, int maxLen) =>
        s.Length > maxLen ? s[..maxLen] : s;
}
