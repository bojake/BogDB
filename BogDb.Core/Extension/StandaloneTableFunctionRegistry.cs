using System.Collections.Concurrent;
using System.Linq;

namespace BogDb.Core.Extension;

/// <summary>
/// Registry for standalone table functions contributed by extensions.
/// Kept distinct from the general table-function registry to match native shape.
/// </summary>
public sealed class StandaloneTableFunctionRegistry
{
    private readonly Func<string?>? _ownerAccessor;
    private readonly ConcurrentDictionary<string, ITableFunction> _functions =
        new(System.StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _owners =
        new(System.StringComparer.OrdinalIgnoreCase);

    public StandaloneTableFunctionRegistry(Func<string?>? ownerAccessor = null)
    {
        _ownerAccessor = ownerAccessor;
    }

    public void Register(ITableFunction function)
    {
        _functions[function.Name] = function;
        var owner = _ownerAccessor?.Invoke();
        if (!string.IsNullOrWhiteSpace(owner))
            _owners[function.Name] = owner;
        else
            _owners.TryRemove(function.Name, out _);
    }

    public bool TryGet(string name, out ITableFunction function)
        => _functions.TryGetValue(name, out function!);

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
