using System.Collections.Generic;
using System.Collections;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Main.QueryResult;

/// <summary>
/// Packages Physical pipeline output chunk aggregations into 
/// an interactive reader schema bridging .NET C# apps to the Native core.
/// </summary>
public sealed class QueryResult : IEnumerable<BogRow>
{
    private readonly Processor.Operator.Result.ResultCollector? _resultCollector;
    private int _currentRowIndex;
    private readonly List<BogRow> _rows;
    private readonly bool _isSuccess;
    private readonly string _errorMessage;
    private string[] _columnNames;
    private LogicalTypeID[] _columnTypes;
    private readonly double? _profileMilliseconds;
    private double? _totalMilliseconds;
    private BogDbQueryExecutionMetrics? _executionMetrics;

    public QueryResult(Processor.Operator.Result.ResultCollector resultCollector)
        : this(resultCollector, null)
    {
    }

    public QueryResult(
        Processor.Operator.Result.ResultCollector resultCollector,
        IReadOnlyList<LogicalTypeID>? declaredColumnTypes)
    {
        _resultCollector = resultCollector;
        _currentRowIndex = 0;
        _isSuccess = true;
        _errorMessage = string.Empty;
        _profileMilliseconds = resultCollector.ProfileMilliseconds;
        _totalMilliseconds = _profileMilliseconds;

        // Extract column names from the collector (empty array if not set).
        var colNames = resultCollector.ColumnNames.Count > 0
            ? resultCollector.ColumnNames as string[] ?? System.Linq.Enumerable.ToArray(resultCollector.ColumnNames)
            : System.Array.Empty<string>();
        if (colNames.Length == 0)
            colNames = InferColumnNames(resultCollector.Rows);
        _columnNames = colNames;
        _columnTypes = ResolveColumnTypes(resultCollector.Rows, colNames.Length, declaredColumnTypes);

        _rows = new List<BogRow>();
        foreach (var row in _resultCollector.Rows)
        {
            if (colNames.Length > 0 && TryUnwrapDictionaryRow(row, colNames, out var dict))
            {
                var values = new object[colNames.Length];
                for (var i = 0; i < colNames.Length; i++)
                    values[i] = dict.TryGetValue(colNames[i], out var value) ? value! : DBNull.Value;
                _rows.Add(new BogRow(values, colNames));
                continue;
            }

            _rows.Add(colNames.Length > 0 ? new BogRow(row, colNames) : new BogRow(row));
        }
    }

    private static string[] InferColumnNames(IReadOnlyList<object[]> rows)
    {
        foreach (var row in rows)
        {
            if (TryUnwrapDictionaryRow(row, null, out var dict))
                return dict.Keys.ToArray();
        }

        return System.Array.Empty<string>();
    }

    private static bool TryUnwrapDictionaryRow(
        object[] row,
        IReadOnlyList<string>? columnNames,
        out IReadOnlyDictionary<string, object?> dictionary)
    {
        if (row.Length == 1)
        {
            if (row[0] is IReadOnlyDictionary<string, object?> roDict)
            {
                if (!ShouldUnwrapDictionaryRow(roDict, columnNames))
                {
                    dictionary = null!;
                    return false;
                }

                dictionary = roDict;
                return true;
            }

            if (row[0] is Dictionary<string, object?> dict)
            {
                if (!ShouldUnwrapDictionaryRow(dict, columnNames))
                {
                    dictionary = null!;
                    return false;
                }

                dictionary = dict;
                return true;
            }
        }

        dictionary = null!;
        return false;
    }

    private static bool ShouldUnwrapDictionaryRow(
        IReadOnlyDictionary<string, object?> dictionary,
        IReadOnlyList<string>? columnNames)
    {
        if (columnNames == null || columnNames.Count == 0)
            return true;

        foreach (var columnName in columnNames)
        {
            if (!dictionary.ContainsKey(columnName))
                return false;
        }

        return true;
    }

    private QueryResult(string errorMessage, bool isSuccess = false)
    {
        _resultCollector = null;
        _currentRowIndex = 0;
        _isSuccess = isSuccess;
        _errorMessage = isSuccess ? string.Empty : errorMessage;
        _rows = new List<BogRow>();
        _columnNames = System.Array.Empty<string>();
        _columnTypes = System.Array.Empty<LogicalTypeID>();
        _profileMilliseconds = null;
        _totalMilliseconds = null;
    }

    public static QueryResult FromError(string errorMessage) => new QueryResult(errorMessage, isSuccess: false);

    /// <summary>
    /// Creates a successful QueryResult from table-function rows (used by LOAD FROM extension path).
    /// Each dictionary is wrapped as a BogRow whose GetAsDictionary() returns the map.
    /// </summary>
    public static QueryResult FromTableFunctionRows(
        IEnumerable<Dictionary<string, object?>> rows)
    {
        var result = new QueryResult("table_function_ok", isSuccess: true);
        var materialized = rows.ToList();
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in materialized)
        {
            foreach (var key in row.Keys)
            {
                if (seen.Add(key))
                    columns.Add(key);
            }
        }

        result._columnNames = columns.ToArray();
        result._columnTypes = result.InferColumnTypesFromMaterializedRows();

        foreach (var row in materialized)
        {
            if (result._columnNames.Length == 0)
            {
                result._rows.Add(new BogRow(new object[] { row! }));
                continue;
            }

            var values = new object[result._columnNames.Length];
            for (int i = 0; i < result._columnNames.Length; i++)
                values[i] = row.TryGetValue(result._columnNames[i], out var v) ? v! : DBNull.Value;
            result._rows.Add(new BogRow(values, result._columnNames));
        }

        return result;
    }

    /// <summary>
    /// Creates a QueryResult from ordered row dictionaries with explicit column ordering.
    /// Each BogRow has its values in the order of <paramref name="columns"/>,
    /// so GetValue(i) / GetString(i) / GetInt64(i) correctly index individual columns.
    /// Used by window function results.
    /// </summary>
    public static QueryResult FromOrderedRows(
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<string> columns,
        IReadOnlyList<LogicalTypeID>? declaredColumnTypes = null)
    {
        var result = new QueryResult("ordered_rows_ok", isSuccess: true);
        var columnArray = columns as string[] ?? columns.ToArray();
        result._columnNames = columnArray;
        foreach (var row in rows)
        {
            var values = new object[columns.Count];
            for (int i = 0; i < columns.Count; i++)
                values[i] = row.TryGetValue(columns[i], out var v) ? v! : (object)DBNull.Value;
            result._rows.Add(new BogRow(values, columnArray));
        }
        result._columnTypes = ResolveColumnTypes(
            result._rows.Select(row => Enumerable.Range(0, columnArray.Length).Select(row.GetValue).ToArray()).ToArray(),
            columnArray.Length,
            declaredColumnTypes);
        return result;
    }

    public bool IsSuccess => _isSuccess;
    public string ErrorMessage => _errorMessage;
    public double? ProfileMilliseconds => _profileMilliseconds;
    public double? TotalMilliseconds => _totalMilliseconds;
    public BogDbQueryExecutionMetrics? ExecutionMetrics => _executionMetrics;
    public int ColumnCount => _columnNames.Length;

    /// <summary>Column alias names in projection order. Empty when column names are unavailable (e.g. transaction results).</summary>
    public IReadOnlyList<string> ColumnNames =>
        _columnNames;

    public IReadOnlyList<LogicalTypeID> ColumnTypes => _columnTypes;

    public IReadOnlyList<BogDbLogicalType> ColumnLogicalTypes =>
        Enumerable.Range(0, _columnNames.Length)
            .Select(GetColumnLogicalType)
            .ToArray();

    public IReadOnlyList<BogDbColumnDescriptor> Columns =>
        _columnNames
            .Select((name, ordinal) => new BogDbColumnDescriptor(
                Name: name,
                Ordinal: ordinal,
                LogicalType: ordinal < _columnTypes.Length
                    ? GetColumnLogicalType(ordinal)
                    : BogDbLogicalType.FromId(LogicalTypeID.ANY)))
            .ToArray();

    public BogDbQuerySummary Summary =>
        new(
            IsSuccess: _isSuccess,
            ErrorMessage: _errorMessage,
            ColumnCount: ColumnCount,
            RowCount: GetNumTuples(),
            ProfileMilliseconds: _profileMilliseconds,
            TotalMilliseconds: _totalMilliseconds,
            ExecutionMetrics: _executionMetrics);

    internal void AttachExecutionMetrics(BogDbQueryExecutionMetrics? executionMetrics)
    {
        _executionMetrics = executionMetrics;
        if (executionMetrics != null)
        {
            _totalMilliseconds = executionMetrics.TotalMilliseconds;
        }
    }

    internal void AttachTotalMilliseconds(double totalMilliseconds)
    {
        _totalMilliseconds = totalMilliseconds;
    }

    public int GetColumnIndex(string columnName)
    {
        if (!TryGetColumnIndex(columnName, out var index))
            throw new KeyNotFoundException($"Column '{columnName}' was not found in the query result.");

        return index;
    }

    public bool TryGetColumnIndex(string columnName, out int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        for (var i = 0; i < _columnNames.Length; i++)
        {
            if (string.Equals(_columnNames[i], columnName, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    public void ResetIterator()
    {
        if (!_isSuccess)
            throw new InvalidOperationException($"Cannot reset a failed query result: {_errorMessage}");

        _currentRowIndex = 0;
    }

    public bool HasNext()
    {
        if (!_isSuccess) return false;
        return _currentRowIndex < _rows.Count;
    }

    public BogRow GetNext()
    {
        if (!_isSuccess)
            throw new InvalidOperationException($"Cannot read rows from failed query result: {_errorMessage}");
        if (_currentRowIndex >= _rows.Count)
            throw new InvalidOperationException("No more rows available in query result.");

        var row = _rows[_currentRowIndex];
        _currentRowIndex++;
        return row;
    }

    public ulong GetNumTuples() => (ulong)_rows.Count;

    /// <summary>
    /// All rows as a read-only list. Does not consume the iterator.
    /// </summary>
    public IReadOnlyList<BogRow> Rows => _rows;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the query failed.
    /// Returns this for fluent chaining.
    /// </summary>
    public QueryResult ThrowIfFailed()
    {
        if (!_isSuccess)
            throw new InvalidOperationException($"Query failed: {_errorMessage}");
        return this;
    }

    /// <summary>
    /// Executes <paramref name="action"/> for each row. Returns this for chaining.
    /// </summary>
    public QueryResult ForEach(Action<BogRow> action)
    {
        ThrowIfFailed();
        foreach (var row in _rows)
            action(row);
        return this;
    }

    /// <summary>
    /// Projects each row to <typeparamref name="T"/> via <paramref name="selector"/>.
    /// </summary>
    public List<T> Select<T>(Func<BogRow, T> selector)
    {
        ThrowIfFailed();
        var result = new List<T>(_rows.Count);
        foreach (var row in _rows)
            result.Add(selector(row));
        return result;
    }

    /// <summary>
    /// Returns all rows as a materialized list.
    /// </summary>
    public List<BogRow> ToRowList()
    {
        ThrowIfFailed();
        return new List<BogRow>(_rows);
    }

    /// <summary>
    /// Returns the first row, or null if empty.
    /// </summary>
    public BogRow? FirstOrDefault()
    {
        if (!_isSuccess || _rows.Count == 0) return null;
        return _rows[0];
    }

    /// <summary>
    /// Returns the scalar value from the first column of the first row.
    /// Useful for COUNT(*), SUM(), etc.
    /// </summary>
    public T Scalar<T>()
    {
        ThrowIfFailed();
        if (_rows.Count == 0)
            throw new InvalidOperationException("Query returned no rows.");
        return _rows[0].Get<T>(0)!;
    }

    // ── IEnumerable<BogRow> ─────────────────────────────────────────────

    public IEnumerator<BogRow> GetEnumerator() => _rows.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static LogicalTypeID[] InferColumnTypes(IReadOnlyList<object[]> rows, int columnCount)
    {
        if (columnCount <= 0)
            return System.Array.Empty<LogicalTypeID>();

        var result = new LogicalTypeID[columnCount];
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            result[columnIndex] = LogicalTypeID.ANY;

        foreach (var row in rows)
        {
            for (var columnIndex = 0; columnIndex < columnCount && columnIndex < row.Length; columnIndex++)
            {
                if (result[columnIndex] != LogicalTypeID.ANY)
                    continue;

                var inferred = InferLogicalTypeForMetadata(row[columnIndex]);
                if (inferred != LogicalTypeID.ANY)
                    result[columnIndex] = inferred;
            }
        }

        return result;
    }

    private LogicalTypeID[] InferColumnTypesFromMaterializedRows()
    {
        if (_columnNames.Length == 0)
            return System.Array.Empty<LogicalTypeID>();

        var rawRows = _rows
            .Select(row => Enumerable.Range(0, _columnNames.Length).Select(row.GetValue).ToArray())
            .ToArray();
        return InferColumnTypes(rawRows, _columnNames.Length);
    }

    private BogDbLogicalType GetColumnLogicalType(int ordinal)
    {
        var declared = ordinal < _columnTypes.Length
            ? _columnTypes[ordinal]
            : LogicalTypeID.ANY;

        foreach (var row in _rows)
        {
            if (ordinal >= row.Count)
                continue;

            var value = row.GetValue(ordinal);
            if (value is null or DBNull)
                continue;

            var inferred = BogDbLogicalType.FromValue(value);
            return declared != LogicalTypeID.ANY && inferred.Id == LogicalTypeID.ANY
                ? BogDbLogicalType.FromId(declared)
                : declared != LogicalTypeID.ANY && inferred.Id != declared
                    ? new BogDbLogicalType(declared, inferred.ElementType, inferred.KeyType, inferred.ValueType, inferred.Fields)
                    : inferred;
        }

        return BogDbLogicalType.FromId(declared);
    }

    private static LogicalTypeID[] ResolveColumnTypes(
        IReadOnlyList<object[]> rows,
        int columnCount,
        IReadOnlyList<LogicalTypeID>? declaredColumnTypes)
    {
        var inferred = InferColumnTypes(rows, columnCount);
        if (declaredColumnTypes == null || declaredColumnTypes.Count == 0)
            return inferred;

        var resolved = new LogicalTypeID[columnCount];
        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var declared = columnIndex < declaredColumnTypes.Count
                ? declaredColumnTypes[columnIndex]
                : LogicalTypeID.ANY;
            resolved[columnIndex] = declared != LogicalTypeID.ANY
                ? declared
                : inferred[columnIndex];
        }

        return resolved;
    }

    internal static LogicalTypeID InferLogicalTypeForMetadata(object? value)
    {
        value = TypeCoercionHelper.Normalize(value);
        if (value is null or DBNull)
            return LogicalTypeID.ANY;

        return value switch
        {
            bool => LogicalTypeID.BOOL,
            long => LogicalTypeID.INT64,
            int => LogicalTypeID.INT32,
            short => LogicalTypeID.INT16,
            sbyte => LogicalTypeID.INT8,
            ulong => LogicalTypeID.UINT64,
            uint => LogicalTypeID.UINT32,
            ushort => LogicalTypeID.UINT16,
            byte => LogicalTypeID.UINT8,
            double => LogicalTypeID.DOUBLE,
            float => LogicalTypeID.FLOAT,
            string => LogicalTypeID.STRING,
            DateTimeOffset => LogicalTypeID.TIMESTAMP_TZ,
            DateTime => LogicalTypeID.TIMESTAMP,
            BogDbInterval => LogicalTypeID.INTERVAL,
            InternalID => LogicalTypeID.INTERNAL_ID,
            IDictionary => LogicalTypeID.STRUCT,
            IEnumerable<KeyValuePair<string, object?>> => LogicalTypeID.STRUCT,
            IEnumerable<KeyValuePair<object?, object?>> => LogicalTypeID.MAP,
            IEnumerable sequence when sequence is not string && sequence is not byte[] => LogicalTypeID.LIST,
            _ => LogicalTypeID.ANY
        };
    }
}
