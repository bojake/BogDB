using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Sequence;

/// <summary>
/// Sequence functions: currval(name), nextval(name).
/// Implements an in-memory sequence counter store keyed by sequence name.
/// Each database instance shares the same ConcurrentDictionary for simplicity.
///
/// DDL (CREATE SEQUENCE) is not yet wired; sequences are auto-created on first nextval.
/// C++ parity: src/function/sequence/sequence_functions.cpp
/// </summary>
internal static class SequenceFunctions
{
    // Thread-safe in-memory sequence state: name → current value
    private static readonly ConcurrentDictionary<string, long> _sequences =
        new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    internal static void ResetAll()
    {
        _sequences.Clear();
    }

    internal static IEnumerable<(string name, long currentValue)> GetActiveSequences()
    {
        foreach (var kvp in _sequences)
            yield return (kvp.Key, kvp.Value);
    }

    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // nextval(seqName) — increment and return
        r["nextval"] = a =>
        {
            if (a.Length < 1) return null;
            var name = TypeCoercionHelper.ToBogDbString(a[0]) ?? string.Empty;
            return (object?)_sequences.AddOrUpdate(name, 1L, (_, prev) => prev + 1L);
        };

        // currval(seqName) — return current value without incrementing
        r["currval"] = a =>
        {
            if (a.Length < 1) return null;
            var name = TypeCoercionHelper.ToBogDbString(a[0]) ?? string.Empty;
            return _sequences.TryGetValue(name, out var val) ? (object?)val : null;
        };

        // setval(seqName, value) — reset sequence counter
        r["setval"] = a =>
        {
            if (a.Length < 2) return null;
            var name = TypeCoercionHelper.ToBogDbString(a[0]) ?? string.Empty;
            var value = TypeCoercionHelper.ToInt64(a[1]);
            _sequences[name] = value;
            return (object?)value;
        };
    }
}
