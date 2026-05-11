using System.Collections.Generic;
using BogDb.Core.Binder;

namespace BogDb.Core.Planner.Operator;

/// <summary>
/// Logical operator for CALL { subquery } execution.
/// Represents an inline subquery that produces rows to be cross-producted or
/// correlated with the outer pipeline.
/// </summary>
public sealed class LogicalCallSubquery : LogicalOperator
{
    /// <summary>The bound inner query to execute.</summary>
    public BoundRegularQuery InnerQuery { get; }

    /// <summary>Outer-scope variable names imported via WITH (empty if non-correlated).</summary>
    public List<string> CorrelatedVariables { get; }

    /// <summary>Metadata about each correlated variable (label, PK).</summary>
    public List<CorrelatedVarInfo> CorrelatedVarInfos { get; }

    /// <summary>Column names produced by the inner query's RETURN clause.</summary>
    public List<string> OutputColumnNames { get; }

    /// <summary>Raw text of the inner query body for correlated re-execution.</summary>
    public string InnerQueryText { get; }

    /// <summary>The planned inner logical plan (set by the Planner).</summary>
    public LogicalPlan? InnerPlan { get; set; }

    public LogicalCallSubquery(
        BoundRegularQuery innerQuery,
        List<string> correlatedVariables,
        List<CorrelatedVarInfo> correlatedVarInfos,
        List<string> outputColumnNames,
        string innerQueryText,
        LogicalOperator? outerChild = null)
        : base(LogicalOperatorType.LOGICAL_CALL_SUBQUERY)
    {
        InnerQuery = innerQuery;
        CorrelatedVariables = correlatedVariables;
        CorrelatedVarInfos = correlatedVarInfos;
        OutputColumnNames = outputColumnNames;
        InnerQueryText = innerQueryText;
        if (outerChild != null)
            Children.Add(outerChild);
    }

    public bool IsCorrelated => CorrelatedVariables.Count > 0;

    public override string GetExpressionsForPrinting()
        => IsCorrelated
            ? $"CallSubquery(correlated: [{string.Join(", ", CorrelatedVariables)}] -> [{string.Join(", ", OutputColumnNames)}])"
            : $"CallSubquery(independent -> [{string.Join(", ", OutputColumnNames)}])";
}
