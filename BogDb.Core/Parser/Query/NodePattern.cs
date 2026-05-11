using System.Collections.Generic;

namespace BogDb.Core.Parser;

/// <summary>
/// A parsed node pattern like (n:Person {age: 25}).
/// Mirrors C++ NodePattern from parser/query/graph_pattern/node_pattern.h
/// </summary>
public class NodePattern
{
    public string VariableName { get; }
    public IReadOnlyList<string> TableNames { get; }
    public IReadOnlyList<(string Key, ParsedExpression Value)> PropertyKeyValues { get; }
    public ParsedExpression? PropertyBagExpression { get; }

    public NodePattern(
        string variableName,
        List<string> tableNames,
        List<(string, ParsedExpression)> propertyKeyValues,
        ParsedExpression? propertyBagExpression = null)
    {
        VariableName = variableName;
        TableNames = tableNames;
        PropertyKeyValues = propertyKeyValues;
        PropertyBagExpression = propertyBagExpression;
    }
}
