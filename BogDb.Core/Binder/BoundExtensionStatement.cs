using BogDb.Core.Common;

namespace BogDb.Core.Binder;

public sealed class BoundExtensionStatement : BoundStatement
{
    public Parser.ExtensionStatement Statement { get; }

    public BoundExtensionStatement(Parser.ExtensionStatement statement)
        : base(StatementType.EXTENSION)
    {
        Statement = statement;
    }
}
