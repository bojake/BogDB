using BogDb.Core.Common;

namespace BogDb.Core.Parser;

public enum TransactionCommand : byte
{
    BEGIN,
    COMMIT,
    ROLLBACK,
    CHECKPOINT
}

public sealed class TransactionStatement : Statement
{
    public TransactionCommand Command { get; }
    public bool IsReadOnly { get; }

    public TransactionStatement(TransactionCommand command, bool isReadOnly = false)
        : base(StatementType.TRANSACTION)
    {
        Command = command;
        IsReadOnly = isReadOnly;
    }
}
