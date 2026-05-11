using System.Collections.Generic;

namespace BogDb.Core.Parser
{
    public class CreateClause : UpdatingClause
    {
        public List<PatternElement> PatternElements { get; }
        public CreateClause(List<PatternElement> patternElements) : base(ClauseType.CREATE)
        {
            PatternElements = patternElements;
        }
    }

    public class DeleteClause : UpdatingClause
    {
        public List<ParsedExpression> Expressions { get; }
        public DeleteClause(List<ParsedExpression> expressions) : base(ClauseType.DELETE)
        {
            Expressions = expressions;
        }
    }

    public class SetClause : UpdatingClause
    {
        public List<ParsedExpression> SetItems { get; }
        public SetClause(List<ParsedExpression> setItems) : base(ClauseType.SET)
        {
            SetItems = setItems;
        }
    }

    public class MergeAction
    {
        public bool IsOnMatch { get; }
        public SetClause SetClause { get; }

        public MergeAction(bool isOnMatch, SetClause setClause)
        {
            IsOnMatch = isOnMatch;
            SetClause = setClause;
        }
    }

    public class MergeClause : UpdatingClause
    {
        public List<PatternElement> PatternElements { get; }
        public List<MergeAction> Actions { get; }

        public MergeClause(List<PatternElement> patternElements, List<MergeAction> actions) : base(ClauseType.MERGE)
        {
            PatternElements = patternElements;
            Actions = actions;
        }
    }
}
