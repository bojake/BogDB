using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Optimizer;

/// <summary>
/// Optimizer rule: fuse <see cref="LogicalOrderBy"/> immediately followed by
/// <see cref="LogicalLimit"/> into a single <see cref="LogicalTopK"/> node.
///
/// A top-K heap requires only O(n·log K) time vs O(n·log n) for a full sort,
/// and keeps only K rows in memory instead of the full sorted dataset.
///
/// C++ parity: src/optimizer/top_k_optimizer.cpp
/// </summary>
public sealed class TopKOptimizerRule : LogicalRule
{
    public override bool Rewrite(LogicalPlan plan)
    {
        var (newRoot, changed) = Rewrite(plan.LastOperator);
        plan.LastOperator = newRoot;
        return changed;
    }

    /// <summary>
    /// Recursively walks the operator tree bottom-up.
    /// When a LIMIT whose direct child is an ORDER_BY is found, replaces both
    /// with a single TOP_K node that carries the sort keys and the limit expression.
    /// Returns (newOp, didChange).
    /// </summary>
    private static (LogicalOperator op, bool changed) Rewrite(LogicalOperator op)
    {
        bool anyChanged = false;

        // Recurse into children first (bottom-up)
        for (int i = 0; i < op.GetNumChildren(); i++)
        {
            var (newChild, childChanged) = Rewrite(op.Children[i]);
            if (childChanged)
            {
                op.Children[i] = newChild;
                anyChanged = true;
            }
        }

        // Pattern: LIMIT( ORDER_BY( child ) )
        if (op is LogicalLimit limit &&
            limit.GetChild(0) is LogicalOrderBy orderBy)
        {
            var topK = new LogicalTopK(
                orderBy.Expressions,
                orderBy.IsAscending,
                limit.LimitExpression,
                orderBy.GetChild(0));
            return (topK, true);
        }

        return (op, anyChanged);
    }
}
