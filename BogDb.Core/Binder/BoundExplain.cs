using BogDb.Core.Common;

namespace BogDb.Core.Binder;

public sealed class BoundExplain : BoundStatement
{
    public BoundStatement StatementToExplain { get; }
    public ExplainType ExplainType { get; }

    public BoundExplain(BoundStatement statementToExplain, ExplainType explainType)
        : base(StatementType.EXPLAIN)
    {
        StatementToExplain = statementToExplain;
        ExplainType = explainType;
    }
}
