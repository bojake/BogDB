using BogDb.Core.Common;

namespace BogDb.Core.Parser;

public sealed class ExplainStatement : Statement
{
    public Statement StatementToExplain { get; }
    public ExplainType ExplainType { get; }

    public ExplainStatement(Statement statementToExplain, ExplainType explainType)
        : base(StatementType.EXPLAIN)
    {
        StatementToExplain = statementToExplain;
        ExplainType = explainType;
    }
}
