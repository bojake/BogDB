using System.Collections.Generic;

namespace BogDb.Core.Parser;

/// <summary>
/// Parsed AST node for a regular (non-EXPLAIN) Cypher query.
/// May contain multiple SingleQuery parts joined via UNION / UNION ALL.
/// Mirrors C++ RegularQuery from parser/query/regular_query.h
/// </summary>
public class RegularQuery : Statement
{
    private readonly List<SingleQuery> _singleQueries = new();
    private readonly List<bool> _isUnionAllList = new();  // true = UNION ALL, false = UNION
    private bool _isProfile;

    public RegularQuery(SingleQuery firstQuery) : base(Common.StatementType.QUERY)
    {
        _singleQueries.Add(firstQuery);
        _isProfile = false;
    }

    public void AddSingleQuery(SingleQuery query, bool isUnionAll)
    {
        _singleQueries.Add(query);
        _isUnionAllList.Add(isUnionAll);
    }

    public int GetNumSingleQueries() => _singleQueries.Count;
    public SingleQuery GetSingleQuery(int idx) => _singleQueries[idx];

    /// <summary>
    /// Returns whether the i-th UNION connector is UNION ALL (i = index into connectors, not queries).
    /// </summary>
    public bool GetIsUnionAll(int connectorIdx) => _isUnionAllList[connectorIdx];

    public void SetIsProfile(bool isProfile) => _isProfile = isProfile;

    public bool GetIsProfile() => _isProfile;
}
