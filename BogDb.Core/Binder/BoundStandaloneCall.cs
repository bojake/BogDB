using BogDb.Core.Common;

namespace BogDb.Core.Binder;

public sealed class BoundStandaloneCall : BoundStatement
{
    public string OptionName { get; }
    public Expression OptionValue { get; }
    public LogicalTypeID OptionType { get; }

    public BoundStandaloneCall(string optionName, Expression optionValue, LogicalTypeID optionType)
        : base(StatementType.STANDALONE_CALL)
    {
        OptionName = optionName;
        OptionValue = optionValue;
        OptionType = optionType;
    }
}
