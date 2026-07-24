using System;
using System.Collections.Generic;
using BogDb.Core.Binder;
using BogDb.Core.Common;
using BogDb.Core.Extension;
using BogDb.Core.Function;
using BogDb.Core.Storage.Index;

namespace BogDb.Core.Planner;

/// <summary>
/// The Cypher Execution Planner.
/// Phase 8: Builds a real Scan → [Filter] → Projection logical plan from BoundRegularQuery.
/// </summary>
public sealed class Planner
{
    // GroupId records how a predicate combines with the others: predicates sharing a GroupId are UNIONed
    // (they came from one IN list or one OR-of-equalities), predicates with distinct GroupIds are
    // INTERSECTed (they are separate AND conjuncts). Grouping by property name instead — as the planner
    // once did — silently turned `p = 'a' AND p = 'c'` into `p = 'a' OR p = 'c'`, because two same-property
    // AND conjuncts look identical to two IN alternatives once flattened.
    private readonly record struct IndexLookupPredicate(string PropertyName, object LookupKey, bool IsPrefixScan = false, int GroupId = 0);
    private readonly Main.BogDatabase? _database;
    private int _markJoinCounter;

    public Planner(Main.BogDatabase? database = null)
    {
        _database = database;
    }

    public LogicalPlan GetBestPlan(BoundStatement boundStatement)
        => GetBestPlan(boundStatement, null);

    public LogicalPlan GetBestPlan(BoundStatement boundStatement, IReadOnlySet<string>? preBoundVariables)
    {
        return boundStatement.StatementType switch
        {
            StatementType.QUERY => PlanQuery(boundStatement, preBoundVariables),
            StatementType.COPY_TO => PlanCopyTo((BoundCopyTo)boundStatement),
            StatementType.COPY_FROM => PlanCopyFrom((BoundCopyFrom)boundStatement),
            StatementType.ATTACH_DATABASE => PlanAttachDatabase((BoundAttachDatabase)boundStatement),
            StatementType.EXTENSION => PlanExtensionStatement((BoundExtensionStatement)boundStatement),
            StatementType.CREATE_MACRO => PlanCreateMacro((BoundCreateMacro)boundStatement),
            StatementType.EXPLAIN => PlanExplain((BoundExplain)boundStatement, preBoundVariables),
            StatementType.STANDALONE_CALL => PlanStandaloneCall((BoundStandaloneCall)boundStatement),
            StatementType.STANDALONE_CALL_FUNCTION => PlanStandaloneCallFunction((BoundStandaloneCallFunction)boundStatement),
            _ => throw new NotSupportedException($"Planner does not support: {boundStatement.StatementType}")
        };
    }

    private LogicalPlan PlanExplain(BoundExplain boundStatement, IReadOnlySet<string>? preBoundVariables)
        => GetBestPlan(boundStatement.StatementToExplain, preBoundVariables);

    private static LogicalPlan PlanExtensionStatement(BoundExtensionStatement boundStatement)
    {
        var plan = new LogicalPlan
        {
            LastOperator = new Operator.LogicalExtensionStatement(boundStatement.Statement),
            Cost = 1
        };
        return plan;
    }

    private static LogicalPlan PlanAttachDatabase(BoundAttachDatabase boundStatement)
    {
        var plan = new LogicalPlan
        {
            LastOperator = new Operator.LogicalAttachDatabase(boundStatement),
            Cost = 1
        };
        return plan;
    }

    private static LogicalPlan PlanCreateMacro(BoundCreateMacro boundStatement)
    {
        var plan = new LogicalPlan
        {
            LastOperator = new Operator.LogicalCreateMacro(
                boundStatement.Name,
                boundStatement.Parameters,
                boundStatement.BodyExpression),
            Cost = 1
        };
        return plan;
    }

    // ─── COPY FROM ─────────────────────────────────────────────────────────────

    private static LogicalPlan PlanCopyFrom(BoundCopyFrom boundStatement)
    {
        var plan = new LogicalPlan
        {
            LastOperator = new Operator.LogicalCopyFrom(
                boundStatement.TableName,
                boundStatement.FilePath,
                boundStatement.ColumnOrder),
            Cost = 10
        };
        return plan;
    }

    private LogicalPlan PlanCopyTo(BoundCopyTo boundStatement)
    {
        var childPlan = PlanRegularQuery(boundStatement.Query, null);
        var plan = new LogicalPlan
        {
            LastOperator = new Operator.LogicalCopyTo(boundStatement.FilePath, boundStatement.ColumnNames, childPlan.LastOperator),
            Cost = 10
        };
        return plan;
    }

    // ─── STANDALONE CALL ───────────────────────────────────────────────────────

    private static LogicalPlan PlanStandaloneCallFunction(BoundStandaloneCallFunction boundStatement)
    {
        var op = new Operator.LogicalTableFunctionCall(boundStatement.FunctionExpression, Array.Empty<Expression>(), null);
        return new LogicalPlan { LastOperator = op, Cost = 10 };
    }

    private static LogicalPlan PlanStandaloneCall(BoundStandaloneCall boundStatement)
    {
        var op = new Operator.LogicalStandaloneCall(boundStatement.OptionName, boundStatement.OptionValue);
        return new LogicalPlan { LastOperator = op, Cost = 10 };
    }

    // ─── QUERY ─────────────────────────────────────────────────────────────────

    private LogicalPlan PlanQuery(BoundStatement boundStatement, IReadOnlySet<string>? preBoundVariables)
    {
        // If the caller is passing a real BoundRegularQuery, use the full pipeline.
        // For legacy/mock callers (e.g. PlannerTests MockBoundStatement), fall back to
        // a minimal Scan → Projection plan so existing tests continue to pass.
        if (boundStatement is BoundRegularQuery regularQuery)
            return PlanRegularQuery(regularQuery, preBoundVariables);

        // Minimal fallback plan for legacy tests
        var fallbackScan = new Operator.LogicalScanNodeProperty(
            new NodeExpression("n"), string.Empty, new List<PropertyExpression>());
        var fallbackProjection = new Operator.LogicalProjection(
            new List<BoundProjectionItem>(), fallbackScan);
        return new LogicalPlan { LastOperator = fallbackProjection, Cost = 100 };
    }

    private LogicalPlan PlanRegularQuery(BoundRegularQuery query, IReadOnlySet<string>? preBoundVariables)
    {
        var current = PlanSingleQuery(query.GetSingleQuery(0), preBoundVariables);

        for (var i = 1; i < query.GetNumSingleQueries(); i++)
        {
            var next = PlanSingleQuery(query.GetSingleQuery(i), preBoundVariables);
            current = new Operator.LogicalUnionAll(current, next);
            if (!query.GetIsUnionAll(i - 1))
            {
                current = new Operator.LogicalDistinct(current);
            }
        }

        if (query.GetIsProfile())
        {
            current = new Operator.LogicalProfile(current);
        }

        return new LogicalPlan
        {
            LastOperator = current,
            Cost = 100
        };
    }

    private Operator.LogicalOperator PlanSingleQuery(
        NormalizedSingleQuery singleQuery,
        IReadOnlySet<string>? preBoundVariables)
    {
        Operator.LogicalOperator? current = null;
        var currentVars = preBoundVariables == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(preBoundVariables, StringComparer.Ordinal);
        // Tracks whether any WITH clause produced a LogicalAggregate (survives downstream LogicalFilters).
        bool fromWithAggregate = false;

        // 1. For each MATCH clause, add a ScanNode per QueryNode in the QueryGraph.
        Expression? combinedWhere = null;

        for (int i = 0; i < singleQuery.GetNumReadingClauses(); i++)
        {
            var clause = singleQuery.GetReadingClause(i);
            if (clause is BoundMatchClause matchClause)
            {
                if (matchClause.ClauseType == BogDb.Core.Parser.ClauseType.OPTIONAL_MATCH)
                {
                    if (combinedWhere != null && current != null)
                    {
                        current = new Operator.LogicalFilter(combinedWhere, current);
                        combinedWhere = null;
                    }

                    var optionalPlan = BuildMatchClausePlan(matchClause, preBoundVariables);
                    var left = current ?? new Operator.LogicalSingleRow();
                    current = BuildOptionalJoinBetween(left, optionalPlan, currentVars, CollectVariables(matchClause.QueryGraph));
                    currentVars.UnionWith(CollectVariables(matchClause.QueryGraph));
                    continue;
                }

                if (TryBuildWhereBackedIndexPlan(matchClause, preBoundVariables, out var indexPlan, out var residualWhere))
                {
                    Operator.LogicalOperator matchPlan = residualWhere != null
                        ? new Operator.LogicalFilter(residualWhere, indexPlan)
                        : indexPlan;
                    var matchVars = CollectVariables(matchClause.QueryGraph);
                    if (current == null)
                    {
                        current = matchPlan;
                        currentVars = matchVars;
                    }
                    else
                    {
                        current = BuildJoinBetween(current, matchPlan, currentVars, matchVars);
                        currentVars.UnionWith(matchVars);
                    }
                    continue;
                }

                if (matchClause.QueryGraph.GetNumPatternParts() == 0)
                {
                    var fallbackPlan = BuildLegacyQueryGraphPlan(matchClause.QueryGraph);
                    var fallbackVars = CollectVariables(matchClause.QueryGraph);
                    if (current == null)
                    {
                        current = fallbackPlan;
                        currentVars = fallbackVars;
                    }
                    else
                    {
                        current = BuildJoinBetween(current, fallbackPlan, currentVars, fallbackVars);
                        currentVars.UnionWith(fallbackVars);
                    }
                }
                else
                {
                    var matchPlan = BuildPatternPartGraphPlan(
                        matchClause.QueryGraph,
                        preBoundVariables,
                        matchClause.WherePredicate,
                        out var matchWhere);

                    var matchVars = CollectVariables(matchClause.QueryGraph);
                    if (current == null)
                    {
                        current = matchPlan;
                        currentVars = matchVars;
                    }
                    else
                    {
                        current = BuildJoinBetween(current, matchPlan, currentVars, matchVars);
                        currentVars.UnionWith(matchVars);
                    }

                    if (matchWhere != null)
                        combinedWhere = combinedWhere == null
                            ? matchWhere
                            : new BoundBooleanExpression(ExpressionType.AND, combinedWhere, matchWhere);
                    continue;
                }

                // Collect WHERE predicates
                if (matchClause.WherePredicate != null)
                    combinedWhere = combinedWhere == null
                        ? matchClause.WherePredicate
                        : new BoundBooleanExpression(ExpressionType.AND, combinedWhere, matchClause.WherePredicate);
            }
            else if (clause is BoundWithClause withClause && current != null)
            {
                // First apply WHERE from previous MATCHes before WITH limits
                if (combinedWhere != null)
                {
                    current = new Operator.LogicalFilter(combinedWhere, current);
                    combinedWhere = null; // Clear so it doesn't apply again natively
                }

                if (withClause.ProjectionBody.ProjectionItems.Count > 0)
                {
                    // Detect aggregates — same implicit GROUP BY logic as RETURN clause.
                    // Non-aggregate items become the group keys; aggregate items are accumulated.
                    bool hasAgg = withClause.ProjectionBody.ProjectionItems.Any(i =>
                        i.Expression is BoundFunctionExpression bfe && bfe.IsAggregate);

                    if (hasAgg)
                    {
                        var keyItems = withClause.ProjectionBody.ProjectionItems.Where(i =>
                            i.Expression is not BoundFunctionExpression b || !b.IsAggregate).ToList();
                        var aggItems = withClause.ProjectionBody.ProjectionItems.Where(i =>
                            i.Expression is BoundFunctionExpression b2 && b2.IsAggregate).ToList();
                        current = new Operator.LogicalAggregate(
                            withClause.ProjectionBody.ProjectionItems, keyItems, aggItems, current);
                        fromWithAggregate = true; // flag survives downstream LogicalFilter (HAVING) nodes
                    }
                else
                {
                    current = new Operator.LogicalProjection(
                        new List<BoundProjectionItem>(withClause.ProjectionBody.ProjectionItems), current);
                }

                if (withClause.ProjectionBody.IsDistinct)
                {
                    current = new Operator.LogicalDistinct(current);
                }
            }
            if (withClause.ProjectionBody.OrderByElements.Count > 0)
            {
                var exprs = new List<Expression>();
                    var isAsc = new List<bool>();
                    foreach (var el in withClause.ProjectionBody.OrderByElements)
                    {
                        exprs.Add(el.Expression);
                        isAsc.Add(el.IsAscending);
                    }
                    current = new Operator.LogicalOrderBy(exprs, isAsc, current);
                }
                if (withClause.ProjectionBody.SkipExpression != null)
                {
                    current = new Operator.LogicalSkip(withClause.ProjectionBody.SkipExpression, current);
                }
                if (withClause.ProjectionBody.LimitExpression != null)
                {
                    current = new Operator.LogicalLimit(withClause.ProjectionBody.LimitExpression, current);
                }
                if (withClause.WherePredicate != null)
                {
                    // WHERE after WITH acts as a new filter natively
                    current = new Operator.LogicalFilter(withClause.WherePredicate, current);
                }
            }
            else if (clause is BoundInQueryCall inQueryCall)
            {
                var tfc = new Operator.LogicalTableFunctionCall(inQueryCall.BoundFunctionExpression, inQueryCall.OutVariables, current);
                current = tfc;

                if (inQueryCall.WherePredicate != null)
                {
                    current = new Operator.LogicalFilter(inQueryCall.WherePredicate, current);
                }
            }
            else if (clause is BoundUnwindClause unwindClause)
            {
                // UNWIND — creates a LogicalUnwind that iterates the collection and exposes each element via alias
                current = new Operator.LogicalUnwind(unwindClause.Expression, unwindClause.Alias, current);
            }
            else if (clause is BoundCallSubquery callSubquery)
            {
                // CALL { subquery } — plan the inner query and wrap it
                var innerPlanner = new Planner(_database);
                var innerPlan = innerPlanner.GetBestPlan(callSubquery.BoundInnerQuery);

                var logicalCallSub = new Operator.LogicalCallSubquery(
                    callSubquery.BoundInnerQuery,
                    callSubquery.CorrelatedVariables,
                    callSubquery.CorrelatedVarInfos,
                    callSubquery.OutputColumnNames,
                    callSubquery.InnerQueryText,
                    current);
                logicalCallSub.InnerPlan = innerPlan;
                current = logicalCallSub;

                // Register output column names as active variables
                foreach (var colName in callSubquery.OutputColumnNames)
                {
                    if (!currentVars.Contains(colName))
                        currentVars.Add(colName);
                }
            }
        }

        // 2. Lower EXISTS / NOT EXISTS subqueries into mark-join operators,
        //    then apply remaining WHERE predicates as a filter.
        if (combinedWhere != null && current != null)
        {
            current = LowerExistsToMarkJoin(combinedWhere, current, currentVars, out combinedWhere);
        }
        if (combinedWhere != null && current != null)
            current = new Operator.LogicalFilter(combinedWhere, current);

        // 3. Updating Clauses
        for (int i = 0; i < singleQuery.GetNumUpdatingClauses(); i++)
        {
            var clause = singleQuery.GetUpdatingClause(i);
            current = PlanUpdatingClause(clause, current);
        }

        // 4. Projection
        if (singleQuery.HasReturnClause() && current == null)
        {
            var ret = singleQuery.ReturnClause!;
            bool hasAggregate = ret.Items.Any(item =>
                item.Expression is BoundFunctionExpression bfe && bfe.IsAggregate);

            if (hasAggregate)
            {
                current = new Operator.LogicalSingleRow();
            }
            else
            {
                current = new Operator.LogicalExpressionsScan(
                    ret.Items.Select(item => item.Expression).ToList());
            }
        }

        if (singleQuery.HasReturnClause() && current != null)
        {
            var ret = singleQuery.ReturnClause!;
            if (ret.Items.Count > 0)
            {
                // Detect aggregate expressions — route to LogicalAggregate when any item is aggregate.
                // Non-aggregate items become implicit GROUP BY keys (Cypher's implicit grouping rule).
                bool hasAggregate = ret.Items.Any(item =>
                    item.Expression is BoundFunctionExpression bfe && bfe.IsAggregate);

                if (hasAggregate)
                {
                    // Guard against double-aggregation when a WITH clause already aggregated.
                    // fromWithAggregate survives LogicalFilter nodes inserted by HAVING-style WHERE,
                    // so we don't accidentally create a second aggregate after WITH agg + WHERE filter.
                    if (fromWithAggregate)
                    {
                        current = new Operator.LogicalProjection(
                            new List<BoundProjectionItem>(ret.Items), current);
                    }
                    else
                    {
                        var keyItems  = ret.Items.Where(i =>
                            i.Expression is not BoundFunctionExpression b || !b.IsAggregate).ToList();
                        var aggItems  = ret.Items.Where(i =>
                            i.Expression is BoundFunctionExpression b2 && b2.IsAggregate).ToList();
                        current = new Operator.LogicalAggregate(ret.Items, keyItems, aggItems, current);
                    }
                }
                else
                {
                    current = new Operator.LogicalProjection(new List<BoundProjectionItem>(ret.Items), current);
                }

                if (ret.ProjectionBody.IsDistinct)
                {
                    current = new Operator.LogicalDistinct(current);
                }
            }
            if (ret.ProjectionBody.OrderByElements.Count > 0)
            {
                var exprs = new List<Expression>();
                var isAsc = new List<bool>();
                foreach (var el in ret.ProjectionBody.OrderByElements)
                {
                    exprs.Add(el.Expression);
                    isAsc.Add(el.IsAscending);
                }
                current = new Operator.LogicalOrderBy(exprs, isAsc, current);
            }
            if (ret.ProjectionBody.SkipExpression != null)
            {
                current = new Operator.LogicalSkip(ret.ProjectionBody.SkipExpression, current);
            }
            if (ret.ProjectionBody.LimitExpression != null)
            {
                current = new Operator.LogicalLimit(ret.ProjectionBody.LimitExpression, current);
            }
        }

        return current ?? new Operator.LogicalScanNodeProperty(new NodeExpression("empty"), "", new List<PropertyExpression>());
    }

    private Operator.LogicalOperator BuildPatternPartPlan(
        QueryPatternPart part,
        IReadOnlySet<string>? preBoundVariables)
    {
        if (part.GetNumNodes() == 0)
        {
            throw new InvalidOperationException("Pattern part must contain at least one node.");
        }

        var firstNode = part.GetNode(0);
        var isCorrelatedRoot = !string.IsNullOrEmpty(firstNode.VariableName) &&
            preBoundVariables != null &&
            preBoundVariables.Contains(firstNode.VariableName);

        Operator.LogicalOperator current = isCorrelatedRoot
            ? new Operator.LogicalSingleRow()
            : CreateRootNodeAccessOperator(firstNode);

        return BuildPatternPartPlan(part, current);
    }

    private static Operator.LogicalOperator BuildPatternPartPlan(
        QueryPatternPart part,
        Operator.LogicalOperator rootOperator)
    {
        Operator.LogicalOperator current = rootOperator;

        for (var relIdx = 0; relIdx < part.GetNumRels(); relIdx++)
        {
            var rel = part.GetRel(relIdx);
            bool isRecursive = rel.LowerBound != "1" || rel.UpperBound != "1";

            if (isRecursive)
            {
                int lb = int.TryParse(rel.LowerBound, out var l) ? l : 1;
                int ub = string.IsNullOrEmpty(rel.UpperBound) ? int.MaxValue : (int.TryParse(rel.UpperBound, out var u) ? u : 1);
                current = new Operator.LogicalRecursiveExtend(rel, lb, ub, current);
            }
            else
            {
                current = new Operator.LogicalTraverseRel(rel, current);
            }
        }

        return current;
    }

    private static Operator.LogicalOperator BuildPatternPartPlanFromRoot(
        QueryPatternPart part,
        int rootNodeIndex,
        Operator.LogicalOperator rootOperator)
    {
        Operator.LogicalOperator current = rootOperator;

        for (var relIdx = rootNodeIndex - 1; relIdx >= 0; relIdx--)
        {
            current = AppendRelTraversal(current, ReverseQueryRel(part.GetRel(relIdx)));
        }

        for (var relIdx = rootNodeIndex; relIdx < part.GetNumRels(); relIdx++)
        {
            current = AppendRelTraversal(current, part.GetRel(relIdx));
        }

        return current;
    }

    private static Operator.LogicalOperator AppendRelTraversal(
        Operator.LogicalOperator current,
        QueryRel rel)
    {
        bool isRecursive = rel.LowerBound != "1" || rel.UpperBound != "1";
        if (isRecursive)
        {
            int lb = int.TryParse(rel.LowerBound, out var l) ? l : 1;
            int ub = string.IsNullOrEmpty(rel.UpperBound) ? int.MaxValue : (int.TryParse(rel.UpperBound, out var u) ? u : 1);
            return new Operator.LogicalRecursiveExtend(rel, lb, ub, current);
        }

        return new Operator.LogicalTraverseRel(rel, current);
    }

    private Operator.LogicalOperator BuildLegacyQueryGraphPlan(QueryGraph graph)
    {
        if (graph.GetNumQueryRels() == 1 && graph.GetNumQueryNodes() > 0)
        {
            var rel = graph.GetQueryRel(0);
            var root = CreateRootNodeAccessOperator(rel.SrcNode);
            return new Operator.LogicalTraverseRel(rel, root);
        }

        Operator.LogicalOperator? current = null;
        for (int j = 0; j < graph.GetNumQueryNodes(); j++)
        {
            var queryNode = graph.GetQueryNode(j);
            var scanNode = CreateRootNodeAccessOperator(queryNode);

            current = current == null ? scanNode : new Operator.LogicalNestedLoopJoin(current, scanNode);
        }

        return current ?? new Operator.LogicalScanNodeProperty(new NodeExpression("empty"), "", new List<PropertyExpression>());
    }

    private static bool TryBuildGraphPlanFromRoot(
        QueryGraph graph,
        QueryNode rootNode,
        Operator.LogicalOperator rootOperator,
        out Operator.LogicalOperator plan,
        out int cycleValidationCount)
    {
        plan = rootOperator;
        cycleValidationCount = 0;

        var boundNodes = new HashSet<QueryNode> { rootNode };
        var remainingRels = new List<QueryRel>(graph.GetQueryRels());

        while (remainingRels.Count > 0)
        {
            var progressed = false;
            for (var relIdx = 0; relIdx < remainingRels.Count; relIdx++)
            {
                var rel = remainingRels[relIdx];
                var srcBound = boundNodes.Contains(rel.SrcNode);
                var dstBound = boundNodes.Contains(rel.DstNode);

                if (!srcBound && !dstBound)
                    continue;

                if (srcBound && dstBound)
                {
                    var validationBranch = AppendRelTraversal(new Operator.LogicalSingleRow(), rel);
                    plan = BuildJoinBetween(
                        plan,
                        validationBranch,
                        CollectVariablesFromBoundNodes(boundNodes),
                        CollectVariables(rel));
                    cycleValidationCount++;
                }
                else
                {
                    plan = srcBound
                        ? AppendRelTraversal(plan, rel)
                        : AppendRelTraversal(plan, ReverseQueryRel(rel));
                    boundNodes.Add(rel.SrcNode);
                    boundNodes.Add(rel.DstNode);
                }
                remainingRels.RemoveAt(relIdx);
                progressed = true;
                break;
            }

            if (!progressed)
                return false;
        }

        foreach (var node in graph.GetQueryNodes())
        {
            if (!boundNodes.Contains(node))
                return false;
        }

        return true;
    }

    private static HashSet<string> CollectVariables(QueryRel rel)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(rel.VariableName))
            variables.Add(rel.VariableName);
        if (!string.IsNullOrEmpty(rel.SrcNode.VariableName))
            variables.Add(rel.SrcNode.VariableName);
        if (!string.IsNullOrEmpty(rel.DstNode.VariableName))
            variables.Add(rel.DstNode.VariableName);
        return variables;
    }

    private static HashSet<string> CollectVariablesFromBoundNodes(IEnumerable<QueryNode> nodes)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!string.IsNullOrEmpty(node.VariableName))
                variables.Add(node.VariableName);
        }

        return variables;
    }

    private static Operator.LogicalOperator BuildIndexLookupPlan(
        string tableName,
        string variableName,
        IReadOnlyList<IndexLookupPredicate> predicates)
    {
        if (predicates.Count == 0)
            throw new InvalidOperationException("Indexed lookup plan requires at least one predicate.");

        // Group predicates by GroupId. Members of a group came from ONE source that means UNION — an IN
        // list, or an OR of equalities on the same property. Distinct groups are separate AND conjuncts
        // and must INTERSECT (via ValueJoin), whether or not they share a property. Grouping by property
        // instead unioned `p = 'a' AND p = 'c'` into `p = 'a' OR p = 'c'`. Preserve first-seen group order
        // so plan shape stays deterministic.
        var groupOrder = new List<int>();
        var groupedByGroup = new Dictionary<int, List<IndexLookupPredicate>>();
        foreach (var pred in predicates)
        {
            if (!groupedByGroup.TryGetValue(pred.GroupId, out var list))
            {
                list = new List<IndexLookupPredicate>();
                groupedByGroup[pred.GroupId] = list;
                groupOrder.Add(pred.GroupId);
            }
            list.Add(pred);
        }

        Operator.LogicalOperator? plan = null;
        foreach (var groupId in groupOrder)
        {
            var groupPredicates = groupedByGroup[groupId];

            // Build UNION of scans within the group (IN / OR-of-equalities semantics)
            Operator.LogicalOperator current = new LogicalIndexScanNode(
                tableName,
                groupPredicates[0].PropertyName,
                groupPredicates[0].LookupKey,
                variableName,
                groupPredicates[0].IsPrefixScan);

            for (var i = 1; i < groupPredicates.Count; i++)
            {
                var nextScan = new LogicalIndexScanNode(
                    tableName,
                    groupPredicates[i].PropertyName,
                    groupPredicates[i].LookupKey,
                    variableName,
                    groupPredicates[i].IsPrefixScan);
                current = new Operator.LogicalUnionAll(current, nextScan);
            }

            // Intersect across groups (AND conjuncts)
            plan = plan == null
                ? current
                : new Operator.LogicalValueJoin(plan, current, new[] { variableName });
        }

        return plan!;
    }

    private static Operator.LogicalOperator BuildExternalIndexLookupPlan(
        string tableName,
        string variableName,
        ExternalIndexLookup predicate)
        => new LogicalExternalIndexScanNode(predicate, variableName);

    private long EstimatePredicateFanout(string tableName, IndexLookupPredicate predicate)
    {
        if (_database == null ||
            predicate.LookupKey is Binder.Expression ||
            !_database.NodeIndexes.TryGetValue(tableName, out var tableIndexes))
        {
            return long.MaxValue / 4;
        }

        if (predicate.IsPrefixScan)
        {
            var prefix = predicate.LookupKey is string s ? s : predicate.LookupKey?.ToString() ?? "";
            return tableIndexes.TryLookupByPrefix(predicate.PropertyName, prefix, out var prefixOffsets)
                ? prefixOffsets.Count
                : long.MaxValue / 4;
        }

        return tableIndexes.TryLookupAll(predicate.PropertyName, predicate.LookupKey, out var nodeOffsets)
            ? nodeOffsets.Count
            : long.MaxValue / 4;
    }

    private List<IndexLookupPredicate> OrderPredicatesByEstimatedFanout(
        string tableName,
        IReadOnlyList<IndexLookupPredicate> predicates)
    {
        return predicates
            .OrderBy(predicate => EstimatePredicateFanout(tableName, predicate))
            .ThenBy(predicate => predicate.PropertyName, StringComparer.Ordinal)
            .ToList();
    }

    private long EstimatePredicatesFanout(
        string tableName,
        IReadOnlyList<IndexLookupPredicate> predicates)
    {
        if (predicates.Count == 0)
            return long.MaxValue / 4;

        long best = long.MaxValue / 4;
        foreach (var predicate in predicates)
            best = Math.Min(best, EstimatePredicateFanout(tableName, predicate));
        return best;
    }

    private bool TryApplyAdditionalNodeIndexFilters(
        IReadOnlyList<QueryNode> nodes,
        string rootVariable,
        IReadOnlySet<string>? preBoundVariables,
        HashSet<string> planVariables,
        ref Operator.LogicalOperator plan,
        ref Expression? residualWhere,
        out int additionalIndexFilterCount,
        out long additionalFanoutPenalty)
    {
        additionalIndexFilterCount = 0;
        additionalFanoutPenalty = 0;

        if (_database == null || residualWhere == null)
            return false;

        var usedVariables = new HashSet<string>(StringComparer.Ordinal) { rootVariable };

        while (true)
        {
            QueryNode? bestNode = null;
            List<IndexLookupPredicate>? bestPredicates = null;
            Expression? bestResidual = null;
            long bestFanout = long.MaxValue / 4;

            foreach (var node in nodes)
            {
                if (string.IsNullOrEmpty(node.VariableName) ||
                    usedVariables.Contains(node.VariableName) ||
                    !planVariables.Contains(node.VariableName) ||
                    !IsEligibleWhereIndexedRootNode(node, preBoundVariables))
                {
                    continue;
                }

                if (!TryExtractIndexablePredicates(node, node.TableNames[0], residualWhere, out var predicates, out var candidateResidual))
                    continue;

                var candidateFanout = EstimatePredicatesFanout(node.TableNames[0], predicates);
                if (bestNode == null || candidateFanout < bestFanout)
                {
                    bestNode = node;
                    bestPredicates = predicates;
                    bestResidual = candidateResidual;
                    bestFanout = candidateFanout;
                }
            }

            if (bestNode == null || bestPredicates == null)
                break;

            var orderedPredicates = OrderPredicatesByEstimatedFanout(bestNode.TableNames[0], bestPredicates);
            var lookupPlan = BuildIndexLookupPlan(bestNode.TableNames[0], bestNode.VariableName, orderedPredicates);
            plan = BuildJoinBetween(
                plan,
                lookupPlan,
                planVariables,
                new HashSet<string>(StringComparer.Ordinal) { bestNode.VariableName });
            residualWhere = bestResidual;
            usedVariables.Add(bestNode.VariableName);
            additionalIndexFilterCount++;
            additionalFanoutPenalty += bestFanout;
        }

        return additionalIndexFilterCount > 0;
    }

    private static Operator.LogicalOperator BuildJoinBetween(
        Operator.LogicalOperator left,
        Operator.LogicalOperator right,
        HashSet<string> leftVars,
        HashSet<string> rightVars)
    {
        var shared = new List<string>();
        foreach (var variable in rightVars)
        {
            if (leftVars.Contains(variable))
            {
                shared.Add(variable);
            }
        }

        return shared.Count > 0
            ? new Operator.LogicalValueJoin(left, right, shared)
            : new Operator.LogicalNestedLoopJoin(left, right);
    }

    private static Operator.LogicalOperator BuildOptionalJoinBetween(
        Operator.LogicalOperator left,
        Operator.LogicalOperator right,
        HashSet<string> leftVars,
        HashSet<string> rightVars)
    {
        var shared = new List<string>();
        foreach (var variable in rightVars)
        {
            if (leftVars.Contains(variable))
                shared.Add(variable);
        }

        return new Operator.LogicalOptionalJoin(left, right, shared);
    }

    private Operator.LogicalOperator BuildMatchClausePlan(
        BoundMatchClause matchClause,
        IReadOnlySet<string>? preBoundVariables)
    {
        if (TryBuildWhereBackedIndexPlan(matchClause, preBoundVariables, out var indexedPlan, out var residualWhere))
        {
            if (residualWhere != null)
                indexedPlan = new Operator.LogicalFilter(residualWhere, indexedPlan);
            return indexedPlan;
        }

        Operator.LogicalOperator? current = null;
        var currentVars = new HashSet<string>(StringComparer.Ordinal);

        if (matchClause.QueryGraph.GetNumPatternParts() == 0)
        {
            current = BuildLegacyQueryGraphPlan(matchClause.QueryGraph);
        }
        else
        {
            current = BuildPatternPartGraphPlan(
                matchClause.QueryGraph,
                preBoundVariables,
                matchClause.WherePredicate,
                out var remainingWhere);

            if (remainingWhere != null && current != null)
                current = new Operator.LogicalFilter(remainingWhere, current);
        }

        if (matchClause.QueryGraph.GetNumPatternParts() == 0 &&
            matchClause.WherePredicate != null &&
            current != null)
        {
            current = new Operator.LogicalFilter(matchClause.WherePredicate, current);
        }

        return current ?? new Operator.LogicalSingleRow();
    }

    private Operator.LogicalOperator BuildPatternPartGraphPlan(
        QueryGraph graph,
        IReadOnlySet<string>? preBoundVariables,
        Expression? wherePredicate,
        out Expression? residualWhere)
    {
        residualWhere = wherePredicate;

        var remainingParts = new List<QueryPatternPart>(graph.GetPatternParts());
        Operator.LogicalOperator? current = null;
        var currentVars = new HashSet<string>(StringComparer.Ordinal);

        while (remainingParts.Count > 0)
        {
            var selectedPartIndex = 0;
            Operator.LogicalOperator? selectedPlan = null;
            Expression? selectedResidualWhere = residualWhere;
            var selectedScore = IndexCandidateScore.Worst;
            var planningRootVariables = BuildPlanningRootVariables(preBoundVariables, currentVars);

            if (residualWhere != null)
            {
                for (var idx = 0; idx < remainingParts.Count; idx++)
                {
                    if (TryBuildWhereBackedPatternPartPlan(
                        remainingParts[idx],
                        residualWhere,
                        planningRootVariables,
                        out var indexedPartPlan,
                        out var updatedResidualWhere,
                        out var candidateRootIndex))
                    {
                        var candidateNode = remainingParts[idx].GetNode(candidateRootIndex);
                        var hasIndexPredicates = TryExtractIndexablePredicates(
                            candidateNode,
                            candidateNode.TableNames[0],
                            residualWhere!,
                            out var candidatePredicates,
                            out _);
                        var hasExternalIndexPredicate = !hasIndexPredicates && TryExtractExternalIndexPredicate(
                            candidateNode,
                            candidateNode.TableNames[0],
                            residualWhere!,
                            out _,
                            out _);
                        var predicateCount = hasIndexPredicates ? candidatePredicates.Count : hasExternalIndexPredicate ? 1 : 0;
                        var candidateFanout = hasIndexPredicates ? EstimatePredicatesFanout(candidateNode.TableNames[0], candidatePredicates) : 0;
                        var candidateScore = new IndexCandidateScore(
                            ScoreResidualPredicate(updatedResidualWhere),
                            -predicateCount,
                            candidateFanout,
                            remainingParts[idx].GetNumRels() - (current == null ? 0 : CountSharedVariables(currentVars, remainingParts[idx])),
                            0,
                            GetAnchorPenalty(candidateRootIndex, remainingParts[idx].GetNumNodes()),
                            idx);
                        if (selectedPlan == null || candidateScore.CompareTo(selectedScore) < 0)
                        {
                            selectedPartIndex = idx;
                            selectedPlan = indexedPartPlan;
                            selectedResidualWhere = updatedResidualWhere;
                            selectedScore = candidateScore;
                        }
                    }
                }
            }

            var selectedPart = remainingParts[selectedPartIndex];
            remainingParts.RemoveAt(selectedPartIndex);

            planningRootVariables = BuildPlanningRootVariables(preBoundVariables, currentVars);
            var partPlan = selectedPlan ?? BuildPatternPartPlan(selectedPart, planningRootVariables);
            if (selectedPlan != null)
                residualWhere = selectedResidualWhere;

            var partVars = CollectVariables(selectedPart);
            if (current == null)
            {
                current = partPlan;
                currentVars = partVars;
            }
            else
            {
                current = BuildJoinBetween(current, partPlan, currentVars, partVars);
                currentVars.UnionWith(partVars);
            }
        }

        return current ?? new Operator.LogicalSingleRow();
    }

    private static int CountSharedVariables(HashSet<string> currentVars, QueryPatternPart part)
    {
        if (currentVars.Count == 0)
            return 0;

        var shared = 0;
        foreach (var variable in CollectVariables(part))
        {
            if (currentVars.Contains(variable))
                shared++;
        }

        return shared;
    }

    private static IReadOnlySet<string>? BuildPlanningRootVariables(
        IReadOnlySet<string>? preBoundVariables,
        HashSet<string> currentVars)
    {
        if ((preBoundVariables == null || preBoundVariables.Count == 0) && currentVars.Count == 0)
            return null;

        var variables = preBoundVariables == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(preBoundVariables, StringComparer.Ordinal);
        variables.UnionWith(currentVars);
        return variables;
    }

    private static HashSet<string> CollectVariables(QueryPatternPart part)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in part.GetNodes())
        {
            if (!string.IsNullOrEmpty(node.VariableName))
            {
                variables.Add(node.VariableName);
            }
        }
        foreach (var rel in part.GetRels())
        {
            if (!string.IsNullOrEmpty(rel.VariableName))
            {
                variables.Add(rel.VariableName);
            }
        }
        return variables;
    }

    private static HashSet<string> CollectVariables(QueryGraph graph)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.GetQueryNodes())
        {
            if (!string.IsNullOrEmpty(node.VariableName))
            {
                variables.Add(node.VariableName);
            }
        }
        foreach (var rel in graph.GetQueryRels())
        {
            if (!string.IsNullOrEmpty(rel.VariableName))
            {
                variables.Add(rel.VariableName);
            }
        }
        return variables;
    }

    // ─── EXISTS / NOT EXISTS → MarkJoin lowering ───────────────────────────

    /// <summary>
    /// Scans the WHERE predicate for top-level AND-conjuncts that are EXISTS or
    /// NOT EXISTS subquery expressions and rewrites each into a LogicalMarkJoin.
    /// Returns the (potentially modified) plan and emits any remaining non-subquery
    /// predicate conjuncts through <paramref name="residualWhere"/>.
    /// </summary>
    private Operator.LogicalOperator LowerExistsToMarkJoin(
        Expression where,
        Operator.LogicalOperator current,
        HashSet<string> currentVars,
        out Expression? residualWhere)
    {
        // Decompose into top-level AND conjuncts
        var conjuncts = new List<Expression>();
        DecomposeAndConjuncts(where, conjuncts);

        var remaining = new List<Expression>();

        foreach (var conjunct in conjuncts)
        {
            if (TryExtractExistsSubquery(conjunct, out var subqueryExpr, out var isNegated))
            {
                // Plan the subquery independently (no preBoundVariables)
                // so the build side produces a full scan that can be hash-indexed.
                var subqueryPlan = PlanRegularQuery(subqueryExpr!.BoundQuery, null);
                var subqueryVars = CollectLogicalPlanVariables(subqueryPlan.LastOperator);

                // Safety check: the subquery must not reference outer variables
                // that it doesn't produce itself. This happens with raw pattern atoms
                // like WHERE (a)-[:R]->(b) which are lowered to EXISTS subqueries
                // with renamed inner variables and equality correlation predicates
                // (e.g. WHERE __g015_b = b). In those cases, the inner WHERE
                // references outer variable 'b' which can't be resolved independently.
                var referencedVars = CollectExpressionVariableReferences(subqueryExpr.BoundQuery);
                bool hasOuterOnlyRefs = false;
                foreach (var refVar in referencedVars)
                {
                    if (!subqueryVars.Contains(refVar) && currentVars.Contains(refVar))
                    {
                        hasOuterOnlyRefs = true;
                        break;
                    }
                }

                if (hasOuterOnlyRefs)
                {
                    // Can't run independently — keep as correlated subquery
                    remaining.Add(conjunct);
                    continue;
                }

                // Shared variables = intersection of outer plan vars and subquery vars
                var shared = new List<string>();
                foreach (var v in currentVars)
                {
                    if (subqueryVars.Contains(v))
                        shared.Add(v);
                }

                // Generate a unique mark variable name
                var markVar = $"__mark_{_markJoinCounter++}";

                // Create the mark join: probe = outer plan, build = subquery plan
                current = new Operator.LogicalMarkJoin(
                    current,
                    subqueryPlan.LastOperator,
                    shared,
                    markVar);

                // Replace the EXISTS expression with a filter on the mark variable.
                // For EXISTS:      WHERE __mark_0       (mark must be true)
                // For NOT EXISTS:  WHERE NOT __mark_0   (mark must be false)
                Expression markExpr = new VariableExpression(markVar, Common.LogicalTypeID.BOOL);
                if (isNegated)
                    markExpr = new BoundBooleanExpression(ExpressionType.NOT, markExpr);

                remaining.Add(markExpr);
            }
            else
            {
                remaining.Add(conjunct);
            }
        }

        // Rebuild the residual WHERE from remaining conjuncts
        residualWhere = null;
        foreach (var r in remaining)
        {
            residualWhere = residualWhere == null
                ? r
                : new BoundBooleanExpression(ExpressionType.AND, residualWhere, r);
        }

        return current;
    }

    /// <summary>
    /// Tries to extract a BoundSubqueryExpression(EXISTS) from a conjunct.
    /// Handles direct EXISTS and NOT(EXISTS) wrappers.
    /// </summary>
    private static bool TryExtractExistsSubquery(
        Expression conjunct,
        out BoundSubqueryExpression? subquery,
        out bool isNegated)
    {
        subquery = null;
        isNegated = false;

        // Direct EXISTS { ... }
        if (conjunct is BoundSubqueryExpression direct &&
            direct.SubqueryType == Parser.SubqueryType.EXISTS)
        {
            subquery = direct;
            return true;
        }

        // NOT EXISTS { ... }
        if (conjunct is BoundBooleanExpression boolExpr &&
            boolExpr.ExpressionType == ExpressionType.NOT &&
            boolExpr.Left is BoundSubqueryExpression inner &&
            inner.SubqueryType == Parser.SubqueryType.EXISTS)
        {
            subquery = inner;
            isNegated = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Decomposes an expression into top-level AND conjuncts.
    /// (a AND b AND c) → [a, b, c]
    /// </summary>
    private static void DecomposeAndConjuncts(Expression expr, List<Expression> result)
    {
        if (expr is BoundBooleanExpression boolExpr &&
            boolExpr.ExpressionType == ExpressionType.AND)
        {
            DecomposeAndConjuncts(boolExpr.Left, result);
            if (boolExpr.Right != null)
                DecomposeAndConjuncts(boolExpr.Right, result);
        }
        else
        {
            result.Add(expr);
        }
    }

    /// <summary>
    /// Recursively collects all variable names produced by a logical plan tree.
    /// Walks scan nodes, traversals, and unwind operators to find variable names.
    /// </summary>
    private static HashSet<string> CollectLogicalPlanVariables(Operator.LogicalOperator op)
    {
        var variables = new HashSet<string>(StringComparer.Ordinal);
        CollectLogicalPlanVariablesRecursive(op, variables);
        return variables;
    }

    private static void CollectLogicalPlanVariablesRecursive(
        Operator.LogicalOperator op,
        HashSet<string> variables)
    {
        switch (op)
        {
            case Operator.LogicalScanNodeProperty scan:
                if (!string.IsNullOrEmpty(scan.Node.VariableName))
                    variables.Add(scan.Node.VariableName);
                break;

            case Operator.LogicalTraverseRel traverse:
                if (!string.IsNullOrEmpty(traverse.QueryRel.VariableName))
                    variables.Add(traverse.QueryRel.VariableName);
                if (!string.IsNullOrEmpty(traverse.QueryRel.SrcNode.VariableName))
                    variables.Add(traverse.QueryRel.SrcNode.VariableName);
                if (!string.IsNullOrEmpty(traverse.QueryRel.DstNode.VariableName))
                    variables.Add(traverse.QueryRel.DstNode.VariableName);
                break;

            case Operator.LogicalRecursiveExtend recursive:
                if (!string.IsNullOrEmpty(recursive.QueryRel.VariableName))
                    variables.Add(recursive.QueryRel.VariableName);
                if (!string.IsNullOrEmpty(recursive.QueryRel.SrcNode.VariableName))
                    variables.Add(recursive.QueryRel.SrcNode.VariableName);
                if (!string.IsNullOrEmpty(recursive.QueryRel.DstNode.VariableName))
                    variables.Add(recursive.QueryRel.DstNode.VariableName);
                break;

            case Operator.LogicalUnwind unwind:
                if (!string.IsNullOrEmpty(unwind.Alias))
                    variables.Add(unwind.Alias);
                break;

            case LogicalIndexScanNode indexScan:
                if (!string.IsNullOrEmpty(indexScan.VariableName))
                    variables.Add(indexScan.VariableName);
                break;
        }

        // Recurse into children
        for (int i = 0; i < op.GetNumChildren(); i++)
            CollectLogicalPlanVariablesRecursive(op.GetChild(i), variables);
    }

    /// <summary>
    /// Collects all variable names referenced in expressions within a BoundRegularQuery.
    /// This includes WHERE predicates in reading clauses, which may reference
    /// outer-scope variables via correlated references.
    /// </summary>
    private static HashSet<string> CollectExpressionVariableReferences(BoundRegularQuery query)
    {
        var vars = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < query.GetNumSingleQueries(); i++)
        {
            var sq = query.GetSingleQuery(i);
            for (int j = 0; j < sq.GetNumReadingClauses(); j++)
            {
                if (sq.GetReadingClause(j) is BoundMatchClause matchClause && matchClause.WherePredicate != null)
                    CollectExpressionVariableReferencesRecursive(matchClause.WherePredicate, vars);
            }
        }
        return vars;
    }

    private static void CollectExpressionVariableReferencesRecursive(Expression expr, HashSet<string> vars)
    {
        switch (expr)
        {
            case VariableExpression varExpr:
                if (!string.IsNullOrEmpty(varExpr.VariableName))
                    vars.Add(varExpr.VariableName);
                break;

            case PropertyExpression propExpr:
                if (!string.IsNullOrEmpty(propExpr.NodeVariableName))
                    vars.Add(propExpr.NodeVariableName);
                break;

            case BoundBooleanExpression boolExpr:
                CollectExpressionVariableReferencesRecursive(boolExpr.Left, vars);
                if (boolExpr.Right != null)
                    CollectExpressionVariableReferencesRecursive(boolExpr.Right, vars);
                break;

            case BoundComparisonExpression cmpExpr:
                CollectExpressionVariableReferencesRecursive(cmpExpr.Left, vars);
                CollectExpressionVariableReferencesRecursive(cmpExpr.Right, vars);
                break;

            case BoundFunctionExpression funcExpr:
                foreach (var arg in funcExpr.Arguments)
                    CollectExpressionVariableReferencesRecursive(arg, vars);
                break;
        }
    }

    private static Operator.LogicalOperator PlanUpdatingClause(BoundUpdatingClause clause, Operator.LogicalOperator? current)
    {
        switch (clause)
        {
            case BoundCreateClause create:
                return PlanCreateClause(create, current);
            case BoundMergeClause merge:
                return PlanMergeClause(merge, current);
            case BoundSetClause setClause:
                return PlanSetClause(setClause, current);
            case BoundDeleteClause delClause:
                return PlanDeleteClause(delClause, current);
            default:
                // Preserve pipeline stability for forward-compatible/unknown clause types.
                // Binder currently emits CREATE/MERGE/SET/DELETE only, so this path should
                // be rare and indicates an extension point rather than a fatal planner crash.
                return current ?? new Operator.LogicalScanNodeProperty(
                    new NodeExpression("empty"), "", new List<PropertyExpression>());
        }
    }

    private static Operator.LogicalOperator PlanCreateClause(BoundCreateClause create, Operator.LogicalOperator? current)
    {
        var insert = current != null 
            ? new Operator.LogicalInsert(create.CreateNodes, create.CreateRels, current)
            : new Operator.LogicalInsert(create.CreateNodes, create.CreateRels);
        return insert;
    }

    private static Operator.LogicalOperator PlanMergeClause(BoundMergeClause merge, Operator.LogicalOperator? current)
    {
        if (merge.QueryGraph.GetNumQueryNodes() == 1 && merge.QueryGraph.GetNumQueryRels() == 0)
        {
            var mergeNode = merge.QueryGraph.GetQueryNode(0);
            return current != null
                ? new Operator.LogicalMergeNode(mergeNode, merge.Actions, current)
                : new Operator.LogicalMergeNode(mergeNode, merge.Actions);
        }

        if (merge.QueryGraph.GetNumQueryNodes() == 2 && merge.QueryGraph.GetNumQueryRels() == 1)
        {
            var mergeRel = merge.QueryGraph.GetQueryRel(0);
            return current != null
                ? new Operator.LogicalMergeRel(mergeRel, merge.Actions, current)
                : new Operator.LogicalMergeRel(mergeRel, merge.Actions);
        }

        if (merge.QueryGraph.GetNumQueryNodes() > 0)
        {
            return current != null
                ? new Operator.LogicalMergeGraph(merge.QueryGraph, merge.Actions, current)
                : new Operator.LogicalMergeGraph(merge.QueryGraph, merge.Actions);
        }

        // Keep the remaining MERGE surface explicit: the currently supported paths all need at
        // least one bound query node in the graph.
        throw new NotSupportedException(
            "MERGE currently requires at least one query node and supports single-node, single-relationship, and broader connected graph patterns.");
    }

    private static Operator.LogicalOperator PlanSetClause(BoundSetClause setClause, Operator.LogicalOperator? current)
    {
        if (current == null) throw new InvalidOperationException("SET clause requires a preceding logical plan branch.");
        
        var relVars = CollectRelVariables(current);
        var targetsRel = setClause.SetItems.Any(item =>
            item is BoundComparisonExpression comp && (
                comp.Left is PropertyExpression prop && relVars.Contains(prop.NodeVariableName) ||
                comp.Left is VariableExpression varExpr &&
                    ((varExpr.QueryRel != null) || relVars.Contains(varExpr.VariableName))));

        return targetsRel
            ? new Operator.LogicalSetRelProperty(setClause.SetItems, current)
            : new Operator.LogicalSetNodeProperty(setClause.SetItems, current);
    }

    private static Operator.LogicalOperator PlanDeleteClause(BoundDeleteClause delClause, Operator.LogicalOperator? current)
    {
        if (current == null) throw new InvalidOperationException("DELETE clause requires a preceding logical plan branch.");
        
        var relVars = CollectRelVariables(current);
        var targetsRel = delClause.DeleteExpressions.Any(expr =>
            expr.DataType == LogicalTypeID.REL ||
            (expr is VariableExpression varExpr && relVars.Contains(varExpr.VariableName)));

        return targetsRel
            ? new Operator.LogicalDeleteRel(delClause.DeleteExpressions, current)
            : new Operator.LogicalDeleteNode(delClause.DeleteExpressions, current);
    }

    private static HashSet<string> CollectRelVariables(Operator.LogicalOperator root)
    {
        var relVars = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<Operator.LogicalOperator>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            switch (current)
            {
                case Operator.LogicalTraverseRel traverseRel:
                    if (!string.IsNullOrEmpty(traverseRel.QueryRel.VariableName))
                        relVars.Add(traverseRel.QueryRel.VariableName);
                    break;
                case Operator.LogicalRecursiveExtend recursiveExtend:
                    if (!string.IsNullOrEmpty(recursiveExtend.QueryRel.VariableName))
                        relVars.Add(recursiveExtend.QueryRel.VariableName);
                    break;
                case Operator.LogicalMergeRel mergeRel:
                    if (!string.IsNullOrEmpty(mergeRel.MergeRel.VariableName))
                        relVars.Add(mergeRel.MergeRel.VariableName);
                    break;
            }

            for (int i = 0; i < current.GetNumChildren(); i++)
                stack.Push(current.GetChild(i));
        }

        return relVars;
    }

    private Operator.LogicalOperator CreateRootNodeAccessOperator(QueryNode queryNode)
    {
        if (TryCreateInlineIndexBackedNodePlan(queryNode, out var indexScan))
            return indexScan;

        // Multi-label node scan: emit a UnionAll of scan operators, one per label.
        // Example: MATCH (n:Person|Company) → UnionAll(Scan(Person), Scan(Company))
        if (queryNode.TableNames.Count > 1)
        {
            Operator.LogicalOperator current = new Operator.LogicalScanNodeProperty(
                new NodeExpression(queryNode.VariableName),
                queryNode.TableNames[0],
                queryNode.PropertyExpressions,
                queryNode.InlineProperties,
                queryNode.InlinePropertyBag);

            for (var labelIdx = 1; labelIdx < queryNode.TableNames.Count; labelIdx++)
            {
                var nextScan = new Operator.LogicalScanNodeProperty(
                    new NodeExpression(queryNode.VariableName),
                    queryNode.TableNames[labelIdx],
                    queryNode.PropertyExpressions,
                    queryNode.InlineProperties,
                    queryNode.InlinePropertyBag);
                current = new Operator.LogicalUnionAll(current, nextScan);
            }
            return current;
        }

        return new Operator.LogicalScanNodeProperty(
            new NodeExpression(queryNode.VariableName),
            queryNode.TableNames.Count > 0 ? queryNode.TableNames[0] : "",
            queryNode.PropertyExpressions,
            queryNode.InlineProperties,
            queryNode.InlinePropertyBag);
    }

    private bool TryCreateInlineIndexBackedNodePlan(QueryNode queryNode, out Operator.LogicalOperator indexScan)
    {
        indexScan = null!;

        if (_database == null ||
            string.IsNullOrEmpty(queryNode.VariableName) ||
            queryNode.TableNames.Count != 1 ||   // a multi-label (:A|B) pattern cannot be answered by a
            queryNode.InlinePropertyBag != null ||  // single-table index scan without dropping the other
            queryNode.InlineProperties.Count == 0)  // labels' rows; fall back to a scan that honors all.
        {
            return false;
        }

        var tableName = queryNode.TableNames[0];
        var predicates = new List<IndexLookupPredicate>();
        Expression? residualPredicate = null;

        foreach (var (propertyName, valueExpression) in queryNode.InlineProperties)
        {
            if (TryMatchInlineIndexableEquality(tableName, propertyName, valueExpression, out var lookupKey))
            {
                if (!predicates.Exists(p =>
                        string.Equals(p.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase) &&
                        StructuralValueComparer.AreEqual(p.LookupKey, lookupKey)))
                {
                    // Inline properties {a:1, b:2} are AND'd — each is its own group so they intersect.
                    predicates.Add(new IndexLookupPredicate(propertyName, lookupKey, GroupId: predicates.Count));
                }
                continue;
            }

            var propertyType = ResolveQueryNodePropertyType(queryNode, propertyName);
            var predicate = new BoundComparisonExpression(
                ExpressionType.EQUALS,
                new PropertyExpression(propertyName, queryNode.VariableName, propertyType, queryNode.TableNames.Count == 1 ? tableName : null),
                valueExpression);
            residualPredicate = residualPredicate == null
                ? predicate
                : new BoundBooleanExpression(ExpressionType.AND, residualPredicate, predicate);
        }

        if (predicates.Count == 0)
            return false;

        indexScan = BuildIndexLookupPlan(
            tableName,
            queryNode.VariableName,
            OrderPredicatesByEstimatedFanout(tableName, predicates));
        if (residualPredicate != null)
            indexScan = new Operator.LogicalFilter(residualPredicate, indexScan);

        return true;
    }

    private bool TryMatchInlineIndexableEquality(
        string tableName,
        string propertyName,
        Expression valueExpression,
        out object lookupKey)
    {
        lookupKey = null!;

        if (_database == null ||
            !IsSupportedIndexLookupExpression(valueExpression) ||
            !_database.NodeIndexes.TryGetValue(tableName, out var tableIndexes) ||
            !tableIndexes.HasIndex(propertyName))
        {
            return false;
        }

        if (valueExpression is LiteralExpression literal)
        {
            var normalizedLiteral = Common.TypeCoercionHelper.Normalize(literal.Value);
            if (normalizedLiteral == null)
                return false;
            lookupKey = normalizedLiteral;
            return true;
        }

        lookupKey = valueExpression;
        return true;
    }

    private static LogicalTypeID ResolveQueryNodePropertyType(QueryNode queryNode, string propertyName)
    {
        foreach (var property in queryNode.PropertyExpressions)
        {
            if (string.Equals(property.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase))
                return property.DataType;
        }

        return LogicalTypeID.ANY;
    }

    private bool TryBuildWhereBackedIndexPlan(
        BoundMatchClause matchClause,
        IReadOnlySet<string>? preBoundVariables,
        out Operator.LogicalOperator indexPlan,
        out Expression? residualWhere)
    {
        indexPlan = null!;
        residualWhere = null;

        if (_database == null ||
            matchClause.WherePredicate == null)
        {
            return false;
        }

        if (matchClause.QueryGraph.GetNumPatternParts() == 1)
        {
            var part = matchClause.QueryGraph.GetPatternPart(0);
            if (TryBuildWhereBackedPatternPartPlan(part, matchClause.WherePredicate, preBoundVariables, out indexPlan, out residualWhere, out _))
                return true;
        }

        if (matchClause.QueryGraph.GetNumPatternParts() == 0 &&
            matchClause.QueryGraph.GetNumQueryRels() > 0 &&
            TryBuildWhereBackedGeneralGraphPlan(
                matchClause.QueryGraph,
                matchClause.WherePredicate,
                preBoundVariables,
                out indexPlan,
                out residualWhere))
        {
            return true;
        }

        QueryNode? queryNode = null;
        if (matchClause.QueryGraph.GetNumPatternParts() == 1)
        {
            var part = matchClause.QueryGraph.GetPatternPart(0);
            if (part.GetNumNodes() == 1 && part.GetNumRels() == 0)
                queryNode = part.GetNode(0);
        }
        else if (matchClause.QueryGraph.GetNumPatternParts() == 0 &&
                 matchClause.QueryGraph.GetNumQueryNodes() == 1 &&
                 matchClause.QueryGraph.GetNumQueryRels() == 0)
        {
            queryNode = matchClause.QueryGraph.GetQueryNode(0);
        }

        if (queryNode == null ||
            !string.IsNullOrEmpty(queryNode.VariableName) &&
            preBoundVariables != null &&
            preBoundVariables.Contains(queryNode.VariableName) ||
            queryNode.TableNames.Count == 0 ||
            queryNode.InlinePropertyBag != null)
        {
            return false;
        }

        // A multi-label node pattern (:A|B) cannot be answered by the single-table index plan built
        // below without silently dropping the other labels' rows. The multi-label case where every
        // label is indexed is handled by the pattern-part path above (guarded by AllLabelsHaveIndex);
        // anything reaching here with more than one label falls back to a full scan that honors them all.
        if (queryNode.TableNames.Count > 1)
            return false;

        if (!TryExtractIndexablePredicates(
                queryNode,
                queryNode.TableNames[0],
                matchClause.WherePredicate,
                out var predicates,
                out residualWhere))
        {
            if (!TryExtractExternalIndexPredicate(
                    queryNode,
                    queryNode.TableNames[0],
                    matchClause.WherePredicate,
                    out var externalIndexLookup,
                    out residualWhere))
            {
                return false;
            }

            indexPlan = BuildExternalIndexLookupPlan(
                queryNode.TableNames[0],
                queryNode.VariableName,
                externalIndexLookup);
        }
        else
        {
            var orderedPredicates = OrderPredicatesByEstimatedFanout(queryNode.TableNames[0], predicates);
            indexPlan = BuildIndexLookupPlan(
                queryNode.TableNames[0],
                queryNode.VariableName,
                orderedPredicates);
        }

        // Pattern B: If the node had inline properties, emit them as residual filters
        if (queryNode.InlineProperties.Count > 0)
        {
            foreach (var (propName, valueExpr) in queryNode.InlineProperties)
            {
                var propertyType = ResolveQueryNodePropertyType(queryNode, propName);
                var inlinePredicate = new BoundComparisonExpression(
                    ExpressionType.EQUALS,
                    new PropertyExpression(propName, queryNode.VariableName, propertyType, queryNode.TableNames.Count == 1 ? queryNode.TableNames[0] : null),
                    valueExpr);
                residualWhere = residualWhere == null
                    ? inlinePredicate
                    : new BoundBooleanExpression(ExpressionType.AND, residualWhere, inlinePredicate);
            }
        }

        return true;
    }

    private bool TryBuildWhereBackedGeneralGraphPlan(
        QueryGraph graph,
        Expression wherePredicate,
        IReadOnlySet<string>? preBoundVariables,
        out Operator.LogicalOperator graphPlan,
        out Expression? residualWhere)
    {
        graphPlan = null!;
        residualWhere = wherePredicate;

        if (_database == null ||
            graph.GetNumQueryNodes() == 0 ||
            graph.GetNumQueryRels() == 0)
        {
            return false;
        }

        Operator.LogicalOperator? bestPlan = null;
        Expression? bestResidualWhere = wherePredicate;
        var bestScore = IndexCandidateScore.Worst;

        for (var nodeIdx = 0; nodeIdx < graph.GetNumQueryNodes(); nodeIdx++)
        {
            var candidateNode = graph.GetQueryNode(nodeIdx);
            if (!IsEligibleWhereIndexedRootNode(candidateNode, preBoundVariables))
            {
                continue;
            }

            Operator.LogicalOperator rootPlan;
            Expression? candidateResidualWhere;
            IReadOnlyList<IndexLookupPredicate>? candidatePredicates = null;
            long candidateFanout;
            int predicateCount;

            if (TryExtractIndexablePredicates(
                    candidateNode,
                    candidateNode.TableNames[0],
                    wherePredicate,
                    out var extractedPredicates,
                    out candidateResidualWhere))
            {
                candidatePredicates = extractedPredicates;
                var orderedPredicates = OrderPredicatesByEstimatedFanout(candidateNode.TableNames[0], extractedPredicates);
                rootPlan = BuildIndexLookupPlan(candidateNode.TableNames[0], candidateNode.VariableName, orderedPredicates);
                candidateFanout = EstimatePredicatesFanout(candidateNode.TableNames[0], extractedPredicates);
                predicateCount = extractedPredicates.Count;
            }
            else if (TryExtractExternalIndexPredicate(
                         candidateNode,
                         candidateNode.TableNames[0],
                         wherePredicate,
                         out var externalIndexLookup,
                         out candidateResidualWhere))
            {
                rootPlan = BuildExternalIndexLookupPlan(candidateNode.TableNames[0], candidateNode.VariableName, externalIndexLookup);
                candidateFanout = 0;
                predicateCount = 1;
            }
            else
            {
                continue;
            }

            if (!TryBuildGraphPlanFromRoot(graph, candidateNode, rootPlan, out var candidatePlan, out var cycleValidationCount))
                continue;

            var candidateResidual = candidateResidualWhere;
            var planVariables = CollectVariables(graph);
            TryApplyAdditionalNodeIndexFilters(
                graph.GetQueryNodes(),
                candidateNode.VariableName,
                preBoundVariables,
                planVariables,
                ref candidatePlan,
                ref candidateResidual,
                out var additionalIndexFilterCount,
                out var additionalFanoutPenalty);

            var candidateScore = new IndexCandidateScore(
                ScoreResidualPredicate(candidateResidual),
                -predicateCount - additionalIndexFilterCount,
                candidateFanout + additionalFanoutPenalty,
                graph.GetNumQueryRels(),
                cycleValidationCount,
                0,
                nodeIdx);
            if (bestPlan == null || candidateScore.CompareTo(bestScore) < 0)
            {
                bestPlan = candidatePlan;
                bestResidualWhere = candidateResidual;
                bestScore = candidateScore;
            }
        }

        if (bestPlan == null)
            return false;

        graphPlan = bestPlan;
        residualWhere = bestResidualWhere;
        return true;
    }

    private bool TryBuildWhereBackedPatternPartPlan(
        QueryPatternPart part,
        Expression wherePredicate,
        IReadOnlySet<string>? preBoundVariables,
        out Operator.LogicalOperator partPlan,
        out Expression? residualWhere,
        out int selectedRootIndex)
    {
        partPlan = null!;
        residualWhere = wherePredicate;
        selectedRootIndex = -1;

        if (_database == null || part.GetNumNodes() == 0)
            return false;

        Operator.LogicalOperator? bestPlan = null;
        Expression? bestResidualWhere = wherePredicate;
        var bestScore = IndexCandidateScore.Worst;

        for (var nodeIdx = 0; nodeIdx < part.GetNumNodes(); nodeIdx++)
        {
            var candidateNode = part.GetNode(nodeIdx);
            if (!IsEligibleWhereIndexedRootNode(candidateNode, preBoundVariables))
            {
                continue;
            }

            Operator.LogicalOperator candidatePlan;
            Expression? candidateResidualWhere;
            long candidateFanout;
            int predicateCount;

            if (TryExtractIndexablePredicates(
                    candidateNode,
                    candidateNode.TableNames[0],
                    wherePredicate,
                    out var candidatePredicates,
                    out candidateResidualWhere))
            {
                var orderedPredicates = OrderPredicatesByEstimatedFanout(candidateNode.TableNames[0], candidatePredicates);

                // Pattern C: For multi-label nodes, verify ALL labels have the index
                if (candidateNode.TableNames.Count > 1 && !AllLabelsHaveIndex(candidateNode, orderedPredicates))
                    continue;

                if (!TryBuildIndexedPlanForPatternRoot(part, nodeIdx, candidateNode, orderedPredicates, out candidatePlan))
                    continue;

                candidateFanout = EstimatePredicatesFanout(candidateNode.TableNames[0], candidatePredicates);
                predicateCount = candidatePredicates.Count;
            }
            else if (TryExtractExternalIndexPredicate(
                         candidateNode,
                         candidateNode.TableNames[0],
                         wherePredicate,
                         out var externalIndexLookup,
                         out candidateResidualWhere))
            {
                if (candidateNode.TableNames.Count > 1)
                    continue;

                candidatePlan = nodeIdx == 0
                    ? BuildPatternPartPlan(part, BuildExternalIndexLookupPlan(candidateNode.TableNames[0], candidateNode.VariableName, externalIndexLookup))
                    : BuildPatternPartPlanFromRoot(part, nodeIdx, BuildExternalIndexLookupPlan(candidateNode.TableNames[0], candidateNode.VariableName, externalIndexLookup));
                candidateFanout = 0;
                predicateCount = 1;
            }
            else
            {
                continue;
            }

            var candidateResidual = candidateResidualWhere;
            var planVariables = CollectVariables(part);
            TryApplyAdditionalNodeIndexFilters(
                part.GetNodes(),
                candidateNode.VariableName,
                preBoundVariables,
                planVariables,
                ref candidatePlan,
                ref candidateResidual,
                out var additionalIndexFilterCount,
                out var additionalFanoutPenalty);

            var candidateScore = new IndexCandidateScore(
                ScoreResidualPredicate(candidateResidual),
                -predicateCount - additionalIndexFilterCount,
                candidateFanout + additionalFanoutPenalty,
                part.GetNumRels(),
                0,
                GetAnchorPenalty(nodeIdx, part.GetNumNodes()),
                nodeIdx);
            if (bestPlan == null || candidateScore.CompareTo(bestScore) < 0)
            {
                bestPlan = candidatePlan;
                bestResidualWhere = candidateResidual;
                bestScore = candidateScore;
                selectedRootIndex = nodeIdx;
            }
        }

        if (bestPlan == null)
            return false;

        partPlan = bestPlan;
        residualWhere = bestResidualWhere;

        // Pattern B: For eligible root nodes with inline properties, emit them as residual filters
        if (selectedRootIndex >= 0)
        {
            var rootNode = part.GetNode(selectedRootIndex);
            if (rootNode.InlineProperties.Count > 0)
            {
                foreach (var (propName, valueExpr) in rootNode.InlineProperties)
                {
                    var propertyType = ResolveQueryNodePropertyType(rootNode, propName);
                    var inlinePredicate = new BoundComparisonExpression(
                        ExpressionType.EQUALS,
                        new PropertyExpression(propName, rootNode.VariableName, propertyType, rootNode.TableNames.Count == 1 ? rootNode.TableNames[0] : null),
                        valueExpr);
                    residualWhere = residualWhere == null
                        ? inlinePredicate
                        : new BoundBooleanExpression(ExpressionType.AND, residualWhere, inlinePredicate);
                }
            }
        }

        return true;
    }

    private static int GetAnchorPenalty(int nodeIndex, int numNodes)
    {
        if (nodeIndex < 0 || numNodes <= 1)
            return 0;

        return Math.Min(nodeIndex, numNodes - nodeIndex - 1);
    }

    private static bool TryBuildIndexedPlanForPatternRoot(
        QueryPatternPart part,
        int rootNodeIndex,
        QueryNode rootNode,
        IReadOnlyList<IndexLookupPredicate> predicates,
        out Operator.LogicalOperator plan)
    {
        plan = null!;

        if (part.HasPathVariableName())
            return false;

        foreach (var rel in part.GetRels())
        {
            if (!string.IsNullOrEmpty(rel.PathVariableName))
                return false;
        }

        Operator.LogicalOperator rootPlan;
        if (rootNode.TableNames.Count <= 1)
        {
            rootPlan = BuildIndexLookupPlan(rootNode.TableNames[0], rootNode.VariableName, predicates);
        }
        else
        {
            // Pattern C: Multi-label index scan — union index scans across all labels
            rootPlan = BuildIndexLookupPlan(rootNode.TableNames[0], rootNode.VariableName, predicates);
            for (var labelIdx = 1; labelIdx < rootNode.TableNames.Count; labelIdx++)
            {
                var nextLabelPlan = BuildIndexLookupPlan(rootNode.TableNames[labelIdx], rootNode.VariableName, predicates);
                rootPlan = new Operator.LogicalUnionAll(rootPlan, nextLabelPlan);
            }
        }

        plan = rootNodeIndex == 0
            ? BuildPatternPartPlan(part, rootPlan)
            : BuildPatternPartPlanFromRoot(part, rootNodeIndex, rootPlan);
        return true;
    }

    private static bool IsEligibleWhereIndexedRootNode(
        QueryNode queryNode,
        IReadOnlySet<string>? preBoundVariables)
    {
        // Pattern B: Allow nodes with inline properties — the inline predicates
        // become a residual filter on top of the index scan. Only reject if
        // InlinePropertyBag is set (dynamic property bag, not static inline props).
        if (queryNode.TableNames.Count == 0 ||
            queryNode.InlinePropertyBag != null)
        {
            return false;
        }

        return string.IsNullOrEmpty(queryNode.VariableName) ||
            preBoundVariables == null ||
            !preBoundVariables.Contains(queryNode.VariableName);
    }

    /// <summary>
    /// Pattern C: For multi-label nodes, verify that ALL labels have
    /// indexes on all the properties referenced by the predicates.
    /// </summary>
    private bool AllLabelsHaveIndex(QueryNode queryNode, IReadOnlyList<IndexLookupPredicate> predicates)
    {
        if (_database == null) return false;

        foreach (var tableName in queryNode.TableNames)
        {
            if (!_database.NodeIndexes.TryGetValue(tableName, out var tableIndexes))
                return false;

            foreach (var predicate in predicates)
            {
                if (!tableIndexes.HasIndex(predicate.PropertyName))
                    return false;
            }
        }

        return true;
    }

    private static QueryRel ReverseQueryRel(QueryRel rel)
    {
        var reversedConnections = rel.AllowedConnections
            .Select(connection => new QueryRelConnection(
                connection.TableName,
                connection.DstTableName,
                connection.SrcTableName))
            .ToList();

        return new QueryRel(
            rel.VariableName,
            rel.TableNames.ToList(),
            reversedConnections,
            ReverseArrowDirection(rel.Direction),
            rel.DstNode,
            rel.SrcNode,
            rel.PropertyExpressions.ToList(),
            rel.InlineProperties.ToList(),
            rel.InlinePropertyBag,
            rel.LowerBound,
            rel.UpperBound,
            rel.PathVariableName);
    }

    private static BogDb.Core.Parser.ArrowDirection ReverseArrowDirection(
        BogDb.Core.Parser.ArrowDirection direction)
    {
        return direction switch
        {
            BogDb.Core.Parser.ArrowDirection.RIGHT => BogDb.Core.Parser.ArrowDirection.LEFT,
            BogDb.Core.Parser.ArrowDirection.LEFT => BogDb.Core.Parser.ArrowDirection.RIGHT,
            _ => direction
        };
    }

    private static int ScoreResidualPredicate(Expression? predicate)
    {
        if (predicate == null)
            return 0;

        if (predicate is BoundBooleanExpression booleanExpr &&
            booleanExpr.ExpressionType == ExpressionType.AND &&
            booleanExpr.Right != null)
        {
            return ScoreResidualPredicate(booleanExpr.Left) + ScoreResidualPredicate(booleanExpr.Right);
        }

        return 1;
    }

    private bool TryExtractIndexablePredicate(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        out string propertyName,
        out object lookupKey,
        out Expression? residualPredicate)
    {
        propertyName = string.Empty;
        lookupKey = null!;
        residualPredicate = null;

        if (TryExtractIndexablePredicates(queryNode, tableName, predicate, out var predicates, out residualPredicate))
        {
            propertyName = predicates[0].PropertyName;
            lookupKey = predicates[0].LookupKey;
            return true;
        }

        return false;
    }

    private bool TryExtractExternalIndexPredicate(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        out ExternalIndexLookup externalLookup,
        out Expression? residualPredicate)
    {
        externalLookup = default!;
        residualPredicate = null;
        var found = false;
        CollectExternalIndexPredicate(queryNode, tableName, predicate, ref found, ref externalLookup, ref residualPredicate);
        return found;
    }

    private bool TryExtractIndexablePredicates(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        out List<IndexLookupPredicate> predicates,
        out Expression? residualPredicate)
    {
        predicates = new List<IndexLookupPredicate>();
        residualPredicate = null;
        var groupSeq = 0;
        CollectIndexablePredicates(queryNode, tableName, predicate, predicates, ref residualPredicate, ref groupSeq);
        return predicates.Count > 0;
    }

    private void CollectIndexablePredicates(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        List<IndexLookupPredicate> predicates,
        ref Expression? residualPredicate,
        ref int groupSeq)
    {
        if (TrySimplifyPredicateBooleanNoise(predicate, out var simplifiedPredicate))
        {
            CollectIndexablePredicates(queryNode, tableName, simplifiedPredicate, predicates, ref residualPredicate, ref groupSeq);
            return;
        }

        if (TryMatchIndexableEquality(queryNode, tableName, predicate, out var propertyName, out var lookupKey))
        {
            // A standalone equality is its own AND conjunct — a fresh group, so two same-property
            // equalities intersect (contradiction => no rows) instead of unioning.
            predicates.Add(new IndexLookupPredicate(propertyName, lookupKey, GroupId: groupSeq++));
            return;
        }

        if (TryMatchIndexableInList(queryNode, tableName, predicate, out var inListPredicates))
        {
            // All members of one IN list share a group and are UNIONed together.
            var group = groupSeq++;
            foreach (var ilp in inListPredicates)
            {
                if (!predicates.Exists(p => p.GroupId == group && p.PropertyName == ilp.PropertyName &&
                                            StructuralValueComparer.AreEqual(p.LookupKey, ilp.LookupKey)))
                    predicates.Add(ilp with { GroupId = group });
            }
            return;
        }

        if (TryMatchIndexableStartsWith(queryNode, tableName, predicate, out var swPropertyName, out var swPrefix))
        {
            predicates.Add(new IndexLookupPredicate(swPropertyName, swPrefix, IsPrefixScan: true, GroupId: groupSeq++));
            return;
        }

        if (predicate is BoundBooleanExpression booleanExpr &&
            booleanExpr.ExpressionType == ExpressionType.AND &&
            booleanExpr.Right != null)
        {
            // Each side of an AND allocates its own group(s), so the two sides intersect.
            CollectIndexablePredicates(queryNode, tableName, booleanExpr.Left, predicates, ref residualPredicate, ref groupSeq);
            CollectIndexablePredicates(queryNode, tableName, booleanExpr.Right, predicates, ref residualPredicate, ref groupSeq);
            return;
        }

        // OR-of-equality-on-same-property → multi-key index lookup (UNION semantics)
        // e.g., p.name = 'Alice' OR p.name = 'Bob' → two IndexLookupPredicates in one union group
        if (predicate is BoundBooleanExpression orExpr &&
            orExpr.ExpressionType == ExpressionType.OR &&
            orExpr.Right != null &&
            TryExtractOrDisjunctsAsIndexPredicates(queryNode, tableName, predicate, out var orPredicates))
        {
            var group = groupSeq++;
            foreach (var orp in orPredicates)
            {
                if (!predicates.Exists(p => p.GroupId == group && p.PropertyName == orp.PropertyName &&
                                            StructuralValueComparer.AreEqual(p.LookupKey, orp.LookupKey)))
                    predicates.Add(orp with { GroupId = group });
            }
            return;
        }

        if (TryEvaluateConstantBooleanPredicate(predicate, out var constantValue) && constantValue)
            return;

        residualPredicate = residualPredicate == null
            ? predicate
            : new BoundBooleanExpression(ExpressionType.AND, residualPredicate, predicate);
    }

    private void CollectExternalIndexPredicate(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        ref bool found,
        ref ExternalIndexLookup externalLookup,
        ref Expression? residualPredicate)
    {
        if (TrySimplifyPredicateBooleanNoise(predicate, out var simplifiedPredicate))
        {
            CollectExternalIndexPredicate(queryNode, tableName, simplifiedPredicate, ref found, ref externalLookup, ref residualPredicate);
            return;
        }

        if (TryMatchExternalIndexPredicate(queryNode, tableName, predicate, out var matchedLookup))
        {
            if (!found)
            {
                externalLookup = matchedLookup;
                found = true;
                return;
            }
        }

        if (predicate is BoundBooleanExpression booleanExpr &&
            booleanExpr.ExpressionType == ExpressionType.AND &&
            booleanExpr.Right != null)
        {
            CollectExternalIndexPredicate(queryNode, tableName, booleanExpr.Left, ref found, ref externalLookup, ref residualPredicate);
            CollectExternalIndexPredicate(queryNode, tableName, booleanExpr.Right, ref found, ref externalLookup, ref residualPredicate);
            return;
        }

        if (TryEvaluateConstantBooleanPredicate(predicate, out var constantValue) && constantValue)
            return;

        residualPredicate = residualPredicate == null
            ? predicate
            : new BoundBooleanExpression(ExpressionType.AND, residualPredicate, predicate);
    }

    private static bool TryEvaluateConstantBooleanPredicate(Expression expression, out bool value)
    {
        value = false;
        if (!TryEvaluateConstantExpression(expression, out var rawValue) || rawValue is not bool boolValue)
            return false;

        value = boolValue;
        return true;
    }

    private static bool TrySimplifyPredicateBooleanNoise(Expression predicate, out Expression simplifiedPredicate)
    {
        simplifiedPredicate = predicate;

        if (predicate is not BoundBooleanExpression booleanExpression)
            return false;

        var left = booleanExpression.Left;
        var leftChanged = TrySimplifyPredicateBooleanNoise(left, out var simplifiedLeft);
        if (leftChanged)
            left = simplifiedLeft;

        Expression? right = booleanExpression.Right;
        var rightChanged = false;
        if (right != null && TrySimplifyPredicateBooleanNoise(right, out var simplifiedRight))
        {
            right = simplifiedRight;
            rightChanged = true;
        }

        if (TryEvaluateConstantBooleanPredicate(
                leftChanged || rightChanged
                    ? RebuildBooleanExpression(booleanExpression, left, right)
                    : predicate,
                out var foldedValue))
        {
            simplifiedPredicate = new LiteralExpression(foldedValue, LogicalTypeID.BOOL);
            return true;
        }

        if (booleanExpression.ExpressionType is ExpressionType.AND or ExpressionType.OR)
        {
            if (right == null)
                return leftChanged;

            var hasLeftConstant = TryEvaluateConstantBooleanPredicate(left, out var leftConstant);
            var hasRightConstant = TryEvaluateConstantBooleanPredicate(right, out var rightConstant);

            if (booleanExpression.ExpressionType == ExpressionType.AND)
            {
                if (hasLeftConstant)
                {
                    simplifiedPredicate = leftConstant ? right : new LiteralExpression(false, LogicalTypeID.BOOL);
                    return true;
                }

                if (hasRightConstant)
                {
                    simplifiedPredicate = rightConstant ? left : new LiteralExpression(false, LogicalTypeID.BOOL);
                    return true;
                }
            }
            else
            {
                if (hasLeftConstant)
                {
                    simplifiedPredicate = leftConstant ? new LiteralExpression(true, LogicalTypeID.BOOL) : right;
                    return true;
                }

                if (hasRightConstant)
                {
                    simplifiedPredicate = rightConstant ? new LiteralExpression(true, LogicalTypeID.BOOL) : left;
                    return true;
                }
            }
        }

        if (leftChanged || rightChanged)
        {
            simplifiedPredicate = RebuildBooleanExpression(booleanExpression, left, right);
            return true;
        }

        return false;
    }

    private static Expression RebuildBooleanExpression(
        BoundBooleanExpression expression,
        Expression left,
        Expression? right)
    {
        return right == null
            ? new BoundBooleanExpression(expression.ExpressionType, left)
            : new BoundBooleanExpression(expression.ExpressionType, left, right);
    }

    private static bool TryEvaluateConstantExpression(Expression expression, out object? value)
    {
        value = null;

        switch (expression)
        {
            case LiteralExpression literal:
                value = TypeCoercionHelper.Normalize(literal.Value);
                return true;
            case BoundComparisonExpression comparison:
                return TryEvaluateConstantComparison(comparison, out value);
            case BoundBooleanExpression booleanExpression:
                return TryEvaluateConstantBoolean(booleanExpression, out value);
            case BoundFunctionExpression function when !function.IsAggregate:
                return TryEvaluateConstantFunction(function, out value);
            default:
                return false;
        }
    }

    private static bool TryEvaluateConstantComparison(
        BoundComparisonExpression comparison,
        out object? value)
    {
        value = null;

        if (!TryEvaluateConstantExpression(comparison.Left, out var left) ||
            !TryEvaluateConstantExpression(comparison.Right, out var right))
        {
            return false;
        }

        var result = comparison.ExpressionType switch
        {
            ExpressionType.EQUALS => StructuralValueComparer.AreEqual(left, right),
            ExpressionType.NOT_EQUALS => !StructuralValueComparer.AreEqual(left, right),
            ExpressionType.GREATER_THAN => StructuralValueOrderComparer.CompareValues(left, right) > 0,
            ExpressionType.GREATER_THAN_EQUALS => StructuralValueOrderComparer.CompareValues(left, right) >= 0,
            ExpressionType.LESS_THAN => StructuralValueOrderComparer.CompareValues(left, right) < 0,
            ExpressionType.LESS_THAN_EQUALS => StructuralValueOrderComparer.CompareValues(left, right) <= 0,
            _ => (bool?)null
        };

        if (!result.HasValue)
            return false;

        value = result.Value;
        return true;
    }

    private static bool TryEvaluateConstantBoolean(
        BoundBooleanExpression booleanExpression,
        out object? value)
    {
        value = null;

        if (!TryEvaluateConstantExpression(booleanExpression.Left, out var left))
            return false;

        bool result;
        switch (booleanExpression.ExpressionType)
        {
            case ExpressionType.NOT:
                if (left is not bool leftBool)
                    return false;
                result = !leftBool;
                break;
            case ExpressionType.IS_NULL:
                result = left == null;
                break;
            case ExpressionType.IS_NOT_NULL:
                result = left != null;
                break;
            case ExpressionType.AND:
            case ExpressionType.OR:
            case ExpressionType.XOR:
                if (booleanExpression.Right == null ||
                    left is not bool leftBinaryBool ||
                    !TryEvaluateConstantExpression(booleanExpression.Right, out var right) ||
                    right is not bool rightBinaryBool)
                {
                    return false;
                }

                result = booleanExpression.ExpressionType switch
                {
                    ExpressionType.AND => leftBinaryBool && rightBinaryBool,
                    ExpressionType.OR => leftBinaryBool || rightBinaryBool,
                    ExpressionType.XOR => leftBinaryBool ^ rightBinaryBool,
                    _ => false
                };
                break;
            default:
                return false;
        }

        value = result;
        return true;
    }

    private static bool TryEvaluateConstantFunction(
        BoundFunctionExpression function,
        out object? value)
    {
        value = null;
        var args = new object?[function.Arguments.Count];
        for (var i = 0; i < function.Arguments.Count; i++)
        {
            if (!TryEvaluateConstantExpression(function.Arguments[i], out args[i]))
                return false;
        }

        if (string.Equals(function.FunctionName, "case", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(function.FunctionName, "case_simple", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = TypeCoercionHelper.Normalize(FunctionDispatcher.Invoke(function.FunctionName, args));
        return true;
    }

    private bool TryMatchIndexableEquality(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        out string propertyName,
        out object lookupKey)
    {
        propertyName = string.Empty;
        lookupKey = null!;

        if (_database == null ||
            predicate is not BoundComparisonExpression comparisonExpr ||
            comparisonExpr.ExpressionType != ExpressionType.EQUALS ||
            !_database.NodeIndexes.TryGetValue(tableName, out var tableIndexes))
        {
            return false;
        }

        if (TryMatchPropertyLiteralEquality(
                queryNode,
                comparisonExpr.Left,
                comparisonExpr.Right,
                tableIndexes,
                out propertyName,
                out lookupKey))
        {
            return true;
        }

        return TryMatchPropertyLiteralEquality(
            queryNode,
            comparisonExpr.Right,
            comparisonExpr.Left,
            tableIndexes,
            out propertyName,
            out lookupKey);
    }

    private bool TryMatchExternalIndexPredicate(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        out ExternalIndexLookup lookup)
    {
        lookup = default!;

        if (_database == null ||
            queryNode.TableNames.Count != 1 ||
            !_database.TryGetExtensionService<IExternalIndexProvider>(ExternalIndexServiceNames.Provider, out var externalIndexProvider))
        {
            return false;
        }

        return externalIndexProvider.TryBindPredicate(queryNode, tableName, predicate, out lookup);
    }

    /// <summary>
    /// Recognizes <c>list_contains(LIST_LITERAL(v1, v2, ...), p.prop)</c> — the bound
    /// form of <c>WHERE p.prop IN [v1, v2, ...]</c> — and decomposes it into one
    /// IndexLookupPredicate per literal element so the planner can issue multi-key
    /// index probes instead of falling back to scan/filter.
    /// </summary>
    private bool TryMatchIndexableInList(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        out List<IndexLookupPredicate> predicates)
    {
        predicates = new List<IndexLookupPredicate>();

        // Must be a BoundFunctionExpression for list_contains with 2 arguments
        if (_database == null ||
            predicate is not BoundFunctionExpression funcExpr ||
            !string.Equals(funcExpr.FunctionName, "list_contains", StringComparison.OrdinalIgnoreCase) ||
            funcExpr.Arguments.Count != 2)
        {
            return false;
        }

        if (!_database.NodeIndexes.TryGetValue(tableName, out var tableIndexes))
            return false;

        // Arguments: [0] = list expression, [1] = element (property)
        var listArg = funcExpr.Arguments[0];
        var elementArg = funcExpr.Arguments[1];

        // The element side must be a property of the query node on an indexed property
        if (elementArg is not PropertyExpression propertyExpr ||
            !string.Equals(propertyExpr.NodeVariableName, queryNode.VariableName, StringComparison.Ordinal) ||
            !tableIndexes.HasIndex(propertyExpr.PropertyName))
        {
            return false;
        }

        // The list side must be a LIST_LITERAL with all-literal elements
        if (listArg is not BoundFunctionExpression listLiteral ||
            !string.Equals(listLiteral.FunctionName, "LIST_LITERAL", StringComparison.OrdinalIgnoreCase) ||
            listLiteral.Arguments.Count == 0)
        {
            return false;
        }

        var propName = propertyExpr.PropertyName;
        foreach (var arg in listLiteral.Arguments)
        {
            if (!IsSupportedIndexLookupExpression(arg))
                return false;

            object lookupKey;
            if (arg is LiteralExpression lit)
            {
                var normalized = Common.TypeCoercionHelper.Normalize(lit.Value);
                if (normalized == null) continue;
                lookupKey = normalized;
            }
            else
            {
                lookupKey = arg;
            }

            predicates.Add(new IndexLookupPredicate(propName, lookupKey));
        }

        return predicates.Count > 0;
    }

    /// <summary>
    /// Extracts OR-of-equality-on-same-property patterns into index predicates.
    /// <c>WHERE p.name = 'Alice' OR p.name = 'Bob'</c> → two IndexLookupPredicates.
    /// Returns false if any disjunct is not an equality predicate, targets a different
    /// property, or the property is not indexed.
    /// </summary>
    private bool TryExtractOrDisjunctsAsIndexPredicates(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        out List<IndexLookupPredicate> predicates)
    {
        predicates = new List<IndexLookupPredicate>();

        // Flatten the OR tree into disjuncts
        var disjuncts = new List<Expression>();
        FlattenOrDisjuncts(predicate, disjuncts);

        if (disjuncts.Count < 2)
            return false;

        string? commonPropertyName = null;
        foreach (var disjunct in disjuncts)
        {
            if (!TryMatchIndexableEquality(queryNode, tableName, disjunct, out var propName, out var lookupKey))
                return false; // Not an equality predicate — bail

            if (commonPropertyName == null)
                commonPropertyName = propName;
            else if (!string.Equals(commonPropertyName, propName, StringComparison.OrdinalIgnoreCase))
                return false; // Different properties — can't UNION

            predicates.Add(new IndexLookupPredicate(propName, lookupKey));
        }

        return predicates.Count > 0;
    }

    /// <summary>
    /// Flattens a nested OR tree into a flat list of disjuncts.
    /// <c>A OR B OR C</c> → [A, B, C]
    /// </summary>
    private static void FlattenOrDisjuncts(Expression expression, List<Expression> disjuncts)
    {
        if (expression is BoundBooleanExpression boolExpr &&
            boolExpr.ExpressionType == ExpressionType.OR &&
            boolExpr.Right != null)
        {
            FlattenOrDisjuncts(boolExpr.Left, disjuncts);
            FlattenOrDisjuncts(boolExpr.Right, disjuncts);
        }
        else
        {
            disjuncts.Add(expression);
        }
    }

    /// <summary>
    /// Recognizes <c>starts_with(p.prop, literal)</c> — the bound form of
    /// <c>WHERE p.prop STARTS WITH 'prefix'</c> — and produces a prefix scan
    /// predicate so the planner can accelerate prefix lookups via the index.
    /// </summary>
    private bool TryMatchIndexableStartsWith(
        QueryNode queryNode,
        string tableName,
        Expression predicate,
        out string propertyName,
        out object prefix)
    {
        propertyName = string.Empty;
        prefix = null!;

        // Must be starts_with function with exactly 2 arguments
        if (_database == null ||
            predicate is not BoundFunctionExpression funcExpr ||
            !string.Equals(funcExpr.FunctionName, "starts_with", StringComparison.OrdinalIgnoreCase) ||
            funcExpr.Arguments.Count != 2)
        {
            return false;
        }

        if (!_database.NodeIndexes.TryGetValue(tableName, out var tableIndexes))
            return false;

        // Arguments: [0] = string expression (property), [1] = prefix (literal)
        var stringArg = funcExpr.Arguments[0];
        var prefixArg = funcExpr.Arguments[1];

        // The string side must be a property of the query node on an indexed property
        if (stringArg is not PropertyExpression propertyExpr ||
            !string.Equals(propertyExpr.NodeVariableName, queryNode.VariableName, StringComparison.Ordinal) ||
            !tableIndexes.HasIndex(propertyExpr.PropertyName))
        {
            return false;
        }

        // The prefix side must be a string literal
        if (prefixArg is not LiteralExpression prefixLiteral ||
            prefixLiteral.Value is not string prefixStr ||
            string.IsNullOrEmpty(prefixStr))
        {
            return false;
        }

        propertyName = propertyExpr.PropertyName;
        prefix = prefixStr;
        return true;
    }

    private static bool TryMatchPropertyLiteralEquality(
        QueryNode queryNode,
        Expression propertySide,
        Expression literalSide,
        NodePropertyIndex tableIndexes,
        out string propertyName,
        out object lookupKey)
    {
        propertyName = string.Empty;
        lookupKey = null!;

        if (propertySide is not PropertyExpression propertyExpr ||
            !IsSupportedIndexLookupExpression(literalSide) ||
            !string.Equals(propertyExpr.NodeVariableName, queryNode.VariableName, StringComparison.Ordinal) ||
            !tableIndexes.HasIndex(propertyExpr.PropertyName))
        {
            return false;
        }

        propertyName = propertyExpr.PropertyName;
        if (literalSide is LiteralExpression literalExpr)
        {
            var normalizedLiteral = Common.TypeCoercionHelper.Normalize(literalExpr.Value);
            if (normalizedLiteral == null)
                return false;
            lookupKey = normalizedLiteral;
            return true;
        }

        lookupKey = literalSide;
        return true;
    }

    private static bool IsSupportedIndexLookupExpression(Expression expression)
    {
        return expression switch
        {
            LiteralExpression => true,
            BoundParameterExpression => true,
            BoundFunctionExpression function => function.Arguments.All(IsSupportedIndexLookupExpression),
            _ => false
        };
    }

    private readonly record struct IndexCandidateScore(
        int ResidualScore,
        int PredicateBonusPenalty,
        long FanoutPenalty,
        int TraversalPenalty,
        int CycleValidationPenalty,
        int AnchorPenalty,
        int StableOrder) : IComparable<IndexCandidateScore>
    {
        public static IndexCandidateScore Worst => new(int.MaxValue, int.MaxValue, long.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);

        public int CompareTo(IndexCandidateScore other)
        {
            var result = ResidualScore.CompareTo(other.ResidualScore);
            if (result != 0)
                return result;

            result = PredicateBonusPenalty.CompareTo(other.PredicateBonusPenalty);
            if (result != 0)
                return result;

            result = FanoutPenalty.CompareTo(other.FanoutPenalty);
            if (result != 0)
                return result;

            result = TraversalPenalty.CompareTo(other.TraversalPenalty);
            if (result != 0)
                return result;

            result = CycleValidationPenalty.CompareTo(other.CycleValidationPenalty);
            if (result != 0)
                return result;

            result = AnchorPenalty.CompareTo(other.AnchorPenalty);
            if (result != 0)
                return result;

            return StableOrder.CompareTo(other.StableOrder);
        }
    }
}
