using System.Collections.Generic;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;
using BogDb.Core.Processor;
using BogDb.Core.Processor.Operator;
using BogDb.Core.Binder;
using BogDb.Core.Common;
using BogDb.Core.Processor.Operator.Scan;
using BogDb.Core.Parser;

namespace BogDb.Tests.Processor;

public class PlanMapperTests
{
    private sealed class DummyLogicalOperator : LogicalOperator
    {
        public DummyLogicalOperator(LogicalOperatorType type) : base(type) {}
        public DummyLogicalOperator(LogicalOperatorType type, LogicalOperator child) : base(type, child) {}
        public override string GetExpressionsForPrinting() => string.Empty;
    }

    [Fact]
    public void MapLogicalPlanToPhysical_MapsScanFrontier_ToPhysicalScanFrontier()
    {
        using var database = BogDatabase.Open(":memory:");
        var mapper = new PlanMapper(database);
        var logicalPlan = new LogicalPlan
        {
            LastOperator = new LogicalScanFrontier("n", "Person")
        };

        var physicalPlan = mapper.MapLogicalPlanToPhysical(logicalPlan);

        Assert.NotNull(physicalPlan);
        Assert.NotNull(physicalPlan.LastOperator);
        Assert.IsType<BogDb.Core.Processor.Operator.Scan.PhysicalScanFrontier>(physicalPlan.LastOperator);
        Assert.Equal(PhysicalOperatorType.SCAN_FRONTIER, physicalPlan.LastOperator.OperatorType);
    }

    [Fact]
    public void MapLogicalPlanToPhysical_ScanFrontierWithExternalFrontier_PreservesFrontierReference()
    {
        using var database = BogDatabase.Open(":memory:");
        var mapper = new PlanMapper(database);

        var frontier = new BogDb.Core.GraphDataScience.GdsFrontier();
        frontier.AddNode(new BogDb.Core.GraphDataScience.NodeId(1, 42));

        var logicalPlan = new LogicalPlan
        {
            LastOperator = new LogicalScanFrontier("n", "Person", frontier)
        };

        var physicalPlan = mapper.MapLogicalPlanToPhysical(logicalPlan);

        Assert.NotNull(physicalPlan.LastOperator);
        var physical = Assert.IsType<BogDb.Core.Processor.Operator.Scan.PhysicalScanFrontier>(physicalPlan.LastOperator);
        Assert.Same(frontier, physical.Frontier);
        Assert.Equal(1, physical.Frontier.ActiveCount);
    }

    [Fact]
    public void MapLogicalPlanToPhysical_MapsExpressionsScan()
    {
        using var database = BogDatabase.Open(":memory:");
        var mapper = new PlanMapper(database);
        var logicalPlan = new LogicalPlan
        {
            LastOperator = new LogicalExpressionsScan(new List<Expression>
            {
                new LiteralExpression(7L, LogicalTypeID.INT64)
            })
        };

        var physicalPlan = mapper.MapLogicalPlanToPhysical(logicalPlan);

        Assert.NotNull(physicalPlan.LastOperator);
        Assert.IsType<PhysicalExpressionsScan>(physicalPlan.LastOperator);
        Assert.Equal(PhysicalOperatorType.EXPRESSIONS_SCAN, physicalPlan.LastOperator.OperatorType);
    }

    [Fact]
    public void MapLogicalPlanToPhysical_PassesEarlyStopLimit_ToRecursiveExtend()
    {
        using var database = BogDatabase.Open(":memory:");
        var mapper = new PlanMapper(database);

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
        var recursive = new LogicalRecursiveExtend(rel, 1, 3, scan)
        {
            EarlyStopLimit = 7
        };
        var logicalPlan = new LogicalPlan { LastOperator = recursive };

        var physicalPlan = mapper.MapLogicalPlanToPhysical(logicalPlan);

        var physical = Assert.IsType<BogDb.Core.Processor.Operator.RecursiveExtend>(physicalPlan.LastOperator);
        Assert.Equal(7L, physical.EarlyStopLimit);
    }
}
