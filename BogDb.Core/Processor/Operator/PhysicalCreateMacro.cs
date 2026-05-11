using BogDb.Core.Catalog;
using BogDb.Core.Parser;

namespace BogDb.Core.Processor.Operator;

/// <summary>
/// Executes CREATE MACRO once and produces no output tuples.
/// </summary>
public sealed class PhysicalCreateMacro : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly string _name;
    private readonly IReadOnlyList<MacroParameter> _parameters;
    private readonly ParsedExpression _bodyExpression;
    private bool _executed;

    public PhysicalCreateMacro(
        Main.BogDatabase database,
        string name,
        IReadOnlyList<MacroParameter> parameters,
        ParsedExpression bodyExpression,
        uint id)
        : base(PhysicalOperatorType.CREATE_MACRO, id)
    {
        _database = database;
        _name = name;
        _parameters = parameters;
        _bodyExpression = bodyExpression;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_executed)
            return false;

        _executed = true;
        _database.Catalog.CreateScalarMacroEntry(
            context.Transaction,
            new ScalarMacroCatalogEntry(_name, _parameters, _bodyExpression));
        return false;
    }
}
