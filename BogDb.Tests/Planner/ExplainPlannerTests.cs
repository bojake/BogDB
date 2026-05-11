using BogDb.Core.Binder;
using BogDb.Core.Common;
using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;
using Xunit;

namespace BogDb.Tests.Planner;

public sealed class ExplainPlannerTests
{
    [Fact]
    public void Planner_UsesInnerStatementPlan_ForExplain()
    {
        var singleQuery = new NormalizedSingleQuery();
        singleQuery.SetReturnClause(new BoundReturnClause(new List<BoundProjectionItem>
        {
            new(new LiteralExpression(1L, LogicalTypeID.INT64), "v")
        }));

        var query = new BoundRegularQuery();
        query.AddSingleQuery(singleQuery);

        var plan = new BogDb.Core.Planner.Planner().GetBestPlan(new BoundExplain(query, ExplainType.LOGICAL_PLAN));

        Assert.Equal(LogicalOperatorType.LOGICAL_PROJECTION, plan.LastOperator.OperatorType);
        Assert.Equal(LogicalOperatorType.LOGICAL_EXPRESSIONS_SCAN, plan.LastOperator.GetChild(0).OperatorType);
    }

    [Fact]
    public void Planner_UsesLogicalStandaloneCall_ForBoundStandaloneCall()
    {
        var bound = new BoundStandaloneCall(
            "opt_name",
            new LiteralExpression(true, LogicalTypeID.BOOL),
            LogicalTypeID.BOOL);

        var plan = new BogDb.Core.Planner.Planner().GetBestPlan(bound);

        Assert.Equal(LogicalOperatorType.LOGICAL_STANDALONE_CALL, plan.LastOperator.OperatorType);
    }

    [Fact]
    public void Planner_UsesLogicalCreateMacro_ForBoundCreateMacro()
    {
        var bound = new BoundCreateMacro(
            "Add10",
            Array.Empty<BogDb.Core.Parser.MacroParameter>(),
            new BogDb.Core.Parser.ParsedLiteralExpression(10L, "10"));

        var plan = new BogDb.Core.Planner.Planner().GetBestPlan(bound);

        Assert.Equal(LogicalOperatorType.LOGICAL_CREATE_MACRO, plan.LastOperator.OperatorType);
    }
}
