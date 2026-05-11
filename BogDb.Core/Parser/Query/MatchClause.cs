using System.Collections.Generic;

namespace BogDb.Core.Parser;

/// <summary>
/// Parsed MATCH (or OPTIONAL MATCH) clause.
/// Holds a list of patterns (PatternElement) and an optional WHERE predicate.
/// Mirrors C++ MatchClause from parser/query/reading_clause/match_clause.h
/// </summary>
public class MatchClause : ReadingClause
{
    private readonly List<PatternElement> _patternElements;
    private ParsedExpression? _wherePredicate;

    public MatchClause(List<PatternElement> patternElements, ClauseType clauseType = ClauseType.MATCH)
        : base(clauseType)
    {
        _patternElements = patternElements;
    }

    public void SetWherePredicate(ParsedExpression where) => _wherePredicate = where;

    public int GetNumPatternElements() => _patternElements.Count;
    public PatternElement GetPatternElement(int idx) => _patternElements[idx];
    public bool HasWherePredicate() => _wherePredicate != null;
    public ParsedExpression GetWherePredicate() => _wherePredicate!;
}
