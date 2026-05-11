using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

public sealed class LogicalStandaloneCall : LogicalOperator
{
    public string OptionName { get; }
    public Expression OptionValue { get; }

    public LogicalStandaloneCall(string optionName, Expression optionValue)
        : base(LogicalOperatorType.LOGICAL_STANDALONE_CALL)
    {
        OptionName = optionName;
        OptionValue = optionValue;
    }

    public override string GetExpressionsForPrinting()
        => $"{OptionName} = {OptionValue}";
}
