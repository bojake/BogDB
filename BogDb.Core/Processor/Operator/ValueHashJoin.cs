using System;
using System.Collections.Generic;

namespace BogDb.Core.Processor.Operator;

/// <summary>
/// Hash join operator using structural composite keys for probe/build matching.
/// Replaces the previous string-concat key approach with HashCode.Combine +
/// structural equality to safely handle null, list, map, and nested type values.
///
/// C++ parity: hash_join_probe.cpp + hash_join_build.cpp — the primary equi-join
/// operator for multi-pattern MATCH clause binding across shared variables.
/// </summary>
public sealed class ValueHashJoin : PhysicalOperator
{
    private readonly PhysicalOperator _probeChild;
    private readonly PhysicalOperator _buildChild;
    private readonly IReadOnlyList<string> _sharedVariables;

    // Build-side hash index: structural hash → bucket of matching states
    private Dictionary<int, List<ExecutionState>>? _buildIndex;

    private ExecutionState? _currentProbeState;
    private List<ExecutionState>? _currentMatches;
    private int _matchIndex;

    public ValueHashJoin(
        PhysicalOperator probeChild,
        PhysicalOperator buildChild,
        IReadOnlyList<string> sharedVariables,
        uint id)
        : base(PhysicalOperatorType.HASH_JOIN, id)
    {
        _probeChild = probeChild;
        _buildChild = buildChild;
        _sharedVariables = sharedVariables;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        _buildIndex ??= BuildHashIndex(context);

        while (true)
        {
            if (_currentProbeState == null)
            {
                if (!_probeChild.GetNextTuple(context))
                    return false;

                _currentProbeState = context.CaptureState();
                _currentMatches = ProbeIndex(_currentProbeState);
                _matchIndex = 0;
            }

            while (_currentMatches != null && _matchIndex < _currentMatches.Count)
            {
                var merged = ExecutionState.Merge(_currentProbeState, _currentMatches[_matchIndex++]);
                context.RestoreState(merged);
                context.CurrentProjectionRow = null;
                return true;
            }

            _currentProbeState = null;
            _currentMatches = null;
        }
    }

    // ── Build phase ─────────────────────────────────────────────────────────

    private Dictionary<int, List<ExecutionState>> BuildHashIndex(ExecutionContext context)
    {
        var index = new Dictionary<int, List<ExecutionState>>();

        while (_buildChild.GetNextTuple(context))
        {
            var state = context.CaptureState();
            var hash = ComputeStructuralHash(state, _sharedVariables);

            if (!index.TryGetValue(hash, out var bucket))
            {
                bucket = new List<ExecutionState>();
                index[hash] = bucket;
            }
            bucket.Add(state);
        }

        return index;
    }

    // ── Probe phase ─────────────────────────────────────────────────────────

    private List<ExecutionState>? ProbeIndex(ExecutionState probeState)
    {
        if (_buildIndex == null) return null;

        var hash = ComputeStructuralHash(probeState, _sharedVariables);

        if (!_buildIndex.TryGetValue(hash, out var candidates))
            return null;

        // Filter by structural equality to handle hash collisions
        if (candidates.Count == 1)
        {
            // Fast path: single candidate, check directly
            return IsStructuralMatch(probeState, candidates[0], _sharedVariables)
                ? candidates
                : null;
        }

        var matches = new List<ExecutionState>();
        foreach (var candidate in candidates)
        {
            if (IsStructuralMatch(probeState, candidate, _sharedVariables))
                matches.Add(candidate);
        }

        return matches.Count > 0 ? matches : null;
    }

    // ── Key hashing & equality ──────────────────────────────────────────────

    private static int ComputeStructuralHash(ExecutionState state, IReadOnlyList<string> variables)
    {
        if (state.CurrentVariableIds == null)
            return 0;

        var hash = new HashCode();
        for (int i = 0; i < variables.Count; i++)
        {
            if (state.CurrentVariableIds.TryGetValue(variables[i], out var value))
                hash.Add(OptionalJoin.StructuralHash(value));
            else
                hash.Add(0);
        }
        return hash.ToHashCode();
    }

    private static bool IsStructuralMatch(
        ExecutionState probe,
        ExecutionState build,
        IReadOnlyList<string> variables)
    {
        if (probe.CurrentVariableIds == null || build.CurrentVariableIds == null)
            return false;

        for (int i = 0; i < variables.Count; i++)
        {
            var variable = variables[i];
            if (!probe.CurrentVariableIds.TryGetValue(variable, out var probeValue))
                return false;
            if (!build.CurrentVariableIds.TryGetValue(variable, out var buildValue))
                return false;
            if (!OptionalJoin.StructuralEquals(probeValue, buildValue))
                return false;
        }

        return true;
    }
}
