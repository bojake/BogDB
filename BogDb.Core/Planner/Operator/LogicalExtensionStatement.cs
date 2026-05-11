namespace BogDb.Core.Planner.Operator;

using BogDb.Core.Parser;

/// <summary>
/// Logical operator representing an extension lifecycle statement.
/// </summary>
public sealed class LogicalExtensionStatement : LogicalOperator
{
    public ExtensionStatement Statement { get; }

    public LogicalExtensionStatement(ExtensionStatement statement)
        : base(LogicalOperatorType.LOGICAL_EXTENSION)
    {
        Statement = statement;
    }

    public override string GetExpressionsForPrinting()
        => $"{Statement.Command} EXTENSION {Statement.ExtensionNameOrPath}";
}
