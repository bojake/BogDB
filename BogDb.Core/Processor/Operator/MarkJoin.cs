using System;
using System.Collections.Generic;

namespace BogDb.Core.Processor.Operator;

/// <summary>
/// Mark join operator implementing semi-join (EXISTS) and anti-join (NOT EXISTS)
/// semantics. For each probe-side row, emits the row exactly once with a boolean
/// mark column indicating whether a match exists in the build-side hash table.
///
/// Unlike ValueHashJoin which can emit M×N rows (cartesian per key), MarkJoin
/// always emits exactly one row per probe row — the mark boolean is the only
/// additional output.
///
/// C++ parity: hash_join with JoinType::MARK — used to evaluate EXISTS/NOT EXISTS
/// subqueries at the join level without materializing matched rows.
/// </summary>
public sealed class MarkJoin : PhysicalOperator
{
    private readonly PhysicalOperator _probeChild;
    private readonly PhysicalOperator _buildChild;
    private readonly IReadOnlyList<string> _sharedVariables;
    private readonly string _markVariableName;

    // Build-side hash index
    private Dictionary<int, List<ExecutionState>>? _buildIndex;

    public MarkJoin(
        PhysicalOperator probeChild,
        PhysicalOperator buildChild,
        IReadOnlyList<string> sharedVariables,
        string markVariableName,
        uint id)
        : base(PhysicalOperatorType.MARK_JOIN, id)
    {
        _probeChild = probeChild;
        _buildChild = buildChild;
        _sharedVariables = sharedVariables;
        _markVariableName = markVariableName;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        _buildIndex ??= BuildHashIndex(context);

        if (!_probeChild.GetNextTuple(context))
            return false;

        var probeState = context.CaptureState();
        bool hasMark = ProbeForExistence(probeState);

        // Restore probe state and inject mark variable into scalar bindings
        // (scalar bindings is the channel used for runtime-generated values like
        // UNWIND aliases, so the expression evaluator can resolve the mark via
        // GetVariableValue → CurrentScalarBindings)
        context.RestoreState(probeState);
        context.CurrentScalarBindings ??= new Dictionary<string, object?>();
        context.CurrentScalarBindings[_markVariableName] = hasMark;
        context.CurrentProjectionRow = null;

        return true;
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

    private bool ProbeForExistence(ExecutionState probeState)
    {
        if (_buildIndex == null || _sharedVariables.Count == 0)
            return _buildIndex != null && _buildIndex.Count > 0;

        var hash = ComputeStructuralHash(probeState, _sharedVariables);

        if (!_buildIndex.TryGetValue(hash, out var candidates))
            return false;

        // Check structural equality for at least one match
        foreach (var candidate in candidates)
        {
            if (IsStructuralMatch(probeState, candidate, _sharedVariables))
                return true;
        }

        return false;
    }

    // ── Key computation (delegates to shared structural helpers) ─────────

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
