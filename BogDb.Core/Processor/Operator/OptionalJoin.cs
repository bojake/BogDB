using System;
using System.Collections.Generic;

namespace BogDb.Core.Processor.Operator;

/// <summary>
/// Implements LEFT OUTER JOIN (OPTIONAL MATCH) semantics using a hash-indexed
/// probe for O(N+M) performance instead of O(N×M) linear scan.
///
/// For each left row:
///   - If matches exist in the right-side hash table, emit merged rows
///   - If no match, emit the left row with null-extended right columns
///
/// C++ parity: hash_join.cpp with JoinType::LEFT join semantics.
/// </summary>
public sealed class OptionalJoin : PhysicalOperator
{
    private readonly PhysicalOperator _leftChild;
    private readonly PhysicalOperator _rightChild;
    private readonly IReadOnlyList<string> _sharedVariables;

    // Hash-indexed right side for O(1) probe per left row
    private Dictionary<int, List<ExecutionState>>? _rightIndex;
    private IEqualityComparer<StructuralJoinKey>? _keyComparer;

    private ExecutionState? _currentLeftState;
    private List<ExecutionState>? _currentMatches;
    private int _matchIndex;

    public OptionalJoin(
        PhysicalOperator leftChild,
        PhysicalOperator rightChild,
        IReadOnlyList<string> sharedVariables,
        uint id)
        : base(PhysicalOperatorType.OPTIONAL_JOIN, id)
    {
        _leftChild = leftChild;
        _rightChild = rightChild;
        _sharedVariables = sharedVariables;
        Children.Add(leftChild);
        Children.Add(rightChild);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_rightIndex == null)
            BuildRightIndex(context);

        while (true)
        {
            if (_currentLeftState == null)
            {
                if (!_leftChild.GetNextTuple(context))
                    return false;

                _currentLeftState = context.CaptureState();
                _currentMatches = ProbeRightIndex(_currentLeftState);
                _matchIndex = 0;
            }

            // No matches → emit left row with null-extended right (OPTIONAL semantics)
            if (_currentMatches == null || _currentMatches.Count == 0)
            {
                context.RestoreState(_currentLeftState);
                context.CurrentProjectionRow = null;
                _currentLeftState = null;
                return true;
            }

            // Emit next match
            if (_matchIndex < _currentMatches.Count)
            {
                var merged = ExecutionState.Merge(_currentLeftState, _currentMatches[_matchIndex++]);
                context.RestoreState(merged);
                context.CurrentProjectionRow = null;
                return true;
            }

            _currentLeftState = null;
            _currentMatches = null;
        }
    }

    // ── Hash-indexed build + probe ──────────────────────────────────────────

    private void BuildRightIndex(ExecutionContext context)
    {
        _rightIndex = new Dictionary<int, List<ExecutionState>>();

        while (_rightChild.GetNextTuple(context))
        {
            var state = context.CaptureState();
            var hash = ComputeJoinKeyHash(state, _sharedVariables);

            if (!_rightIndex.TryGetValue(hash, out var bucket))
            {
                bucket = new List<ExecutionState>();
                _rightIndex[hash] = bucket;
            }
            bucket.Add(state);
        }
    }

    private List<ExecutionState>? ProbeRightIndex(ExecutionState leftState)
    {
        if (_rightIndex == null || _sharedVariables.Count == 0)
            return null;

        var hash = ComputeJoinKeyHash(leftState, _sharedVariables);

        if (!_rightIndex.TryGetValue(hash, out var candidates))
            return null;

        // Filter candidates by structural equality (hash collisions)
        var matches = new List<ExecutionState>();
        foreach (var candidate in candidates)
        {
            if (AreStructurallyCompatible(leftState, candidate, _sharedVariables))
                matches.Add(candidate);
        }

        return matches.Count > 0 ? matches : null;
    }

    // ── Key computation ─────────────────────────────────────────────────────

    private static int ComputeJoinKeyHash(ExecutionState state, IReadOnlyList<string> variables)
    {
        if (state.CurrentVariableIds == null || variables.Count == 0)
            return 0;

        var hash = new HashCode();
        for (int i = 0; i < variables.Count; i++)
        {
            if (state.CurrentVariableIds.TryGetValue(variables[i], out var value))
                hash.Add(StructuralHash(value));
            else
                hash.Add(0);
        }
        return hash.ToHashCode();
    }

    private static bool AreStructurallyCompatible(
        ExecutionState leftState,
        ExecutionState rightState,
        IReadOnlyList<string> sharedVariables)
    {
        if (sharedVariables.Count == 0)
            return true;

        if (leftState.CurrentVariableIds == null || rightState.CurrentVariableIds == null)
            return false;

        foreach (var variable in sharedVariables)
        {
            if (!leftState.CurrentVariableIds.TryGetValue(variable, out var leftValue))
                return false;
            if (!rightState.CurrentVariableIds.TryGetValue(variable, out var rightValue))
                return false;
            if (!StructuralEquals(leftValue, rightValue))
                return false;
        }

        return true;
    }

    // ── Structural equality helpers ─────────────────────────────────────────

    internal static int StructuralHash(object? value)
    {
        if (value == null) return 0;

        if (value is System.Collections.IList list)
        {
            var h = new HashCode();
            h.Add(list.Count);
            foreach (var item in list)
                h.Add(StructuralHash(item));
            return h.ToHashCode();
        }

        if (value is IDictionary<string, object> dict)
        {
            var h = new HashCode();
            h.Add(dict.Count);
            foreach (var kv in dict)
            {
                h.Add(kv.Key);
                h.Add(StructuralHash(kv.Value));
            }
            return h.ToHashCode();
        }

        return value.GetHashCode();
    }

    internal static bool StructuralEquals(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null) return false;

        if (left is System.Collections.IList leftList && right is System.Collections.IList rightList)
        {
            if (leftList.Count != rightList.Count) return false;
            for (int i = 0; i < leftList.Count; i++)
            {
                if (!StructuralEquals(leftList[i], rightList[i])) return false;
            }
            return true;
        }

        if (left is IDictionary<string, object> leftDict && right is IDictionary<string, object> rightDict)
        {
            if (leftDict.Count != rightDict.Count) return false;
            foreach (var kv in leftDict)
            {
                if (!rightDict.TryGetValue(kv.Key, out var rv)) return false;
                if (!StructuralEquals(kv.Value, rv)) return false;
            }
            return true;
        }

        return Equals(left, right);
    }
}

/// <summary>Composite structural join key for hash-based join operators.</summary>
internal readonly struct StructuralJoinKey : IEquatable<StructuralJoinKey>
{
    public readonly object?[] Values;
    private readonly int _hash;

    public StructuralJoinKey(object?[] values)
    {
        Values = values;
        var h = new HashCode();
        foreach (var v in values)
            h.Add(OptionalJoin.StructuralHash(v));
        _hash = h.ToHashCode();
    }

    public bool Equals(StructuralJoinKey other)
    {
        if (Values.Length != other.Values.Length) return false;
        for (int i = 0; i < Values.Length; i++)
        {
            if (!OptionalJoin.StructuralEquals(Values[i], other.Values[i]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is StructuralJoinKey k && Equals(k);
    public override int GetHashCode() => _hash;
}
