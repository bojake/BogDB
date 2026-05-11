using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BogDb.Core.Main;
using BogDb.Core.Optimizer;
using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;
using Xunit;

namespace BogDb.Tests.Optimizer;

public class OptimizerCompletenessTests
{
    [Fact]
    public void Optimizer_RegistersAllCppParityRules()
    {
        using var db = BogDatabase.Open(":memory:");
        var optimizer = new BogDb.Core.Optimizer.Optimizer(db);

        var rulesField = typeof(BogDb.Core.Optimizer.Optimizer)
            .GetField("_rules", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(rulesField);

        var rules = Assert.IsAssignableFrom<IEnumerable<LogicalRule>>(rulesField!.GetValue(optimizer));
        var types = rules.Select(r => r.GetType()).ToHashSet();

        Assert.Contains(typeof(CorrelatedSubqueryUnnestRule), types);
        Assert.Contains(typeof(FactorizationRewriterRule), types);
        Assert.Contains(typeof(RemoveFactorizationRewriterRule), types);
    }

    [Fact]
    public void CorrelatedSubqueryUnnestRule_NoCandidatePlan_NoRewrite()
    {
        var plan = new LogicalPlan { LastOperator = new LogicalSingleRow() };
        var changed = new CorrelatedSubqueryUnnestRule().Rewrite(plan);
        Assert.False(changed);
    }

    [Fact]
    public void FactorizationRules_FlatRowModel_AreNoOps()
    {
        var plan = new LogicalPlan { LastOperator = new LogicalSingleRow() };

        var factorized = new FactorizationRewriterRule().Rewrite(plan);
        var deFactorized = new RemoveFactorizationRewriterRule().Rewrite(plan);

        Assert.False(factorized);
        Assert.False(deFactorized);
    }
}
