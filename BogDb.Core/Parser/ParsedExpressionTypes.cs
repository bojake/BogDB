using System;
using System.Globalization;
using BogDb.Core.Common;

namespace BogDb.Core.Parser;

/// <summary>
/// A parsed literal value: integer, double, string, bool, or null.
/// Mirrors C++ ParsedLiteralExpression.
/// </summary>
public class ParsedLiteralExpression : ParsedExpression
{
    public object? Value { get; }
    public LogicalTypeID LiteralTypeId { get; }

    public ParsedLiteralExpression(long value, string rawName)
        : base(ExpressionType.LITERAL, rawName)
    {
        Value = value;
        LiteralTypeId = LogicalTypeID.INT64;
    }

    public ParsedLiteralExpression(double value, string rawName)
        : base(ExpressionType.LITERAL, rawName)
    {
        Value = value;
        LiteralTypeId = LogicalTypeID.DOUBLE;
    }

    public ParsedLiteralExpression(string value, string rawName)
        : base(ExpressionType.LITERAL, rawName)
    {
        Value = value;
        LiteralTypeId = LogicalTypeID.STRING;
    }

    public ParsedLiteralExpression(bool value, string rawName)
        : base(ExpressionType.LITERAL, rawName)
    {
        Value = value;
        LiteralTypeId = LogicalTypeID.BOOL;
    }

    public ParsedLiteralExpression(object? value, string rawName, LogicalTypeID literalTypeId)
        : base(ExpressionType.LITERAL, rawName)
    {
        Value = value;
        LiteralTypeId = literalTypeId;
    }

    /// <summary>NULL literal.</summary>
    public ParsedLiteralExpression(string rawName)
        : base(ExpressionType.LITERAL, rawName)
    {
        Value = null;
        LiteralTypeId = LogicalTypeID.ANY;
    }

    public override ParsedExpression Copy() => Value switch
    {
        long l => new ParsedLiteralExpression(l, _rawName),
        double d => new ParsedLiteralExpression(d, _rawName),
        string s => new ParsedLiteralExpression(s, _rawName),
        bool b => new ParsedLiteralExpression(b, _rawName),
        Dictionary<string, object?> sd => new ParsedLiteralExpression(
            new Dictionary<string, object?>(sd.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), StringComparer.OrdinalIgnoreCase),
            _rawName,
            LiteralTypeId),
        _ => new ParsedLiteralExpression(_rawName),
    };
}

/// <summary>
/// A parsed variable reference, e.g. `n`.
/// Mirrors C++ ParsedVariableExpression.
/// </summary>
public class ParsedVariableExpression : ParsedExpression
{
    public string VariableName { get; }

    public ParsedVariableExpression(string variableName, string rawName)
        : base(ExpressionType.VARIABLE, rawName)
    {
        VariableName = variableName;
    }

    public override ParsedExpression Copy() => new ParsedVariableExpression(VariableName, _rawName);
}

/// <summary>
/// A parsed parameter reference, e.g. `$name` or `$1`.
/// </summary>
public class ParsedParameterExpression : ParsedExpression
{
    public string ParameterName { get; }

    public ParsedParameterExpression(string parameterName, string rawName)
        : base(ExpressionType.PARAMETER, rawName)
    {
        ParameterName = parameterName;
    }

    public override ParsedExpression Copy() => new ParsedParameterExpression(ParameterName, _rawName);
}

/// <summary>
/// A parsed property access expression, e.g. `n.age`.
/// Mirrors C++ ParsedPropertyExpression.
/// </summary>
public class ParsedPropertyExpression : ParsedExpression
{
    public string PropertyName { get; }

    public ParsedPropertyExpression(string propertyName, ParsedExpression child, string rawName)
        : base(ExpressionType.PROPERTY, child, rawName)
    {
        PropertyName = propertyName;
    }

    public ParsedExpression GetChildExpression() => _children[0];
    public override ParsedExpression Copy() => new ParsedPropertyExpression(PropertyName, _children[0].Copy(), _rawName);
}

/// <summary>
/// A parsed function invocation, e.g. `count(n)`, `lower(n.name)`.
/// Mirrors C++ ParsedFunctionExpression.
/// </summary>
public class ParsedFunctionExpression : ParsedExpression
{
    public string FunctionName { get; }
    public bool IsDistinct { get; }

    public ParsedFunctionExpression(string functionName, string rawName, bool isDistinct = false)
        : base(ExpressionType.FUNCTION, rawName)
    {
        FunctionName = functionName.ToUpperInvariant();
        IsDistinct = isDistinct;
    }

    public void AddChild(ParsedExpression child) => _children.Add(child);

    public override ParsedExpression Copy()
    {
        var copy = new ParsedFunctionExpression(FunctionName, _rawName, IsDistinct);
        foreach (var c in _children)
            copy.AddChild(c.Copy());
        return copy;
    }
}

/// <summary>
/// A parsed quantifier expression, e.g. `ALL(x IN [1,2] WHERE x > 0)`.
/// </summary>
public class ParsedQuantifierExpression : ParsedExpression
{
    public string QuantifierName { get; }
    public string VariableName { get; }
    public ParsedExpression CollectionExpression => _children[0];
    public ParsedExpression PredicateExpression => _children[1];

    public ParsedQuantifierExpression(
        string quantifierName,
        string variableName,
        ParsedExpression collectionExpression,
        ParsedExpression predicateExpression,
        string rawName)
        : base(ExpressionType.LAMBDA, rawName)
    {
        QuantifierName = quantifierName.ToUpperInvariant();
        VariableName = variableName;
        _children.Add(collectionExpression);
        _children.Add(predicateExpression);
    }

    public override ParsedExpression Copy() => new ParsedQuantifierExpression(
        QuantifierName,
        VariableName,
        CollectionExpression.Copy(),
        PredicateExpression.Copy(),
        _rawName);
}

/// <summary>
/// A parsed lambda expression, e.g. <c>x -> x > 0</c> or <c>(a, b) -> a + b</c>.
/// Used as function arguments for list_filter, list_reduce, list_transform.
/// </summary>
public class ParsedLambdaExpression : ParsedExpression
{
    public List<string> ParameterNames { get; }
    public ParsedExpression Body => _children[0];

    public ParsedLambdaExpression(
        List<string> parameterNames,
        ParsedExpression bodyExpression,
        string rawName)
        : base(ExpressionType.LAMBDA, rawName)
    {
        ParameterNames = parameterNames;
        _children.Add(bodyExpression);
    }

    public override ParsedExpression Copy() => new ParsedLambdaExpression(
        new List<string>(ParameterNames),
        Body.Copy(),
        _rawName);
}
