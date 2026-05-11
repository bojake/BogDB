using BogDb.Core.Common;
using BogDb.Core.Parser;

namespace BogDb.Core.Binder;

/// <summary>
/// A logically bound subquery expression (EXISTS { MATCH... } or COUNT { MATCH... }).
/// Mirrors C++ BoundExistCountSubqueryExpression.
/// </summary>
public sealed class BoundSubqueryExpression : Expression
{
    public SubqueryType SubqueryType { get; }
    public BoundRegularQuery BoundQuery { get; }

    public BoundSubqueryExpression(SubqueryType type, BoundRegularQuery boundQuery, string rawName)
        : base(ExpressionType.SUBQUERY, type == SubqueryType.EXISTS ? LogicalTypeID.BOOL : LogicalTypeID.INT64)
    {
        SubqueryType = type;
        BoundQuery = boundQuery;
    }
}
