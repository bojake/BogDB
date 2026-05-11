using BogDb.Core.Parser;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical operator representing a CREATE MACRO statement.
/// </summary>
public sealed class LogicalCreateMacro : LogicalOperator
{
    public string Name { get; }
    public IReadOnlyList<MacroParameter> Parameters { get; }
    public ParsedExpression BodyExpression { get; }

    public LogicalCreateMacro(string name, IReadOnlyList<MacroParameter> parameters, ParsedExpression bodyExpression)
        : base(LogicalOperatorType.LOGICAL_CREATE_MACRO)
    {
        Name = name;
        Parameters = parameters;
        BodyExpression = bodyExpression;
    }

    public override string GetExpressionsForPrinting()
        => $"CREATE MACRO {Name}";
}
