using System;
using System.Collections.Generic;
using System.Linq;
using BogDbMath = BogDb.Core.Function.Mathematics;

namespace BogDb.Core.Function;

/// <summary>
/// Central scalar function dispatch table.
/// All functions are keyed by their lowercase canonical name.
/// Unknown function names return null instead of throwing.
/// C++ parity: replaces per-category function factories in src/function/.
/// </summary>
internal static class FunctionDispatcher
{
    private static readonly Dictionary<string, Func<object?[], object?>> _funcs
        = new(StringComparer.OrdinalIgnoreCase);

    private static bool _registered;
    private static readonly object _lock = new();

    static FunctionDispatcher() => RegisterAll();

    private static void RegisterAll()
    {
        lock (_lock)
        {
            if (_registered) return;
            _registered = true;

            Arithmetic.ArithmeticFunctions.Register(_funcs);
            BogDbMath.MathFunctions.Register(_funcs);
            String.StringFunctions.Register(_funcs);
            Date.DateFunctions.Register(_funcs);
            List.ListFunctions.Register(_funcs);
            Cast.CastFunctions.Register(_funcs);
            Utility.UtilityFunctions.Register(_funcs);
            Blob.BlobFunctions.Register(_funcs);
            Map.MapFunctions.Register(_funcs);
            Struct.StructFunctions.Register(_funcs);
            Union.UnionFunctions.Register(_funcs);
            // New categories (Phase: missing-function port sprint)
            Path.PathFunctions.Register(_funcs);
            Array.ArrayFunctions.Register(_funcs);
            Pattern.PatternFunctions.Register(_funcs);
            Timestamp.TimestampFunctions.Register(_funcs);
            Sequence.SequenceFunctions.Register(_funcs);
            Interval.IntervalFunctions.Register(_funcs);
            // P1-010: Previously missing C++ function categories
            Uuid.UuidFunctions.Register(_funcs);
            InternalId.InternalIdFunctions.Register(_funcs);
            Table.TableFunctions.Register(_funcs);
            Export.ExportFunctions.Register(_funcs);
            Gds.GdsFunctions.Register(_funcs);
        }
    }

    /// <summary>
    /// Invoke a registered function by name, returning null for unknown names.
    /// </summary>
    internal static object? Invoke(string name, object?[] args)
    {
        var key = name.ToLowerInvariant();
        return _funcs.TryGetValue(key, out var fn) ? fn(args) : null;
    }

    /// <summary>Returns true if a function with this name is registered.</summary>
    internal static bool IsKnown(string name)
        => _funcs.ContainsKey(name.ToLowerInvariant());

    /// <summary>Returns all registered function names for introspection.</summary>
    internal static IReadOnlyCollection<string> GetRegisteredNames()
        => _funcs.Keys.ToArray();
}
