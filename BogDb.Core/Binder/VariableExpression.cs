using BogDb.Core.Common;

namespace BogDb.Core.Binder;

/// <summary>
/// A bound expression representing a variable lookup.
/// </summary>
public class VariableExpression : Expression
{
    public string VariableName { get; }

    /// <summary>Set by Binder when this variable is a bound QueryNode.</summary>
    public QueryNode? QueryNode { get; set; }
    public QueryRel? QueryRel { get; set; }

    public VariableExpression(string variableName, LogicalTypeID dataType)
        : base(ExpressionType.VARIABLE, dataType)
    {
        VariableName = variableName;
    }
}
