using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Parser;

public sealed class MacroParameter
{
    public string Name { get; }
    public ParsedExpression? DefaultExpression { get; }

    public MacroParameter(string name, ParsedExpression? defaultExpression = null)
    {
        Name = name;
        DefaultExpression = defaultExpression;
    }
}

public sealed class CreateMacro : Statement
{
    public string Name { get; }
    public IReadOnlyList<MacroParameter> Parameters { get; }
    public ParsedExpression BodyExpression { get; }

    public CreateMacro(string name, IReadOnlyList<MacroParameter> parameters, ParsedExpression bodyExpression)
        : base(StatementType.CREATE_MACRO)
    {
        Name = name;
        Parameters = parameters;
        BodyExpression = bodyExpression;
    }
}
