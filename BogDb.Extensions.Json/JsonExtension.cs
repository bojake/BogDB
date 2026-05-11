using BogDb.Core.Extension;
using BogDb.Core.Main;

namespace BogDb.Extensions.Json;

/// <summary>
/// JSON extension — C++ parity with bogdb-master/extension/json.
/// Provides 17 scalar functions, 1 table function (JSON_SCAN / LOAD FROM json).
/// </summary>
public class JsonExtension : IExtension
{
    public string Name => "json";

    public void Load(BogDatabase database)
    {
        // ── Table function: scan/load JSON arrays ────────────────────────
        var scanJsonArray = new ScanJsonArrayTableFunction(database);
        database.FunctionRegistry.Register(scanJsonArray);
        database.StandaloneTableFunctionRegistry.Register(scanJsonArray);

        // ── Scalar functions ─────────────────────────────────────────────
        var r = database.ScalarFunctionRegistry;

        // Existing 4
        r.Register("json_valid",        JsonScalarFunctions.JsonValid);
        r.Register("json_array_length", JsonScalarFunctions.JsonArrayLength);
        r.Register("json_type",         JsonScalarFunctions.JsonType);
        r.Register("json_extract",      JsonScalarFunctions.JsonExtract);

        // New 13 — C++ parity completion
        r.Register("json_keys",         JsonScalarFunctions.JsonKeys);
        r.Register("json_contains",     JsonScalarFunctions.JsonContains);
        r.Register("json_merge_patch",  JsonScalarFunctions.JsonMergePatch);
        r.Register("json_array",        JsonScalarFunctions.JsonArray);
        r.Register("json_object",       JsonScalarFunctions.JsonObject);
        r.Register("json_quote",        JsonScalarFunctions.JsonQuote);
        r.Register("json_structure",    JsonScalarFunctions.JsonStructure);
        r.Register("to_json",           JsonScalarFunctions.ToJson);
        r.Register("cast_to_json",      JsonScalarFunctions.ToJson);
        r.Register("row_to_json",       JsonScalarFunctions.RowToJson);
        r.Register("array_to_json",     JsonScalarFunctions.ArrayToJson);

        // json() alias — casts a string to validated JSON (returns as-is if valid)
        r.Register("json", args =>
        {
            if (args.Length == 0 || args[0] is null) return null;
            var s = args[0]?.ToString();
            if (s == null) return null;
            try { using var doc = System.Text.Json.JsonDocument.Parse(s); return s; }
            catch { return null; }
        });
    }
}
