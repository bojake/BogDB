using System.Collections.Generic;
using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Optimizer;

/// <summary>
/// C++ parity: <c>correlated_subquery_unnest_solver.cpp</c>
///
/// Detects correlated subquery expressions nested inside <c>LogicalFilter</c>
/// nodes (e.g. WHERE EXISTS (...), WHERE NOT EXISTS (...)) and, when possible,
/// unnests them into equivalent mark-join sub-plans.
///
/// <b>Current implementation:</b> The primary EXISTS/NOT EXISTS → MarkJoin lowering
/// is done directly in the <see cref="Planner"/> at plan-build time (see
/// <c>Planner.LowerExistsToMarkJoin</c>). This optimizer rule serves as a
/// secondary pass that catches any remaining correlated subquery candidates
/// that were not lowered at plan-build time — e.g., subqueries that reference
/// outer variables through correlation predicates (like raw pattern atoms
/// <c>WHERE (a)-[:R]-&gt;(b)</c> which carry renamed inner variables and
/// equality correlation predicates).
///
/// When the binder is extended to produce proper correlated-subquery logical
/// operators for these edge cases, this rule should be updated with the full
/// unnesting rewrite. Until then, it correctly returns <c>false</c> (no rewrite).
///
/// Effects today: always returns false (plan unchanged) — the planner handles
/// all cases that can be lowered to mark-join directly.
/// </summary>
public sealed class CorrelatedSubqueryUnnestRule : LogicalRule
{
    public override bool Rewrite(LogicalPlan plan)
    {
        if (plan.LastOperator == null) return false;

        // Scan for candidates — correlated-subquery logical nodes
        bool hasCandidate = VisitForCandidates(plan.LastOperator, new HashSet<LogicalOperator>());
        if (!hasCandidate) return false;

        // TODO: implement secondary unnesting rewrite for edge cases that
        // the planner-side LowerExistsToMarkJoin could not handle
        // (e.g., raw pattern atoms with renamed correlation variables).
        // Return false (plan unchanged) until then — safe no-op.
        return false;
    }

    private static bool VisitForCandidates(LogicalOperator op, HashSet<LogicalOperator> visited)
    {
        if (!visited.Add(op)) return false;

        // Check if any child is a correlated-subquery indicator type
        for (int i = 0; i < op.GetNumChildren(); i++)
        {
            var child = op.GetChild(i);
            // Probe for correlated subquery operator types as they become available
            if (IsCorrelatedSubqueryNode(child)) return true;
            if (VisitForCandidates(child, visited)) return true;
        }
        return false;
    }

    private static bool IsCorrelatedSubqueryNode(LogicalOperator op)
    {
        // As BogDB's binder is extended, add correlated subquery operator type checks here.
        // e.g.:  op.OperatorType == LogicalOperatorType.LOGICAL_EXISTS
        //     || op.OperatorType == LogicalOperatorType.LOGICAL_IN_QUERY_CALL
        return false;
    }
}
