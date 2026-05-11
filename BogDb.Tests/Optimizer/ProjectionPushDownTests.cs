using System;
using System.Collections.Generic;
using Xunit;
using BogDb.Core.Binder;
using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;
using BogDb.Core.Optimizer;

namespace BogDb.Tests.Optimizer;

public class ProjectionPushDownTests
{
    [Fact]
    public void Optimizer_InjectsProjection_BeforeFilter()
    {
        // 1. Arrange a mock AST
        // ScanNode -> Filter
        
        var nodeExpr = new NodeExpression("n");
        var scanNode = new LogicalScanNodeProperty(nodeExpr);
        var filterExpr = new VariableExpression("condition", BogDb.Core.Common.LogicalTypeID.BOOL);
        var filter = new LogicalFilter(filterExpr, scanNode);
        
        var plan = new LogicalPlan();
        plan.LastOperator = filter;

        // Verify AST State Before
        Assert.Equal(LogicalOperatorType.LOGICAL_FILTER, plan.LastOperator.OperatorType);
        Assert.Equal(LogicalOperatorType.LOGICAL_SCAN_NODE, plan.LastOperator.GetChild(0).OperatorType);

        // 2. Act
        var optimizer = new BogDb.Core.Optimizer.Optimizer(null!);
        optimizer.Optimize(plan);

        // 3. Assert
        // FilterPushDownRule now absorbs Filter into ScanNodeProperty's PushedPredicate.
        // The root should now be the scan operator directly.
        Assert.Equal(LogicalOperatorType.LOGICAL_SCAN_NODE, plan.LastOperator.OperatorType);
        var scan = (LogicalScanNodeProperty)plan.LastOperator;
        Assert.NotNull(scan.PushedPredicate);
        Assert.Equal(filterExpr, scan.PushedPredicate);
    }
}
