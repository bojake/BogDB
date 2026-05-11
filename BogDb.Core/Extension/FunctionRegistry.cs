using System.Collections.Concurrent;
using System.Linq;

namespace BogDb.Core.Extension;

/// <summary>
/// Thread-safe registry mapping function names to their <see cref="ITableFunction"/> implementations.
/// Extensions call <see cref="Register"/> from <c>IExtension.Load()</c>.
/// The engine calls <see cref="TryGet"/> during query execution.
/// </summary>
public sealed class FunctionRegistry
{
    private readonly Func<string?>? _ownerAccessor;
    private readonly ConcurrentDictionary<string, ITableFunction> _functions =
        new(System.StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _owners =
        new(System.StringComparer.OrdinalIgnoreCase);

    public FunctionRegistry(Func<string?>? ownerAccessor = null)
    {
        _ownerAccessor = ownerAccessor;
    }

    /// <summary>Register a table function. Overwrites any prior registration with the same name.</summary>
    public void Register(ITableFunction function)
    {
        _functions[function.Name] = function;
        var owner = _ownerAccessor?.Invoke();
        if (!string.IsNullOrWhiteSpace(owner))
            _owners[function.Name] = owner;
        else
            _owners.TryRemove(function.Name, out _);
    }

    /// <summary>Try to retrieve a registered table function by name (case-insensitive).</summary>
    public bool TryGet(string name, out ITableFunction function)
        => _functions.TryGetValue(name, out function!);

    /// <summary>Returns true if a function with the given name is registered.</summary>
    public bool Contains(string name) => _functions.ContainsKey(name);

    public IReadOnlyCollection<string> GetRegisteredNames() => _functions.Keys.ToArray();

    public bool Unregister(string name)
    {
        _owners.TryRemove(name, out _);
        return _functions.TryRemove(name, out _);
    }

    public int UnregisterOwnedBy(string owner)
    {
        var removed = 0;
        foreach (var entry in _owners)
        {
            if (!string.Equals(entry.Value, owner, System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (Unregister(entry.Key))
                removed++;
        }

        return removed;
    }
}
