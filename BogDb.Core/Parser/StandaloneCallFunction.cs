using BogDb.Core.Common;

namespace BogDb.Core.Parser;

/// <summary>
/// Represents a standalone CALL function invocation: CALL func(args).
/// Mirrors C++ StandaloneCallFunction.
/// </summary>
public sealed class StandaloneCallFunction : Statement
{
    public ParsedExpression FunctionExpression { get; }

    public StandaloneCallFunction(ParsedExpression functionExpression)
        : base(StatementType.STANDALONE_CALL_FUNCTION)
    {
        FunctionExpression = functionExpression;
    }
}
