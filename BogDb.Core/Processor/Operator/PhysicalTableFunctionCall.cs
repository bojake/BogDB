using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BogDb.Core.Binder;
using BogDb.Core.Common;
using BogDb.Core.Extension;
using BogDb.Core.GraphDataScience;
using BogDb.Core.Processor;
using BogDb.Core.Processor.Operator.Persistent.Reader.CSV;

namespace BogDb.Core.Processor.Operator;

/// <summary>
/// Physical operator executing CALL fn() YIELD * and LOAD FROM extension dispatch.
///
/// Dispatch order:
///   1. FunctionRegistry (registered by loaded extensions)
///   2. Built-in GDS algorithms (page_rank, wcc, sssp) — legacy fallback.
///
/// Rows are surfaced one at a time through context.CurrentProjectionRow,
/// exactly like other physical operators (PhysicalProjection, ScanNodeProperty, etc.).
/// Each row is a Dictionary&lt;string, object?&gt; boxed in a single-element object[].
/// </summary>
public class PhysicalTableFunctionCall : PhysicalOperator
{
    private readonly Expression _functionExpression;
    private readonly IReadOnlyList<Expression> _outVariables;
    private readonly FunctionRegistry? _registry;
    private readonly StandaloneTableFunctionRegistry? _standaloneRegistry;

    private bool _initialised = false;
    private List<Dictionary<string, object?>>? _rows;
    private int _rowIndex = 0;

    public PhysicalTableFunctionCall(
        Expression functionExpression,
        IReadOnlyList<Expression> outVariables,
        PhysicalOperator? child,
        uint id,
        FunctionRegistry? registry = null,
        StandaloneTableFunctionRegistry? standaloneRegistry = null)
        : base(PhysicalOperatorType.TABLE_FUNCTION_CALL, child!, id)
    {
        _functionExpression = functionExpression;
        _outVariables = outVariables;
        _registry = registry;
        _standaloneRegistry = standaloneRegistry;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_functionExpression is not BoundFunctionExpression boundFunc)
            return false;

        // ── Lazy initialise: invoke the function once, buffer all rows ────────
        if (!_initialised)
        {
            _initialised = true;
            _rows = InvokeFunction(boundFunc, context);
            _rowIndex = 0;
        }

        if (_rows == null || _rowIndex >= _rows.Count)
        {
            context.CurrentProjectionRow = null;
            return false;
        }

        // ── Expose the current row through CurrentProjectionRow ───────────────
        var dict = _rows[_rowIndex++];
        context.CurrentScalarBindings ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in dict)
            context.CurrentScalarBindings[key] = value;

        // Flatten the dictionary into an object[] keyed by out-variable position,
        // or as a single-element array with the dict for GetAsDictionary() support.
        context.CurrentProjectionRow = new object[] { dict! };
        return true;
    }

    // ── Function dispatch ─────────────────────────────────────────────────────

    private List<Dictionary<string, object?>> InvokeFunction(BoundFunctionExpression boundFunc, ExecutionContext context)
    {
        var functionName = boundFunc.FunctionName.ToLowerInvariant();
        var args = ExtractArguments(boundFunc.Arguments, context);
        Extension.TableFunctionRuntimeContext.Set(context.Database, context.Transaction);

        try
        {
            if (functionName == "load_from")
                return InvokeLoadFrom(args, context);

            // 1. Extension registry — highest priority
            if (_standaloneRegistry != null && _standaloneRegistry.TryGet(functionName, out var standaloneFn))
            {
                return standaloneFn.Invoke(args).ToList();
            }

            if (_registry != null && _registry.TryGet(functionName, out var fn))
            {
                return fn.Invoke(args).ToList();
            }

            // 2. Built-in GDS algorithms — backward-compatible legacy stub path
            var mockNodes = new List<Common.ValueVector>();
            var mockEdges = new List<Common.ValueVector>();
            switch (functionName)
            {
                case "page_rank": new PageRank().Compute(mockNodes, mockEdges); break;
                case "wcc":       new Wcc().Compute(mockNodes, mockEdges);      break;
                case "sssp":      new Sssp().Compute(mockNodes, mockEdges);     break;
            }

            // GDS stubs do not produce row output in the current implementation
            return new List<Dictionary<string, object?>>();
        }
        finally
        {
            Extension.TableFunctionRuntimeContext.Clear();
        }
    }

    private List<Dictionary<string, object?>> InvokeLoadFrom(
        IReadOnlyList<object?> args,
        ExecutionContext context)
    {
        if (args.Count == 0 || args[0] is not string source || string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("LOAD FROM requires a string source argument.");

        if (IsCsvSource(source))
            return InvokeLoadFromCsv(source, args.Skip(1).ToList(), context);
        if (IsAttachedSource(source))
            return InvokeLoadFromAttached(source, context);

        var targetFunctionName = ResolveLoadFromFunctionName(source);

        if (_standaloneRegistry != null && _standaloneRegistry.TryGet(targetFunctionName, out var standaloneFn))
            return standaloneFn.Invoke(new object?[] { source }).ToList();

        if (_registry != null && _registry.TryGet(targetFunctionName, out var fn))
            return fn.Invoke(new object?[] { source }).ToList();

        throw new InvalidOperationException(
            $"No table function '{targetFunctionName}' registered. Load the appropriate extension first.");
    }

    private List<Dictionary<string, object?>> InvokeLoadFromAttached(string source, ExecutionContext context)
    {
        var database = context.Database
            ?? throw new InvalidOperationException("Attached LOAD FROM requires database context.");
        var payload = source["attached:".Length..];
        var pipeIndex = payload.IndexOf('|');
        var alias = pipeIndex >= 0 ? payload[..pipeIndex] : payload;
        var tableName = pipeIndex >= 0 ? payload[(pipeIndex + 1)..] : null;

        if (!database.TryGetAttachedDatabase(alias, out var attachedDatabase))
            throw new InvalidOperationException($"Attached database '{alias}' is not registered.");
        if (!database.TryResolveStorageExtension(attachedDatabase.DbType, out var storageExtension))
            throw new InvalidOperationException($"No storage extension is available for attached database type '{attachedDatabase.DbType}'.");

        return storageExtension.Scan(attachedDatabase, string.IsNullOrWhiteSpace(tableName) ? null : tableName).ToList();
    }

    private static bool IsAttachedSource(string source)
        => source.StartsWith("attached:", StringComparison.OrdinalIgnoreCase);

    private static string ResolveLoadFromFunctionName(string source)
    {
        if (IsDuckDbSource(source))
            return "scan_duckdb";
        if (IsSqliteSource(source))
            return "scan_sqlite";
        return "scan_json_array";
    }

    private static bool IsDuckDbSource(string source)
    {
        var dbPath = source.Contains('|') ? source[..source.LastIndexOf('|')] : source;
        var extension = Path.GetExtension(dbPath);
        return extension.Equals(".duckdb", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ddb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSqliteSource(string source)
    {
        var dbPath = source.Contains('|') ? source[..source.LastIndexOf('|')] : source;
        var extension = Path.GetExtension(dbPath);
        return extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".db", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCsvSource(string source)
    {
        var path = source.Contains('|') ? source[..source.LastIndexOf('|')] : source;
        return Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);
    }

    private List<Dictionary<string, object?>> InvokeLoadFromCsv(
        string source,
        IReadOnlyList<object?> schemaArgs,
        ExecutionContext context)
    {
        if (schemaArgs.Count % 2 != 0)
            throw new InvalidOperationException("LOAD FROM typed CSV schema must contain name/type pairs.");

        var database = context.Database
            ?? throw new InvalidOperationException("CSV LOAD FROM requires database context.");
        var header = CsvFileAccess.ReadCsvHeader(database, source);
        var typeMap = BuildCsvTypeMap(header, schemaArgs);
        var rows = new List<Dictionary<string, object?>>();

        using var reader = CsvFileAccess.OpenReader(database, source);
        foreach (var cells in reader.ReadAllRows(header.Count))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < header.Count; i++)
            {
                var columnName = header[i];
                var rawValue = i < cells.Length ? cells[i] : null;
                row[columnName] = ParseCsvValue(rawValue, typeMap[columnName]);
            }
            rows.Add(row);
        }

        return rows;
    }

    private static Dictionary<string, LogicalTypeID> BuildCsvTypeMap(
        IReadOnlyList<string> header,
        IReadOnlyList<object?> schemaArgs)
    {
        var typeMap = new Dictionary<string, LogicalTypeID>(StringComparer.Ordinal);
        foreach (var column in header)
            typeMap[column] = LogicalTypeID.STRING;

        for (var i = 0; i < schemaArgs.Count; i += 2)
        {
            if (schemaArgs[i] is not string columnName || schemaArgs[i + 1] is not string typeName)
                throw new InvalidOperationException("LOAD FROM typed CSV schema must contain string name/type pairs.");
            if (!typeMap.ContainsKey(columnName))
                throw new InvalidOperationException($"CSV source does not contain declared column '{columnName}'.");
            if (!Enum.TryParse<LogicalTypeID>(typeName, ignoreCase: true, out var typeId))
                throw new InvalidOperationException($"Unsupported LOAD FROM type: {typeName}");
            typeMap[columnName] = typeId;
        }

        return typeMap;
    }

    private static object? ParseCsvValue(string? rawValue, LogicalTypeID typeId)
    {
        if (string.IsNullOrEmpty(rawValue))
            return DBNull.Value;

        return typeId switch
        {
            LogicalTypeID.INT64 => long.Parse(rawValue, CultureInfo.InvariantCulture),
            LogicalTypeID.INT32 => int.Parse(rawValue, CultureInfo.InvariantCulture),
            LogicalTypeID.DOUBLE => double.Parse(rawValue, CultureInfo.InvariantCulture),
            LogicalTypeID.FLOAT => float.Parse(rawValue, CultureInfo.InvariantCulture),
            LogicalTypeID.BOOL => bool.Parse(rawValue),
            _ => rawValue
        };
    }

    // ── Argument extraction ───────────────────────────────────────────────────

    private static IReadOnlyList<object?> ExtractArguments(
        IReadOnlyList<Expression> expressions,
        ExecutionContext context)
    {
        var result = new List<object?>(expressions.Count);
        foreach (var expr in expressions)
        {
            var value = ExpressionExecutionHelper.Evaluate(expr, context);
            result.Add(expr.HasAlias()
                ? new NamedFunctionArgument(expr.GetAlias(), value)
                : value);
        }
        return result;
    }
}
