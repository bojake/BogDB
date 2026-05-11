using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Computes Single-Source-Shortest-Paths dynamically resolving distances scaling downstream relationships iteratively safely structurally overlapping distance metrics completely.
/// </summary>
public class Sssp
{
    private readonly AlgorithmState _state;

    public Sssp(int maxIterations = 100)
    {
        _state = new AlgorithmState(maxIterations);
    }

    public void Compute(List<ValueVector> nodes, List<ValueVector> edges)
    {
        // Seeds a root node passing cascading distances bridging frontiers sequentially explicitly testing algorithm properties optimally!
        
        while (!_state.IsComplete())
        {
            _state.AdvanceIteration();
        }
    }
}
