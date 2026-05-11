using BogDb.Core.Binder;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator;

public sealed class PhysicalStandaloneCall : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly string _optionName;
    private readonly Expression _optionValue;
    private readonly LogicalTypeID _optionType;
    private bool _executed;

    public PhysicalStandaloneCall(
        Main.BogDatabase database,
        string optionName,
        Expression optionValue,
        LogicalTypeID optionType,
        uint id)
        : base(PhysicalOperatorType.STANDALONE_CALL, id)
    {
        _database = database;
        _optionName = optionName;
        _optionValue = optionValue;
        _optionType = optionType;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_executed)
            return false;

        _executed = true;
        var value = TypeCoercionHelper.Normalize(
            ExpressionExecutionHelper.Evaluate(_optionValue, context));
        _database.SetExtensionOption(_optionName, CoerceValue(value, _optionType));
        return false;
    }

    private static object? CoerceValue(object? value, LogicalTypeID optionType)
    {
        if (value is null)
            return null;

        return optionType switch
        {
            LogicalTypeID.BOOL => System.Convert.ToBoolean(value),
            LogicalTypeID.INT16 => System.Convert.ToInt16(value),
            LogicalTypeID.INT32 => System.Convert.ToInt32(value),
            LogicalTypeID.INT64 => System.Convert.ToInt64(value),
            LogicalTypeID.FLOAT => System.Convert.ToSingle(value),
            LogicalTypeID.DOUBLE => System.Convert.ToDouble(value),
            LogicalTypeID.STRING => System.Convert.ToString(value),
            _ => value
        };
    }
}
