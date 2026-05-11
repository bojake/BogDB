using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using BogDb.Core.Binder;
using BogDb.Core.Main;

namespace BogDb.Core.Processor.Operator;

/// <summary>
/// Physical operator for CALL { subquery } execution.
///
/// For non-correlated subqueries:
///   Materializes the inner plan results once (on first call), then for each
///   outer-side row, emits cross-product rows (outer × inner).
///
/// For correlated subqueries:
///   For each outer-side row, rewrites the inner query text to filter by the
///   outer row's node identity, then parses/binds/plans/executes the rewritten
///   query as a fresh pipeline.
/// </summary>
public sealed class PhysicalCallSubquery : PhysicalOperator
{
    private readonly PhysicalOperator _outerChild;
    private readonly PhysicalOperator _innerRoot;
    private readonly IReadOnlyList<string> _correlatedVariables;
    private readonly IReadOnlyList<CorrelatedVarInfo>? _correlatedVarInfos;
    private readonly IReadOnlyList<string> _outputColumnNames;
    private readonly string? _innerQueryText;
    private readonly bool _isCorrelated;
    private readonly BogDatabase? _database;

    // Non-correlated: materialized inner results
    private List<ExecutionState>? _materializedInnerResults;
    // Correlated: materialized inner results for current outer row
    private List<ExecutionState>? _correlatedInnerResults;
    private int _currentInnerIdx;
    private ExecutionState? _currentOuterState;
    private bool _outerExhausted;

    public PhysicalCallSubquery(
        PhysicalOperator outerChild,
        PhysicalOperator innerRoot,
        IReadOnlyList<string> correlatedVariables,
        IReadOnlyList<CorrelatedVarInfo>? correlatedVarInfos,
        IReadOnlyList<string> outputColumnNames,
        string? innerQueryText,
        BogDatabase? database,
        uint id)
        : base(PhysicalOperatorType.CALL_SUBQUERY, id)
    {
        _outerChild = outerChild;
        _innerRoot = innerRoot;
        _correlatedVariables = correlatedVariables;
        _correlatedVarInfos = correlatedVarInfos;
        _outputColumnNames = outputColumnNames;
        _innerQueryText = innerQueryText;
        _isCorrelated = correlatedVariables.Count > 0;
        _database = database;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_isCorrelated)
            return GetNextCorrelated(context);
        return GetNextNonCorrelated(context);
    }

    /// <summary>
    /// Non-correlated: materialize inner results once, then cross-product
    /// with outer rows.
    /// </summary>
    private bool GetNextNonCorrelated(ExecutionContext context)
    {
        if (_materializedInnerResults == null)
        {
            _materializedInnerResults = MaterializeInnerPlan(context);
            _currentInnerIdx = 0;
            _outerExhausted = false;
            _currentOuterState = null;
        }

        if (_materializedInnerResults.Count == 0)
            return false;

        while (true)
        {
            if (_currentOuterState != null && _currentInnerIdx < _materializedInnerResults.Count)
            {
                var innerState = _materializedInnerResults[_currentInnerIdx++];
                var merged = ExecutionState.Merge(_currentOuterState, innerState);
                context.RestoreState(merged);
                return true;
            }

            if (_outerExhausted)
                return false;

            if (!_outerChild.GetNextTuple(context))
            {
                _outerExhausted = true;
                return false;
            }

            _currentOuterState = context.CaptureState();
            _currentInnerIdx = 0;
        }
    }

    /// <summary>
    /// Correlated: for each outer row, rewrite and re-execute the inner query
    /// with the outer row's variable identity as a filter.
    /// </summary>
    private bool GetNextCorrelated(ExecutionContext context)
    {
        while (true)
        {
            if (_correlatedInnerResults != null && _currentInnerIdx < _correlatedInnerResults.Count)
            {
                var innerState = _correlatedInnerResults[_currentInnerIdx++];
                var merged = ExecutionState.Merge(_currentOuterState!, innerState);
                context.RestoreState(merged);
                return true;
            }

            if (!_outerChild.GetNextTuple(context))
                return false;

            _currentOuterState = context.CaptureState();
            _correlatedInnerResults = ExecuteCorrelatedInner(context, _currentOuterState);
            _currentInnerIdx = 0;
        }
    }

    /// <summary>
    /// Rewrites the inner query text to filter by the outer row's correlated
    /// variable(s), then executes the rewritten query through a fresh pipeline.
    ///
    /// For each correlated variable:
    ///   - Strips the leading WITH clause
    ///   - Adds a MATCH filter: MATCH (var:Label {pk: 'value'})
    /// </summary>
    private List<ExecutionState> ExecuteCorrelatedInner(
        ExecutionContext context, ExecutionState outerState)
    {
        if (_database == null || string.IsNullOrEmpty(_innerQueryText) 
            || _correlatedVarInfos == null)
            return new List<ExecutionState>();

        // Rewrite the inner query text with explicit property filters
        var rewrittenQuery = RewriteCorrelatedQuery(outerState);

        // Execute through the full pipeline: parse → bind → plan → execute
        return ExecuteQueryPipeline(rewrittenQuery, context);
    }

    /// <summary>
    /// Rewrites the inner query text for correlated execution.
    /// Strips the leading WITH clause and adds explicit property-based
    /// node matches for each correlated variable.
    /// </summary>
    private string RewriteCorrelatedQuery(ExecutionState outerState)
    {
        var queryText = _innerQueryText!;

        // Strip the leading WITH clause: "WITH a, b MATCH ..." → "MATCH ..."
        var withMatch = Regex.Match(queryText,
            @"^\s*WITH\s+[^M]*?(MATCH|RETURN|WHERE)",
            RegexOptions.IgnoreCase);
        if (withMatch.Success)
        {
            // Find where the actual MATCH/RETURN/WHERE starts after WITH
            var nextKeywordIdx = queryText.IndexOf(withMatch.Groups[1].Value,
                withMatch.Groups[1].Index, StringComparison.OrdinalIgnoreCase);
            queryText = queryText.Substring(nextKeywordIdx);
        }

        // For each correlated variable, prepend a filtered MATCH
        var prefix = new StringBuilder();
        for (int i = 0; i < _correlatedVarInfos!.Count; i++)
        {
            var info = _correlatedVarInfos[i];
            if (string.IsNullOrEmpty(info.NodeLabel))
                continue;

            // Get the PK value from the outer state
            var pkValue = GetOuterPropertyValue(outerState, info.VariableName, info.PrimaryKeyPropertyName);
            if (pkValue == null)
                continue;

            // Build filtered MATCH: MATCH (a:Person {id: 'alice'})
            var escapedValue = FormatPropertyValue(pkValue);
            prefix.Append($"MATCH ({info.VariableName}:{info.NodeLabel} " +
                          $"{{{info.PrimaryKeyPropertyName}: {escapedValue}}}) ");
        }

        return prefix.ToString() + queryText;
    }

    /// <summary>
    /// Gets a property value for a correlated variable from the outer execution state.
    /// </summary>
    private static object? GetOuterPropertyValue(
        ExecutionState outerState, string varName, string propertyName)
    {
        // Check variable properties first
        if (outerState.CurrentVariableProperties != null &&
            outerState.CurrentVariableProperties.TryGetValue(varName, out var props) &&
            props.TryGetValue(propertyName, out var val))
        {
            return val;
        }

        // Fall back to scalar bindings
        if (outerState.CurrentScalarBindings != null &&
            outerState.CurrentScalarBindings.TryGetValue($"{varName}.{propertyName}", out var scalarVal))
        {
            return scalarVal;
        }

        return null;
    }

    /// <summary>
    /// Formats a property value for inline inclusion in a Cypher query string.
    /// </summary>
    private static string FormatPropertyValue(object value)
    {
        return value switch
        {
            string s => $"'{s.Replace("'", "\\'")}'",
            int i => i.ToString(),
            long l => l.ToString(),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => $"'{value}'"
        };
    }

    /// <summary>
    /// Executes a Cypher query string through a fresh parse → bind → plan → execute pipeline.
    /// Returns the result rows as ExecutionState snapshots.
    /// </summary>
    private List<ExecutionState> ExecuteQueryPipeline(string queryText, ExecutionContext outerContext)
    {
        var results = new List<ExecutionState>();

        try
        {
            // Parse
            var inputStream = new Antlr4.Runtime.AntlrInputStream(queryText);
            var lexer = new CypherLexer(inputStream);
            lexer.RemoveErrorListeners();
            var tokenStream = new Antlr4.Runtime.CommonTokenStream(lexer);
            var parser = new CypherParser(tokenStream);
            parser.RemoveErrorListeners();
            var root = parser.ku_Statements();

            var transformer = new BogDb.Core.Parser.Antlr4.Transformer(root);
            var statements = transformer.Transform();
            if (statements.Count != 1 || statements[0] is not BogDb.Core.Parser.RegularQuery regularQuery)
                return results;

            // Bind
            var binder = new BogDb.Core.Binder.Binder(_database!.Catalog);
            var boundQuery = binder.BindQuery(regularQuery);

            // Plan
            var planner = new BogDb.Core.Planner.Planner(_database);
            var plan = planner.GetBestPlan(boundQuery);

            // Optimize
            var optimizer = new BogDb.Core.Optimizer.Optimizer(_database);
            optimizer.Optimize(plan);

            // Map to physical
            var planMapper = new PlanMapper(_database);
            var physicalPlan = planMapper.MapLogicalPlanToPhysical(plan);

            // Execute with a fresh context sharing the same transaction
            var innerContext = new ExecutionContext(
                outerContext.Transaction, outerContext.BufferManager, outerContext.Database)
            {
                QueryMetrics = outerContext.QueryMetrics
            };
            innerContext.ParameterBindings = outerContext.ParameterBindings;

            while (physicalPlan.LastOperator.GetNextTuple(innerContext))
            {
                results.Add(innerContext.CaptureState());
            }
        }
        catch
        {
            // If the rewritten query fails, return empty results
            // (graceful degradation for edge cases)
        }

        return results;
    }

    private List<ExecutionState> MaterializeInnerPlan(ExecutionContext context)
    {
        var results = new List<ExecutionState>();
        var savedState = context.CaptureState();

        while (_innerRoot.GetNextTuple(context))
        {
            results.Add(context.CaptureState());
        }

        context.RestoreState(savedState);
        return results;
    }
}
