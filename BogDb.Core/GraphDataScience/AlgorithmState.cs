using System.Collections.Concurrent;
using BogDb.Core.Common;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// Contains execution states wrapping cyclic boundary evaluations preventing infinite algorithmic traversals safely securely.
/// Defines algorithm-specific parameters (e.g Damping, Tolerances) internally scaling execution dynamically.
/// </summary>
public class AlgorithmState
{
    // Common properties spanning SSSP, WCC, and PageRank inherently.
    public int MaxIterations { get; }
    public int CurrentIteration { get; private set; }
    public Frontier CurrentFrontier { get; private set; }
    public Frontier NextFrontier { get; private set; }
    
    // Abstract property store scaling node-specific outcomes (e.g float for PageRank, int for WCC/SSSP distances)
    private readonly ConcurrentDictionary<ulong, object> _nodeProperties;

    public AlgorithmState(int maxIterations)
    {
        MaxIterations = maxIterations;
        CurrentIteration = 0;
        CurrentFrontier = new Frontier();
        NextFrontier = new Frontier();
        _nodeProperties = new ConcurrentDictionary<ulong, object>();
    }

    public void AdvanceIteration()
    {
        CurrentIteration++;
        CurrentFrontier = NextFrontier;
        NextFrontier = new Frontier(); // Reset targeting the following iterations gracefully securely.
    }

    public bool IsComplete()
    {
        return CurrentIteration >= MaxIterations || !CurrentFrontier.HasActiveNodes;
    }

    public void SetProperty(InternalID nodeID, object value)
    {
        _nodeProperties[nodeID.Offset] = value;
    }

    public T? GetProperty<T>(InternalID nodeID) where T : struct
    {
        if (_nodeProperties.TryGetValue(nodeID.Offset, out var val))
        {
            return (T)val;
        }
        return null;
    }
}
