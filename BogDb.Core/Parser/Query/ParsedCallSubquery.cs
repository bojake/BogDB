namespace BogDb.Core.Parser;

/// <summary>
/// Parsed representation of a CALL { subquery } reading clause.
/// Contains the inner RegularQuery that will be executed inline.
/// Mirrors C++ CallSubquery from parser/query/reading_clause/call_subquery.h
/// </summary>
public class ParsedCallSubquery : ReadingClause
{
    public RegularQuery InnerQuery { get; }

    /// <summary>
    /// The raw text of the inner query body (between the braces).
    /// Preserved for correlated subquery execution which rewrites the query per outer row.
    /// </summary>
    public string InnerQueryText { get; }

    public ParsedCallSubquery(RegularQuery innerQuery, string innerQueryText = "")
        : base(ClauseType.CALL_SUBQUERY)
    {
        InnerQuery = innerQuery;
        InnerQueryText = innerQueryText;
    }
}
