using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BogDb.Core.Function.Table;

/// <summary>
/// Table/catalog metadata functions — callable via CALL fn() RETURN * or RETURN fn().
/// </summary>
public static class TableFunctions
{
    public const string BogDbNgVersion = "0.9.0-ng";

    // Runtime settings store
    private static readonly Dictionary<string, string> _settings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["threads"] = "4",
            ["progress_bar"] = "false",
            ["checkpoint_threshold"] = "67108864",
            ["spill_to_disk"] = "true",
        };

    // Thread-local catalog context set by BogConnection.Query before each execution
    [ThreadStatic] private static Main.BogDatabase? _catalog;

    /// <summary>Called by BogConnection.Query() to make the active DB available to table functions.</summary>
    public static void SetCatalogContext(Main.BogDatabase db) => _catalog = db;

    public static void Register(Dictionary<string, Func<object?[], object?>> funcs)
    {
        funcs["db_version"] = _ => BogDbNgVersion;

        funcs["current_setting"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            return _settings.TryGetValue(args[0]!.ToString()!, out var v) ? v : null;
        };

        funcs["set_setting"] = args =>
        {
            if (args.Length < 2 || args[0] == null) return null;
            _settings[args[0]!.ToString()!] = args[1]?.ToString() ?? "";
            return "OK";
        };

        funcs["catalog_version"] = _ => 1L;

        // P1-053: catalog-wired
        funcs["show_tables_count"] = _ =>
        {
            if (_catalog == null) return 0L;
            return (long)(_catalog.NodeTables.Count + _catalog.RelTables.Count);
        };

        funcs["table_type"] = args =>
        {
            if (args.Length == 0 || args[0] == null || _catalog == null) return "unknown";
            var name = args[0]!.ToString()!;
            if (_catalog.NodeTables.ContainsKey(name)) return "NODE";
            if (_catalog.RelTables.ContainsKey(name)) return "REL";
            return "unknown";
        };

        funcs["show_tables"] = _ =>
        {
            if (_catalog == null) return "";
            var node = _catalog.NodeTables.Keys;
            var rel  = _catalog.RelTables.Keys;
            return string.Join(", ", node.Concat(rel).OrderBy(n => n));
        };

        funcs["show_functions_contains"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return false;
            return FunctionDispatcher.IsKnown(args[0]!.ToString()!);
        };

        funcs["file_info"] = args =>
        {
            if (args.Length == 0 || args[0] == null) return null;
            var path = args[0]!.ToString()!;
            try
            {
                var fi = new System.IO.FileInfo(path);
                return new Dictionary<string, object?>
                {
                    ["path"]      = fi.FullName,
                    ["size"]      = fi.Exists ? (object?)fi.Length : null,
                    ["exists"]    = fi.Exists,
                    ["extension"] = fi.Extension,
                };
            }
            catch { return null; }
        };

        funcs["storage_info"] = _ => new Dictionary<string, object?>
        {
            ["storage_type"] = "in-memory",
            ["compression"]  = "none",
            ["page_size"]    = 4096L,
        };
    }
}

