using BogDb.Core.Common;

namespace BogDb.Core.Binder;

/// <summary>
/// Bound representation of a standalone CALL function invocation.
/// Mirrors C++ BoundStandaloneCallFunction in a simplified form.
/// </summary>
public sealed class BoundStandaloneCallFunction : BoundStatement
{
    public BoundFunctionExpression FunctionExpression { get; }

    public BoundStandaloneCallFunction(BoundFunctionExpression functionExpression)
        : base(StatementType.STANDALONE_CALL_FUNCTION)
    {
        FunctionExpression = functionExpression;
    }
}
