namespace BogDb.Core.Planner.Operator;

using BogDb.Core.Binder;

public sealed class LogicalAttachDatabase : LogicalOperator
{
    public BoundAttachDatabase Statement { get; }

    public LogicalAttachDatabase(BoundAttachDatabase statement)
        : base(LogicalOperatorType.LOGICAL_ATTACH_DATABASE)
    {
        Statement = statement;
    }

    public override string GetExpressionsForPrinting()
        => $"ATTACH {Statement.Path} AS {Statement.Alias} (dbtype {Statement.DbType})";
}
