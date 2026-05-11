using System.Collections.Generic;

namespace BogDb.Core.Parser;

/// <summary>
/// A single (non-union) Cypher query containing reading/updating clauses and an optional RETURN.
/// Mirrors C++ SingleQuery from parser/query/single_query.h
/// </summary>
public class SingleQuery
{
    private readonly List<ReadingClause> _readingClauses = new();
    private readonly List<UpdatingClause> _updatingClauses = new();
    private ReturnClause? _returnClause;

    public void AddReadingClause(ReadingClause clause) => _readingClauses.Add(clause);
    public void AddUpdatingClause(UpdatingClause clause) => _updatingClauses.Add(clause);
    public void SetReturnClause(ReturnClause clause) => _returnClause = clause;

    public int GetNumReadingClauses() => _readingClauses.Count;
    public ReadingClause GetReadingClause(int idx) => _readingClauses[idx];
    public int GetNumUpdatingClauses() => _updatingClauses.Count;
    public UpdatingClause GetUpdatingClause(int idx) => _updatingClauses[idx];
    public bool HasReturnClause() => _returnClause != null;
    public ReturnClause GetReturnClause() => _returnClause!;
}

/// <summary>
/// Base for updating clauses (Create, Set, Delete, Merge — implemented in later phases).
/// </summary>
public abstract class UpdatingClause
{
    public ClauseType ClauseType { get; }
    protected UpdatingClause(ClauseType type) { ClauseType = type; }
}
