using System.Collections.Generic;
using BogDb.Core.Parser;

namespace BogDb.Core.Binder
{
    public class BoundCreateClause : BoundUpdatingClause
    {
        public List<QueryNode> CreateNodes { get; }
        public List<QueryRel> CreateRels { get; }

        public BoundCreateClause(List<QueryNode> createNodes, List<QueryRel> createRels) : base(ClauseType.CREATE)
        {
            CreateNodes = createNodes;
            CreateRels = createRels;
        }
    }

    public class BoundMergeAction
    {
        public bool IsOnMatch { get; }
        public BoundSetClause SetClause { get; }

        public BoundMergeAction(bool isOnMatch, BoundSetClause setClause)
        {
            IsOnMatch = isOnMatch;
            SetClause = setClause;
        }
    }

    public class BoundMergeClause : BoundUpdatingClause
    {
        public QueryGraph QueryGraph { get; }
        public List<BoundMergeAction> Actions { get; }

        public BoundMergeClause(QueryGraph queryGraph, List<BoundMergeAction> actions) : base(ClauseType.MERGE)
        {
            QueryGraph = queryGraph;
            Actions = actions;
        }
    }

    public class BoundDeleteClause : BoundUpdatingClause
    {
        public List<Expression> DeleteExpressions { get; }
        public BoundDeleteClause(List<Expression> deleteExpressions) : base(ClauseType.DELETE)
        {
            DeleteExpressions = deleteExpressions;
        }
    }

    public class BoundSetClause : BoundUpdatingClause
    {
        public List<Expression> SetItems { get; }
        public BoundSetClause(List<Expression> setItems) : base(ClauseType.SET)
        {
            SetItems = setItems;
        }
    }
}
