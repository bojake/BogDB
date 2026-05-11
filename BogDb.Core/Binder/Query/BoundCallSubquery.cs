using System.Collections.Generic;

namespace BogDb.Core.Binder;

/// <summary>
/// Metadata about a correlated variable for CALL subquery execution.
/// Contains the node's label and primary key property name so the
/// physical operator can filter by the outer row's specific node.
/// </summary>
public sealed class CorrelatedVarInfo
{
    /// <summary>Variable name (e.g. "a").</summary>
    public string VariableName { get; }

    /// <summary>Node table label (e.g. "Person").</summary>
    public string NodeLabel { get; }

    /// <summary>Primary key property name (e.g. "id").</summary>
    public string PrimaryKeyPropertyName { get; }

    public CorrelatedVarInfo(string variableName, string nodeLabel, string primaryKeyPropertyName)
    {
        VariableName = variableName;
        NodeLabel = nodeLabel;
        PrimaryKeyPropertyName = primaryKeyPropertyName;
    }
}

/// <summary>
/// Bound representation of a CALL { subquery } reading clause.
/// Contains the bound inner query and tracks which outer-scope variables
/// are imported (correlated) via WITH at the start of the subquery body.
/// </summary>
public class BoundCallSubquery : BoundReadingClause
{
    /// <summary>The bound inner query (MATCH ... RETURN ...).</summary>
    public BoundRegularQuery BoundInnerQuery { get; }

    /// <summary>
    /// Names of outer-scope variables imported into the subquery via a leading WITH clause.
    /// Empty for non-correlated subqueries.
    /// </summary>
    public List<string> CorrelatedVariables { get; }

    /// <summary>
    /// Metadata about each correlated variable (label, primary key).
    /// Used by the physical operator to rewrite the query per outer row.
    /// </summary>
    public List<CorrelatedVarInfo> CorrelatedVarInfos { get; }

    /// <summary>
    /// Column names produced by the inner query's RETURN clause.
    /// These are projected into the outer scope after execution.
    /// </summary>
    public List<string> OutputColumnNames { get; }

    /// <summary>
    /// Raw text of the inner query body, used for correlated execution
    /// which rewrites and re-executes per outer row.
    /// </summary>
    public string InnerQueryText { get; }

    public BoundCallSubquery(
        BoundRegularQuery innerQuery,
        List<string> correlatedVariables,
        List<CorrelatedVarInfo> correlatedVarInfos,
        List<string> outputColumnNames,
        string innerQueryText)
        : base(Parser.ClauseType.CALL_SUBQUERY)
    {
        BoundInnerQuery = innerQuery;
        CorrelatedVariables = correlatedVariables;
        CorrelatedVarInfos = correlatedVarInfos;
        OutputColumnNames = outputColumnNames;
        InnerQueryText = innerQueryText;
    }
}
