using System;
using System.Collections.Concurrent;
using BogDb.Core.Binder;

namespace BogDb.Core.Extension;

public delegate object? ContextAwareScalarFunction(
    BoundFunctionExpression function,
    object?[] evaluatedArguments,
    BogDb.Core.Processor.ExecutionContext context);

/// <summary>
/// Thread-safe registry of scalar functions contributed by extensions.
/// This is database-owned substrate for future parser/binder integration.
/// </summary>
public sealed class ScalarFunctionRegistry
{
    private readonly Func<string?>? _ownerAccessor;
    private readonly ConcurrentDictionary<string, Func<object?[], object?>> _functions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ContextAwareScalarFunction> _contextAwareFunctions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _owners =
        new(StringComparer.OrdinalIgnoreCase);

    public ScalarFunctionRegistry(Func<string?>? ownerAccessor = null)
    {
        _ownerAccessor = ownerAccessor;
    }

    public void Register(string name, Func<object?[], object?> function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        _functions[name] = function;
        _contextAwareFunctions.TryRemove(name, out _);
        var owner = _ownerAccessor?.Invoke();
        if (!string.IsNullOrWhiteSpace(owner))
            _owners[name] = owner;
        else
            _owners.TryRemove(name, out _);
    }

    public void RegisterContextAware(string name, ContextAwareScalarFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        _contextAwareFunctions[name] = function;
        _functions.TryRemove(name, out _);
        var owner = _ownerAccessor?.Invoke();
        if (!string.IsNullOrWhiteSpace(owner))
            _owners[name] = owner;
        else
            _owners.TryRemove(name, out _);
    }

    public bool TryGet(string name, out Func<object?[], object?> function)
        => _functions.TryGetValue(name, out function!);

    public bool TryGetContextAware(string name, out ContextAwareScalarFunction function)
        => _contextAwareFunctions.TryGetValue(name, out function!);

    public bool Contains(string name)
        => _functions.ContainsKey(name) || _contextAwareFunctions.ContainsKey(name);

    public bool Unregister(string name)
    {
        _owners.TryRemove(name, out _);
        var removed = _functions.TryRemove(name, out _);
        removed |= _contextAwareFunctions.TryRemove(name, out _);
        return removed;
    }

    public int UnregisterOwnedBy(string owner)
    {
        var removed = 0;
        foreach (var entry in _owners)
        {
            if (!string.Equals(entry.Value, owner, StringComparison.OrdinalIgnoreCase))
                continue;
            if (Unregister(entry.Key))
                removed++;
        }

        return removed;
    }
}
