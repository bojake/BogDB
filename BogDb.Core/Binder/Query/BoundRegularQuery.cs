using System.Collections.Generic;

namespace BogDb.Core.Binder;

/// <summary>
/// Bound AST for a regular query after semantic analysis.
/// Holds NormalizedSingleQuery parts and union metadata.
/// Mirrors C++ BoundRegularQuery.
/// </summary>
public class BoundRegularQuery : BoundStatement
{
    private readonly List<NormalizedSingleQuery> _singleQueries = new();
    private readonly List<bool> _isUnionAllList = new();
    private bool _isProfile;

    public BoundRegularQuery() : base(Common.StatementType.QUERY)
    {
        _isProfile = false;
    }

    public void AddSingleQuery(NormalizedSingleQuery q, bool isUnionAll = false)
    {
        _singleQueries.Add(q);
        if (_singleQueries.Count > 1)
            _isUnionAllList.Add(isUnionAll);
    }

    public int GetNumSingleQueries() => _singleQueries.Count;
    public NormalizedSingleQuery GetSingleQuery(int idx) => _singleQueries[idx];
    public bool GetIsUnionAll(int connectorIdx) => _isUnionAllList[connectorIdx];
    public void SetIsProfile(bool isProfile) => _isProfile = isProfile;
    public bool GetIsProfile() => _isProfile;
}
