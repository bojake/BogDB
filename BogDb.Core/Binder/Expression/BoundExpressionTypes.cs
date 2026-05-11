using BogDb.Core.Common;

namespace BogDb.Core.Binder;

/// <summary>
/// A bound literal value expression with a concrete type.
/// </summary>
public class LiteralExpression : Expression
{
    public object? Value { get; }

    public LiteralExpression(object? value, LogicalTypeID typeId)
        : base(ExpressionType.LITERAL, typeId)
    {
        Value = value;
    }
}

/// <summary>
/// A bound property access expression: `n.age` resolved to a specific property
/// on a specific QueryNode with a concrete return type from the catalog.
/// </summary>
public class PropertyExpression : Expression
{
    public string PropertyName { get; }
    public string NodeVariableName { get; }
    public string? TableName { get; }

    public PropertyExpression(
        string propertyName,
        string nodeVariableName,
        LogicalTypeID typeId,
        string? tableName = null)
        : base(ExpressionType.PROPERTY, typeId)
    {
        PropertyName = propertyName;
        NodeVariableName = nodeVariableName;
        TableName = tableName;
    }
}
