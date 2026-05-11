using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.Intersect;

/// <summary>
/// Multi-way intersection probe: for each row from the probe child, tests that the
/// current node ID exists in ALL build-side hash tables. Only emits rows that pass.
/// C++ parity: intersect.h — hash-probe intersection across adjacency list build tables.
/// </summary>
public sealed class Intersect : PhysicalOperator
{
    private readonly PhysicalOperator _probeChild;
    private readonly List<IntersectBuild> _buildChildren;
    private bool _buildInitialized;

    public Intersect(
        PhysicalOperator probeChild,
        List<IntersectBuild> buildChildren,
        int intersectKeyPos,
        uint id)
        : base(PhysicalOperatorType.INTERSECT, probeChild, id)
    {
        _probeChild = probeChild;
        _buildChildren = buildChildren;

        foreach (var build in _buildChildren)
            Children.Add(build);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        // On first call, drain all build pipelines to populate hash tables
        if (!_buildInitialized)
        {
            foreach (var build in _buildChildren)
                build.GetNextTuple(context); // sinks drain themselves
            _buildInitialized = true;
        }

        // Probe: emit only rows where key exists in every build table
        while (_probeChild.GetNextTuple(context))
        {
            var key = ResolveProbeKey(context);
            if (key == null) continue;

            bool matchedAll = true;
            foreach (var build in _buildChildren)
            {
                if (!build.ContainsKey(key.Value))
                {
                    matchedAll = false;
                    break;
                }
            }

            if (matchedAll) return true;
        }

        return false;
    }

    private long? ResolveProbeKey(ExecutionContext context)
    {
        if (context.CurrentNodeId is long lId) return lId;
        if (context.CurrentProjectionRow is { Length: > 0 })
        {
            var v = context.CurrentProjectionRow[0];
            if (v is long l) return l;
            if (v is int  i) return i;
        }
        return null;
    }
}
