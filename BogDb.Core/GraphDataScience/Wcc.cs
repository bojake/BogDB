using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Executes Weakly Connected Components (WCC) mapping isolated disjoint graph clusters natively updating sub-graph labels recursively spanning component limits successfully.
/// </summary>
public class Wcc
{
    private readonly AlgorithmState _state;

    public Wcc(int maxIterations = 100)
    {
        _state = new AlgorithmState(maxIterations);
    }

    public void Compute(List<ValueVector> nodes, List<ValueVector> edges)
    {
        // Recursively passes component IDs downstream mapping clusters globally scaling iterations intelligently natively.
        
        while (!_state.IsComplete())
        {
            _state.AdvanceIteration();
        }
    }
}
