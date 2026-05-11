using System.Collections.Generic;

namespace BogDb.Core.Binder;

/// <summary>
/// A single query after normalization: reading clauses + bound result.
/// Mirrors C++ NormalizedSingleQuery.
/// </summary>
public class NormalizedSingleQuery
{
    private readonly List<BoundReadingClause> _readingClauses = new();
    private readonly List<BoundUpdatingClause> _updatingClauses = new();
    public BoundReturnClause? ReturnClause { get; private set; }

    public void AddReadingClause(BoundReadingClause clause) => _readingClauses.Add(clause);
    public void AddUpdatingClause(BoundUpdatingClause clause) => _updatingClauses.Add(clause);
    public void SetReturnClause(BoundReturnClause clause) => ReturnClause = clause;

    public int GetNumReadingClauses() => _readingClauses.Count;
    public BoundReadingClause GetReadingClause(int idx) => _readingClauses[idx];
    
    public int GetNumUpdatingClauses() => _updatingClauses.Count;
    public BoundUpdatingClause GetUpdatingClause(int idx) => _updatingClauses[idx];
    
    public bool HasReturnClause() => ReturnClause != null;
}

public abstract class BoundUpdatingClause
{
    public Parser.ClauseType ClauseType { get; }
    protected BoundUpdatingClause(Parser.ClauseType type) { ClauseType = type; }
}

/// <summary>Base class for bound reading clauses.</summary>
public abstract class BoundReadingClause
{
    public Parser.ClauseType ClauseType { get; }
    protected BoundReadingClause(Parser.ClauseType type) { ClauseType = type; }
}

public class BoundWithClause : BoundReadingClause
{
    public BoundProjectionBody ProjectionBody { get; }
    public Expression? WherePredicate { get; }

    public BoundWithClause(BoundProjectionBody body, Expression? wherePredicate) : base(Parser.ClauseType.WITH)
    {
        ProjectionBody = body;
        WherePredicate = wherePredicate;
    }
}

public class BoundUnwindClause : BoundReadingClause
{
    public Expression Expression { get; }
    public string Alias { get; }

    public BoundUnwindClause(Expression expression, string alias) : base(Parser.ClauseType.UNWIND)
    {
        Expression = expression;
        Alias = alias;
    }
}
