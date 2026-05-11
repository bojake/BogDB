using BogDb.Core.Common;
using BogDb.Core.Parser.Query;

namespace BogDb.Core.Parser;

public enum SubqueryType
{
    EXISTS,
    COUNT
}

/// <summary>
/// A parsed subquery expression, typically `EXISTS { MATCH ... }` or `COUNT { MATCH ... }`.
/// Wraps a fully independent GraphPattern or RegularQuery.
/// Mirrors C++ ParsedSubqueryExpression.
/// </summary>
public sealed class ParsedSubqueryExpression : ParsedExpression
{
    public SubqueryType SubqueryType { get; }
    public RegularQuery Subquery { get; }

    public ParsedSubqueryExpression(SubqueryType type, RegularQuery subquery, string rawName)
        : base(ExpressionType.SUBQUERY, rawName)
    {
        SubqueryType = type;
        Subquery = subquery;
    }

    public override ParsedExpression Copy()
    {
        // Copying subqueries explicitly involves duplicating the ast structures
        // We will assume shallow references for AST propagation parsing unless deeper rewrites occur natively.
        return new ParsedSubqueryExpression(SubqueryType, Subquery, _rawName);
    }
}
