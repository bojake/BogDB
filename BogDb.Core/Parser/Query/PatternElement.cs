using System.Collections.Generic;

namespace BogDb.Core.Parser;

/// <summary>
/// A pattern element is a node optionally followed by rel-node chains.
/// For Phase 8 we only support single-node patterns.
/// Mirrors C++ PatternElement from parser/query/graph_pattern/pattern_element.h
/// </summary>
public class PatternElement
{
    private readonly NodePattern _firstNode;
    private readonly List<PatternElementChain> _chains = new();
    private string? _pathName;

    public PatternElement(NodePattern firstNode)
    {
        _firstNode = firstNode;
    }

    public void AddPatternElementChain(PatternElementChain chain) => _chains.Add(chain);
    public void SetPathName(string name) => _pathName = name;

    public NodePattern GetFirstNodePattern() => _firstNode;
    public int GetNumPatternElementChains() => _chains.Count;
    public PatternElementChain GetPatternElementChain(int idx) => _chains[idx];
    public bool HasPathName() => _pathName != null;
    public string GetPathName() => _pathName!;
}

/// <summary>
/// A rel-node pair extending a PatternElement chain: -[r:REL]->(m:Label)
/// Not used in Phase 8 (single-node only) but defined for completeness.
/// </summary>
public class PatternElementChain
{
    public RelPattern RelPattern { get; }
    public NodePattern NodePattern { get; }

    public PatternElementChain(RelPattern relPattern, NodePattern nodePattern)
    {
        RelPattern = relPattern;
        NodePattern = nodePattern;
    }
}
