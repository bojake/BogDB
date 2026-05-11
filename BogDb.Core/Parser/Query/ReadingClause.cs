namespace BogDb.Core.Parser;

public enum ClauseType
{
    MATCH,
    OPTIONAL_MATCH,
    UNWIND,
    WITH,
    IN_QUERY_CALL,
    LOAD_FROM,
    CREATE,
    MERGE,
    DELETE,
    SET,
    CALL_SUBQUERY
}

/// <summary>
/// Abstract base for reading clauses: MATCH, OPTIONAL MATCH, UNWIND, etc.
/// Mirrors C++ ReadingClause from parser/query/reading_clause/reading_clause.h
/// </summary>
public abstract class ReadingClause
{
    public ClauseType ClauseType { get; }
    protected ReadingClause(ClauseType type) { ClauseType = type; }
}

public class UnwindClause : ReadingClause
{
    public ParsedExpression Expression { get; }
    public string Alias { get; }

    public UnwindClause(ParsedExpression expression, string alias) : base(ClauseType.UNWIND)
    {
        Expression = expression;
        Alias = alias;
    }
}
