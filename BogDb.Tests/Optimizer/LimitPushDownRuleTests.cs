using System.Collections.Generic;
using BogDb.Core.Binder;
using BogDb.Core.Common;
using BogDb.Core.Optimizer;
using BogDb.Core.Parser;
using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;
using Xunit;

namespace BogDb.Tests.Optimizer;

public sealed class LimitPushDownRuleTests
{
    [Fact]
    public void LimitPushDown_WithSkip_PropagatesSkipPlusLimit_ToRecursiveExtend()
    {
        var src = new QueryNode(
            "a",
            "_0_a",
            new List<string> { "Person" },
            new List<PropertyExpression>(),
            new List<(string Key, Expression Value)>());
        var dst = new QueryNode(
            "b",
            "_0_b",
            new List<string> { "Person" },
            new List<PropertyExpression>(),
            new List<(string Key, Expression Value)>());
        var rel = new QueryRel(
            "r",
            new List<string> { "KNOWS" },
            new List<QueryRelConnection>(),
            ArrowDirection.RIGHT,
            src,
            dst,
            new List<PropertyExpression>(),
            new List<(string Key, Expression Value)>(),
            lowerBound: "1",
            upperBound: "3");

        var scan = new LogicalScanNodeProperty(new NodeExpression("a"), "Person", new List<PropertyExpression>());
        var recursive = new LogicalRecursiveExtend(rel, 1, 3, scan);
        var skip = new LogicalSkip(new LiteralExpression(5L, LogicalTypeID.INT64), recursive);
        var limit = new LogicalLimit(new LiteralExpression(2L, LogicalTypeID.INT64), skip);
        var plan = new LogicalPlan { LastOperator = limit };

        var changed = new LimitPushDownRule().Rewrite(plan);

        Assert.True(changed);
        Assert.Equal(7L, recursive.EarlyStopLimit);
    }
}
