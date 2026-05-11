using BogDb.Core.Common;
using BogDbQueryResult = BogDb.Core.Main.QueryResult.QueryResult;
using BogDbLogicalType = BogDb.Core.Main.QueryResult.BogDbLogicalType;

namespace BogDb.Core.Main;

/// <summary>
/// C#-native prepared statement facade for embedded hosts.
/// This first slice validates parse/bind at prepare time and reuses the
/// existing parameterized query path for execution.
/// </summary>
public sealed class BogDbPreparedStatement : IDisposable
{
    private readonly BogConnection _connection;
    private readonly Dictionary<string, object?> _bindings;
    private readonly IReadOnlyDictionary<string, BogDbLogicalType> _parameterExpectedTypes;
    private bool _disposed;

    internal BogDbPreparedStatement(
        BogConnection connection,
        string query,
        bool isSuccess,
        string errorMessage,
        IReadOnlyList<string>? parameterNames = null,
        IReadOnlyList<QueryResult.BogDbColumnDescriptor>? resultColumns = null,
        IReadOnlyDictionary<string, BogDbLogicalType>? parameterExpectedTypes = null)
    {
        _connection = connection;
        Query = query;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        _bindings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        ParameterNames = parameterNames ?? Array.Empty<string>();
        ResultColumns = resultColumns ?? Array.Empty<QueryResult.BogDbColumnDescriptor>();
        _parameterExpectedTypes = parameterExpectedTypes ?? new Dictionary<string, BogDbLogicalType>(StringComparer.OrdinalIgnoreCase);
    }

    public string Query { get; }
    public bool IsSuccess { get; }
    public string ErrorMessage { get; }
    public IReadOnlyList<string> ParameterNames { get; }
    public IReadOnlyList<QueryResult.BogDbColumnDescriptor> ResultColumns { get; }
    public IReadOnlyDictionary<string, object?> Bindings => _bindings;
    public int ParameterCount => ParameterNames.Count;
    public int ResultColumnCount => ResultColumns.Count;
    public IReadOnlyList<string> ResultColumnNames => ResultColumns.Select(column => column.Name).ToArray();
    public IReadOnlyList<BogDbLogicalType> ResultColumnLogicalTypes => ResultColumns.Select(column => column.LogicalType).ToArray();
    public IReadOnlyList<string> BoundParameterNames => ParameterNames.Where(IsBound).ToArray();
    public IReadOnlyList<string> MissingParameterNames => ParameterNames.Where(name => !IsBound(name)).ToArray();
    public bool HasBindings => _bindings.Count > 0;
    public bool AreAllParametersBound => ParameterNames.All(IsBound);
    public IReadOnlyList<BogDbParameterDescriptor> Parameters =>
        ParameterNames
            .Select((name, ordinal) =>
            {
                var isBound = _bindings.TryGetValue(name, out var value);
                var expectedLogicalType = _parameterExpectedTypes.TryGetValue(name, out var expected)
                    ? expected
                    : BogDbLogicalType.FromId(BogDb.Core.Common.LogicalTypeID.ANY);
                var actualLogicalType = isBound
                    ? BogDbLogicalType.FromValue(value)
                    : expectedLogicalType;
                return new BogDbParameterDescriptor(
                    Name: name,
                    Ordinal: ordinal,
                    IsBound: isBound,
                    ExpectedLogicalType: expectedLogicalType,
                    LogicalType: actualLogicalType,
                    Value: value);
            })
            .ToArray();

    public BogDbPreparedStatement Bind(string name, object? value)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Parameter names cannot be empty.");

        var normalizedName = name[0] == '$' ? name[1..] : name;
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new InvalidOperationException("Parameter names cannot be empty.");

        var normalizedValue = TypeCoercionHelper.Normalize(value);
        if (_parameterExpectedTypes.TryGetValue(normalizedName, out var expectedLogicalType) &&
            !CanBindValueToExpectedType(normalizedValue, expectedLogicalType))
        {
            var actualLogicalType = BogDbLogicalType.FromValue(normalizedValue);
            throw new InvalidOperationException(
                $"Parameter '${normalizedName}' expects {expectedLogicalType.Name} but got {actualLogicalType.Name}.");
        }

        _bindings[normalizedName] = normalizedValue;
        return this;
    }

    public bool HasParameter(string name)
    {
        ThrowIfDisposed();
        var normalizedName = NormalizeParameterName(name);
        return ParameterNames.Any(parameterName =>
            string.Equals(parameterName, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsBound(string name)
    {
        ThrowIfDisposed();
        var normalizedName = NormalizeParameterName(name);
        return _bindings.ContainsKey(normalizedName);
    }

    public BogDbPreparedStatement Bind(IReadOnlyDictionary<string, object?> values)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(values);

        foreach (var (name, value) in values)
            Bind(name, value);

        return this;
    }

    public BogDbPreparedStatement ClearBindings()
    {
        ThrowIfDisposed();
        _bindings.Clear();
        return this;
    }

    public BogDbQueryResult Execute()
    {
        ThrowIfDisposed();
        if (!IsSuccess)
            return BogDbQueryResult.FromError(ErrorMessage);

        return _connection.Execute(this);
    }

    public Task<BogDbQueryResult> ExecuteAsync()
    {
        ThrowIfDisposed();
        if (!IsSuccess)
            return Task.FromResult(BogDbQueryResult.FromError(ErrorMessage));

        return _connection.ExecuteAsync(this);
    }

    public void Dispose()
    {
        _disposed = true;
        _bindings.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BogDbPreparedStatement));
    }

    private static string NormalizeParameterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Parameter names cannot be empty.");

        var normalizedName = name[0] == '$' ? name[1..] : name;
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new InvalidOperationException("Parameter names cannot be empty.");

        return normalizedName;
    }

    private static bool CanBindValueToExpectedType(object? normalizedValue, BogDbLogicalType expectedLogicalType)
    {
        if (normalizedValue == null || expectedLogicalType.IsAny)
            return true;

        var actualLogicalType = BogDbLogicalType.FromValue(normalizedValue);
        if (actualLogicalType.Id == expectedLogicalType.Id)
            return true;

        if (actualLogicalType.IsNumeric && expectedLogicalType.IsNumeric)
            return true;

        if (expectedLogicalType.Id == BogDb.Core.Common.LogicalTypeID.STRING)
            return true;

        if (normalizedValue is string)
        {
            return expectedLogicalType.Id is
                BogDb.Core.Common.LogicalTypeID.INTERVAL or
                BogDb.Core.Common.LogicalTypeID.DATE or
                BogDb.Core.Common.LogicalTypeID.TIMESTAMP or
                BogDb.Core.Common.LogicalTypeID.TIMESTAMP_SEC or
                BogDb.Core.Common.LogicalTypeID.TIMESTAMP_MS or
                BogDb.Core.Common.LogicalTypeID.TIMESTAMP_NS or
                BogDb.Core.Common.LogicalTypeID.TIMESTAMP_TZ;
        }

        return false;
    }
}
