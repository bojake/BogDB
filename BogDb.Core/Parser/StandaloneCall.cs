using BogDb.Core.Common;

namespace BogDb.Core.Parser;

/// <summary>
/// Represents a standalone CALL option assignment: CALL option = expr.
/// Mirrors C++ StandaloneCall.
/// </summary>
public sealed class StandaloneCall : Statement
{
    public string OptionName { get; }
    public ParsedExpression OptionValue { get; }

    public StandaloneCall(string optionName, ParsedExpression optionValue)
        : base(StatementType.STANDALONE_CALL)
    {
        OptionName = optionName;
        OptionValue = optionValue;
    }
}
