using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BogDb.Core.Main;

namespace BogDb.Mcp.Codegen.Server.Services.Ingestion;

/// <summary>
/// codegen_ingest_repo — Scans a local repository directory and populates the
/// codegen graph with Repo → Package → Module → File → Symbol nodes and edges.
///
/// Day-1 extraction uses convention-based regex patterns for C#, TypeScript, Python, and Java.
/// The interface is designed to plug in tree-sitter or LSP-based extractors later.
/// </summary>
public static class RepoIngestor
{
    public const string Name = "codegen_ingest_repo";
    public const string Description =
        "Scan a local repository directory and ingest its structure into the codegen graph. " +
        "Creates Repo, Package, Module, File, and Symbol nodes with hierarchy edges. " +
        "Supports C#, TypeScript/JavaScript, Python, and Java.";

    public static object InputSchema => new
    {
        type = "object",
        properties = new
        {
            repoPath = new { type = "string", description = "Absolute path to the repository root" },
            repoName = new { type = "string", description = "Display name for the repo (defaults to directory name)" },
            branch   = new { type = "string", description = "Branch name (defaults to 'main')" },
        },
        required = new[] { "repoPath" },
    };

    // Supported file extensions → language mapping
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"]   = "csharp",
        [".ts"]   = "typescript",
        [".tsx"]  = "typescript",
        [".js"]   = "javascript",
        [".jsx"]  = "javascript",
        [".py"]   = "python",
        [".java"] = "java",
    };

    public static object Execute(BogConnection conn, JsonElement arguments)
    {
        var repoPath = GetString(arguments, "repoPath");
        var repoName = GetOptionalString(arguments, "repoName") ?? Path.GetFileName(repoPath.TrimEnd(Path.DirectorySeparatorChar));
        var branch   = GetOptionalString(arguments, "branch") ?? "main";

        if (!Directory.Exists(repoPath))
            return new { success = false, error = $"Directory does not exist: {repoPath}" };

        conn.BeginWriteTransaction();

        try
        {
            var repoId = $"repo:{repoName}";
            var now = DateTime.UtcNow.ToString("O");

            // 1. Create Repo node
            conn.UpsertNodeById("Repo", repoId, new()
            {
                ["id"]            = repoId,
                ["name"]          = repoName,
                ["url"]           = repoPath,
                ["defaultBranch"] = branch,
                ["language"]      = "multi",
                ["lastIndexedAt"] = now,
            });

            // 2. Discover packages (project files)
            var packages = DiscoverPackages(repoPath, repoName);
            var stats = new IngestStats();

            foreach (var pkg in packages)
            {
                conn.UpsertNodeById("Package", pkg.Id, new()
                {
                    ["id"]        = pkg.Id,
                    ["name"]      = pkg.Name,
                    ["ecosystem"] = pkg.Ecosystem,
                    ["version"]   = pkg.Version,
                });
                conn.UpsertRelationshipById("CONTAINS_PACKAGE", repoId, pkg.Id, new());
                stats.Packages++;

                // 3. Create Module for each namespace/directory
                var moduleId = $"mod:{repoName}/{pkg.Name}";
                conn.UpsertNodeById("Module", moduleId, new()
                {
                    ["id"]            = moduleId,
                    ["qualifiedName"] = pkg.Name,
                    ["filePath"]      = pkg.RootDir,
                    ["language"]      = pkg.Language,
                });
                conn.UpsertRelationshipById("CONTAINS_MODULE", pkg.Id, moduleId, new());
                stats.Modules++;

                // 4. Scan source files in this package
                var sourceFiles = DiscoverSourceFiles(pkg.RootDir);
                foreach (var filePath in sourceFiles)
                {
                    var relPath = Path.GetRelativePath(repoPath, filePath);
                    var ext = Path.GetExtension(filePath);
                    var lang = LanguageMap.GetValueOrDefault(ext, "unknown");
                    var lines = CountLines(filePath);
                    var hash = ComputeHash(filePath);

                    var fileId = $"file:{repoName}/{relPath}";
                    conn.UpsertNodeById("File", fileId, new()
                    {
                        ["id"]           = fileId,
                        ["path"]         = relPath,
                        ["language"]     = lang,
                        ["hash"]         = hash,
                        ["lastModified"] = new FileInfo(filePath).LastWriteTimeUtc.ToString("O"),
                        ["lineCount"]    = (long)lines,
                    });
                    conn.UpsertRelationshipById("CONTAINS_FILE", moduleId, fileId, new());
                    stats.Files++;

                    // 5. Extract symbols
                    var content = File.ReadAllText(filePath);
                    var symbols = ExtractSymbols(content, lang, repoName, relPath);
                    foreach (var sym in symbols)
                    {
                        conn.UpsertNodeById("Symbol", sym.Id, new()
                        {
                            ["id"]            = sym.Id,
                            ["name"]          = sym.Name,
                            ["qualifiedName"] = sym.QualifiedName,
                            ["kind"]          = sym.Kind,
                            ["signature"]     = sym.Signature,
                            ["docstring"]     = sym.Docstring,
                            ["startLine"]     = (long)sym.StartLine,
                            ["endLine"]       = (long)sym.EndLine,
                        });
                        conn.UpsertRelationshipById("DEFINES_SYMBOL", fileId, sym.Id, new()
                        {
                            ["startLine"] = (long)sym.StartLine,
                            ["endLine"]   = (long)sym.EndLine,
                        });
                        stats.Symbols++;
                    }
                }
            }

            conn.Commit();

            return new
            {
                success = true,
                repoName,
                repoPath,
                branch,
                stats = new
                {
                    stats.Packages,
                    stats.Modules,
                    stats.Files,
                    stats.Symbols,
                },
            };
        }
        catch (Exception ex)
        {
            try { conn.Rollback(); } catch { /* best effort */ }
            return new { success = false, error = ex.Message };
        }
    }

    // ── Package Discovery ────────────────────────────────────────────────────

    private static List<PackageInfo> DiscoverPackages(string repoPath, string repoName)
    {
        var packages = new List<PackageInfo>();

        // C# — .csproj files
        foreach (var proj in Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsIgnoredPath(proj)) continue;
            var name = Path.GetFileNameWithoutExtension(proj);
            packages.Add(new PackageInfo
            {
                Id        = $"pkg:{repoName}/{name}",
                Name      = name,
                Ecosystem = "nuget",
                Version   = "0.0.0",
                Language   = "csharp",
                RootDir   = Path.GetDirectoryName(proj) ?? repoPath,
            });
        }

        // TypeScript/JavaScript — package.json
        foreach (var pj in Directory.GetFiles(repoPath, "package.json", SearchOption.AllDirectories))
        {
            if (IsIgnoredPath(pj)) continue;
            var dir = Path.GetDirectoryName(pj) ?? repoPath;
            var name = Path.GetFileName(dir);
            try
            {
                var json = JsonDocument.Parse(File.ReadAllText(pj));
                if (json.RootElement.TryGetProperty("name", out var n))
                    name = n.GetString() ?? name;
            }
            catch { /* use dir name */ }
            packages.Add(new PackageInfo
            {
                Id        = $"pkg:{repoName}/{name}",
                Name      = name,
                Ecosystem = "npm",
                Version   = "0.0.0",
                Language   = "typescript",
                RootDir   = dir,
            });
        }

        // Python — setup.py / pyproject.toml
        foreach (var py in Directory.GetFiles(repoPath, "pyproject.toml", SearchOption.AllDirectories)
                     .Concat(Directory.GetFiles(repoPath, "setup.py", SearchOption.AllDirectories)))
        {
            if (IsIgnoredPath(py)) continue;
            var dir = Path.GetDirectoryName(py) ?? repoPath;
            var name = Path.GetFileName(dir);
            packages.Add(new PackageInfo
            {
                Id        = $"pkg:{repoName}/{name}",
                Name      = name,
                Ecosystem = "pip",
                Version   = "0.0.0",
                Language   = "python",
                RootDir   = dir,
            });
        }

        // Java — pom.xml
        foreach (var pom in Directory.GetFiles(repoPath, "pom.xml", SearchOption.AllDirectories))
        {
            if (IsIgnoredPath(pom)) continue;
            var dir = Path.GetDirectoryName(pom) ?? repoPath;
            var name = Path.GetFileName(dir);
            packages.Add(new PackageInfo
            {
                Id        = $"pkg:{repoName}/{name}",
                Name      = name,
                Ecosystem = "maven",
                Version   = "0.0.0",
                Language   = "java",
                RootDir   = dir,
            });
        }

        // Fallback: if no package manifests found, treat the whole repo as one package
        if (packages.Count == 0)
        {
            packages.Add(new PackageInfo
            {
                Id        = $"pkg:{repoName}/root",
                Name      = repoName,
                Ecosystem = "unknown",
                Version   = "0.0.0",
                Language   = "multi",
                RootDir   = repoPath,
            });
        }

        return packages;
    }

    // ── Source File Discovery ─────────────────────────────────────────────────

    private static List<string> DiscoverSourceFiles(string rootDir)
    {
        var files = new List<string>();
        foreach (var ext in LanguageMap.Keys)
        {
            try
            {
                files.AddRange(Directory.GetFiles(rootDir, $"*{ext}", SearchOption.AllDirectories)
                    .Where(f => !IsIgnoredPath(f)));
            }
            catch { /* permission or path issues */ }
        }
        return files;
    }

    // ── Symbol Extraction (convention-based regex) ────────────────────────────

    private static List<SymbolInfo> ExtractSymbols(string content, string language, string repoName, string relPath)
    {
        return language switch
        {
            "csharp"     => ExtractCSharpSymbols(content, repoName, relPath),
            "typescript" or "javascript" => ExtractTypeScriptSymbols(content, repoName, relPath),
            "python"     => ExtractPythonSymbols(content, repoName, relPath),
            "java"       => ExtractJavaSymbols(content, repoName, relPath),
            _            => new List<SymbolInfo>(),
        };
    }

    private static List<SymbolInfo> ExtractCSharpSymbols(string content, string repoName, string relPath)
    {
        var symbols = new List<SymbolInfo>();
        var lines = content.Split('\n');
        var ns = "";

        // Extract namespace
        var nsMatch = Regex.Match(content, @"namespace\s+([\w.]+)");
        if (nsMatch.Success) ns = nsMatch.Groups[1].Value;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Classes, records, structs, interfaces, enums
            var typeMatch = Regex.Match(line, @"(?:public|internal|private|protected)\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?<kind>class|record|struct|interface|enum)\s+(?<name>\w+)");
            if (typeMatch.Success)
            {
                var kind = typeMatch.Groups["kind"].Value;
                var name = typeMatch.Groups["name"].Value;
                var qn = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                symbols.Add(new SymbolInfo
                {
                    Id            = $"sym:{repoName}/{relPath}:{name}",
                    Name          = name,
                    QualifiedName = qn,
                    Kind          = kind == "record" ? "class" : kind,
                    Signature     = line.TrimEnd('{').Trim(),
                    Docstring     = ExtractXmlDocAbove(lines, i),
                    StartLine     = i + 1,
                    EndLine       = i + 1,
                });
            }

            // Methods
            var methodMatch = Regex.Match(line, @"(?:public|internal|private|protected)\s+(?:static\s+|async\s+|virtual\s+|override\s+|abstract\s+)*[\w<>\[\]?,\s]+\s+(?<name>\w+)\s*\(");
            if (methodMatch.Success && !typeMatch.Success)
            {
                var name = methodMatch.Groups["name"].Value;
                if (name is not ("if" or "else" or "while" or "for" or "foreach" or "switch" or "catch" or "using" or "return" or "new"))
                {
                    var qn = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
                    symbols.Add(new SymbolInfo
                    {
                        Id            = $"sym:{repoName}/{relPath}:{name}@{i + 1}",
                        Name          = name,
                        QualifiedName = qn,
                        Kind          = "method",
                        Signature     = line.TrimEnd('{').Trim(),
                        Docstring     = ExtractXmlDocAbove(lines, i),
                        StartLine     = i + 1,
                        EndLine       = i + 1,
                    });
                }
            }
        }

        return symbols;
    }

    private static List<SymbolInfo> ExtractTypeScriptSymbols(string content, string repoName, string relPath)
    {
        var symbols = new List<SymbolInfo>();
        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // export class/interface/type/enum
            var match = Regex.Match(line, @"(?:export\s+)?(?:default\s+)?(?<kind>class|interface|type|enum)\s+(?<name>\w+)");
            if (match.Success)
            {
                symbols.Add(new SymbolInfo
                {
                    Id            = $"sym:{repoName}/{relPath}:{match.Groups["name"].Value}",
                    Name          = match.Groups["name"].Value,
                    QualifiedName = $"{relPath}:{match.Groups["name"].Value}",
                    Kind          = match.Groups["kind"].Value,
                    Signature     = line.TrimEnd('{').Trim(),
                    StartLine     = i + 1,
                    EndLine       = i + 1,
                });
            }

            // export function / const
            var fnMatch = Regex.Match(line, @"(?:export\s+)?(?:async\s+)?function\s+(?<name>\w+)");
            if (fnMatch.Success)
            {
                symbols.Add(new SymbolInfo
                {
                    Id            = $"sym:{repoName}/{relPath}:{fnMatch.Groups["name"].Value}@{i + 1}",
                    Name          = fnMatch.Groups["name"].Value,
                    QualifiedName = $"{relPath}:{fnMatch.Groups["name"].Value}",
                    Kind          = "function",
                    Signature     = line.TrimEnd('{').Trim(),
                    StartLine     = i + 1,
                    EndLine       = i + 1,
                });
            }

            var constMatch = Regex.Match(line, @"(?:export\s+)?const\s+(?<name>\w+)\s*=");
            if (constMatch.Success && !fnMatch.Success)
            {
                symbols.Add(new SymbolInfo
                {
                    Id            = $"sym:{repoName}/{relPath}:{constMatch.Groups["name"].Value}@{i + 1}",
                    Name          = constMatch.Groups["name"].Value,
                    QualifiedName = $"{relPath}:{constMatch.Groups["name"].Value}",
                    Kind          = "const",
                    Signature     = line.Length > 120 ? line[..120] + "…" : line,
                    StartLine     = i + 1,
                    EndLine       = i + 1,
                });
            }
        }

        return symbols;
    }

    private static List<SymbolInfo> ExtractPythonSymbols(string content, string repoName, string relPath)
    {
        var symbols = new List<SymbolInfo>();
        var lines = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // class Foo:
            var classMatch = Regex.Match(line, @"^class\s+(?<name>\w+)");
            if (classMatch.Success)
            {
                symbols.Add(new SymbolInfo
                {
                    Id            = $"sym:{repoName}/{relPath}:{classMatch.Groups["name"].Value}",
                    Name          = classMatch.Groups["name"].Value,
                    QualifiedName = $"{relPath}:{classMatch.Groups["name"].Value}",
                    Kind          = "class",
                    Signature     = line.TrimEnd(':').Trim(),
                    Docstring     = ExtractPythonDocstring(lines, i),
                    StartLine     = i + 1,
                    EndLine       = i + 1,
                });
            }

            // def foo(... ):
            var defMatch = Regex.Match(line, @"^\s*(?:async\s+)?def\s+(?<name>\w+)\s*\(");
            if (defMatch.Success)
            {
                symbols.Add(new SymbolInfo
                {
                    Id            = $"sym:{repoName}/{relPath}:{defMatch.Groups["name"].Value}@{i + 1}",
                    Name          = defMatch.Groups["name"].Value,
                    QualifiedName = $"{relPath}:{defMatch.Groups["name"].Value}",
                    Kind          = "function",
                    Signature     = line.TrimEnd(':').Trim(),
                    Docstring     = ExtractPythonDocstring(lines, i),
                    StartLine     = i + 1,
                    EndLine       = i + 1,
                });
            }
        }

        return symbols;
    }

    private static List<SymbolInfo> ExtractJavaSymbols(string content, string repoName, string relPath)
    {
        var symbols = new List<SymbolInfo>();
        var lines = content.Split('\n');
        var pkg = "";

        var pkgMatch = Regex.Match(content, @"package\s+([\w.]+);");
        if (pkgMatch.Success) pkg = pkgMatch.Groups[1].Value;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // class, interface, enum, record
            var typeMatch = Regex.Match(line, @"(?:public|private|protected)\s+(?:static\s+|abstract\s+|final\s+)*(?<kind>class|interface|enum|record)\s+(?<name>\w+)");
            if (typeMatch.Success)
            {
                var name = typeMatch.Groups["name"].Value;
                var qn = string.IsNullOrEmpty(pkg) ? name : $"{pkg}.{name}";
                symbols.Add(new SymbolInfo
                {
                    Id            = $"sym:{repoName}/{relPath}:{name}",
                    Name          = name,
                    QualifiedName = qn,
                    Kind          = typeMatch.Groups["kind"].Value == "record" ? "class" : typeMatch.Groups["kind"].Value,
                    Signature     = line.TrimEnd('{').Trim(),
                    StartLine     = i + 1,
                    EndLine       = i + 1,
                });
            }

            // Method signatures
            var methodMatch = Regex.Match(line, @"(?:public|private|protected)\s+(?:static\s+|final\s+|synchronized\s+)*[\w<>\[\]?,\s]+\s+(?<name>\w+)\s*\(");
            if (methodMatch.Success && !typeMatch.Success)
            {
                var name = methodMatch.Groups["name"].Value;
                if (name is not ("if" or "else" or "while" or "for" or "switch" or "catch" or "return" or "new"))
                {
                    var qn = string.IsNullOrEmpty(pkg) ? name : $"{pkg}.{name}";
                    symbols.Add(new SymbolInfo
                    {
                        Id            = $"sym:{repoName}/{relPath}:{name}@{i + 1}",
                        Name          = name,
                        QualifiedName = qn,
                        Kind          = "method",
                        Signature     = line.TrimEnd('{').Trim(),
                        StartLine     = i + 1,
                        EndLine       = i + 1,
                    });
                }
            }
        }

        return symbols;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ExtractXmlDocAbove(string[] lines, int lineIndex)
    {
        var docLines = new List<string>();
        for (int i = lineIndex - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("///"))
                docLines.Insert(0, trimmed[3..].Trim());
            else if (trimmed.StartsWith("//") || string.IsNullOrWhiteSpace(trimmed))
                continue;
            else
                break;
        }
        return string.Join(" ", docLines);
    }

    private static string ExtractPythonDocstring(string[] lines, int lineIndex)
    {
        if (lineIndex + 1 >= lines.Length) return "";
        var nextLine = lines[lineIndex + 1].Trim();
        if (nextLine.StartsWith("\"\"\"") || nextLine.StartsWith("'''"))
        {
            var quote = nextLine[..3];
            if (nextLine.Length > 6 && nextLine.EndsWith(quote))
                return nextLine[3..^3].Trim();
            // Multi-line docstring
            var sb = new StringBuilder(nextLine[3..]);
            for (int i = lineIndex + 2; i < lines.Length && i < lineIndex + 20; i++)
            {
                if (lines[i].Trim().EndsWith(quote))
                {
                    sb.Append(' ');
                    sb.Append(lines[i].Trim().TrimEnd('\'', '"'));
                    break;
                }
                sb.Append(' ');
                sb.Append(lines[i].Trim());
            }
            return sb.ToString().Trim();
        }
        return "";
    }

    private static bool IsIgnoredPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/") || normalized.Contains("/obj/")
            || normalized.Contains("/node_modules/") || normalized.Contains("/.git/")
            || normalized.Contains("/__pycache__/") || normalized.Contains("/dist/")
            || normalized.Contains("/build/") || normalized.Contains("/.vs/")
            || normalized.Contains("/target/");
    }

    private static int CountLines(string filePath)
    {
        try { return File.ReadLines(filePath).Count(); }
        catch { return 0; }
    }

    private static string ComputeHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash)[..16].ToLowerInvariant();
        }
        catch { return ""; }
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? GetOptionalString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Escape(string s) => s.Replace("'", "\\'").Replace("\\", "\\\\");

    // ── Internal Types ───────────────────────────────────────────────────────

    private sealed class PackageInfo
    {
        public string Id        { get; init; } = "";
        public string Name      { get; init; } = "";
        public string Ecosystem { get; init; } = "";
        public string Version   { get; init; } = "";
        public string Language  { get; init; } = "";
        public string RootDir   { get; init; } = "";
    }

    private sealed class SymbolInfo
    {
        public string Id            { get; init; } = "";
        public string Name          { get; init; } = "";
        public string QualifiedName { get; init; } = "";
        public string Kind          { get; init; } = "";
        public string Signature     { get; init; } = "";
        public string Docstring     { get; init; } = "";
        public int    StartLine     { get; init; }
        public int    EndLine       { get; init; }
    }

    private sealed class IngestStats
    {
        public int Packages { get; set; }
        public int Modules  { get; set; }
        public int Files    { get; set; }
        public int Symbols  { get; set; }
    }
}
