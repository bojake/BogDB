using System.Collections.Generic;

namespace BogDb.Core.Parser;

public enum ArrowDirection { LEFT, RIGHT, BOTH }

/// <summary>
/// A parsed relationship pattern like -[r:KNOWS]-> 
/// Minimal definition for Phase 8; recursive/weighted rels are Phase 9+.
/// </summary>
public class RelPattern
{
    public string VariableName { get; }
    public IReadOnlyList<string> RelTypes { get; }
    public ArrowDirection Direction { get; }
    public IReadOnlyList<(string Key, ParsedExpression Value)> PropertyKeyValues { get; }
    public ParsedExpression? PropertyBagExpression { get; }
    public string LowerBound { get; }
    public string UpperBound { get; }

    public RelPattern(
        string variableName,
        List<string> relTypes,
        ArrowDirection direction,
        List<(string, ParsedExpression)> propertyKeyValues,
        ParsedExpression? propertyBagExpression = null,
        string lowerBound = "1",
        string upperBound = "1")
    {
        VariableName = variableName;
        RelTypes = relTypes;
        Direction = direction;
        PropertyKeyValues = propertyKeyValues;
        PropertyBagExpression = propertyBagExpression;
        LowerBound = lowerBound;
        UpperBound = upperBound;
    }
}
