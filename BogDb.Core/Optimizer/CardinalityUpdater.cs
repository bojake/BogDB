using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;
using BogDb.Core.Main;

namespace BogDb.Core.Optimizer;

/// <summary>
/// Scans through the logical plan tree bottom-up, computing estimated cardinality
/// for each operator using storage-level table sizes and standard selectivity factors.
/// These estimates drive cost-based join ordering in JoinOrderOptimizer.
/// </summary>
public sealed class CardinalityUpdater : LogicalRule
{
    private const double DefaultFilterSelectivity = 0.1;   // 10% pass rate for non-index filters
    private const double DefaultLimitSelectivity = 0.05;   // 5% pass rate for LIMIT
    private const double DefaultRelFanout = 10.0;          // average out-degree per node
    private const double DefaultTableFunctionRows = 100.0;

    private readonly BogDatabase _database;

    public CardinalityUpdater(BogDatabase database)
    {
        _database = database;
    }

    public override bool Rewrite(LogicalPlan plan)
    {
        if (plan.LastOperator != null)
        {
            UpdateCardinality(plan.LastOperator);
            plan.Cardinality = (ulong)System.Math.Max(0, plan.LastOperator.EstCardinality);
        }
        
        return false;
    }

    private void UpdateCardinality(LogicalOperator op)
    {
        foreach (var child in op.Children)
        {
            UpdateCardinality(child);
        }

        switch (op.OperatorType)
        {
            case LogicalOperatorType.LOGICAL_SCAN_NODE:
                var scanNode = (LogicalScanNodeProperty)op;
                op.EstCardinality = EstimateTableCardinality(scanNode.TableName);
                break;

            case LogicalOperatorType.LOGICAL_INDEX_SCAN_NODE:
                // Index scans are highly selective — estimated fanout typically small.
                op.EstCardinality = 1.0;
                break;

            case LogicalOperatorType.LOGICAL_RECURSIVE_EXTEND:
            case LogicalOperatorType.LOGICAL_EXTEND:
                var childCount = op.HasChild() ? op.GetChild(0).EstCardinality : 1.0;
                op.EstCardinality = childCount * DefaultRelFanout;
                break;

            case LogicalOperatorType.LOGICAL_CROSS_PRODUCT:
                var leftCard = op.GetChild(0).EstCardinality;
                var rightCard = op.GetChild(1).EstCardinality;
                op.EstCardinality = leftCard * rightCard;
                break;

            case LogicalOperatorType.LOGICAL_HASH_JOIN:
                // Value join: estimated as min of the two sides (inner join selectivity).
                leftCard = op.GetChild(0).EstCardinality;
                rightCard = op.GetChild(1).EstCardinality;
                op.EstCardinality = System.Math.Min(leftCard, rightCard);
                break;

            case LogicalOperatorType.LOGICAL_OPTIONAL_JOIN:
                // Left outer join: preserves all left rows.
                op.EstCardinality = op.GetChild(0).EstCardinality;
                break;

            case LogicalOperatorType.LOGICAL_FILTER:
                op.EstCardinality = op.GetChild(0).EstCardinality * DefaultFilterSelectivity;
                break;

            case LogicalOperatorType.LOGICAL_LIMIT:
                var limitOp = (Planner.Operator.LogicalLimit)op;
                var childEst = op.GetChild(0).EstCardinality;
                if (limitOp.LimitExpression is Binder.LiteralExpression lit && lit.Value is long limitVal)
                    op.EstCardinality = System.Math.Min(childEst, limitVal);
                else
                    op.EstCardinality = childEst * DefaultLimitSelectivity;
                break;

            case LogicalOperatorType.LOGICAL_SKIP:
                var skipOp = (Planner.Operator.LogicalSkip)op;
                childEst = op.GetChild(0).EstCardinality;
                if (skipOp.SkipExpression is Binder.LiteralExpression skipLit && skipLit.Value is long skipVal)
                    op.EstCardinality = System.Math.Max(0, childEst - skipVal);
                else
                    op.EstCardinality = childEst * 0.5;
                break;

            case LogicalOperatorType.LOGICAL_AGGREGATE:
                // Aggregation with group keys: estimate as fraction of child.
                // Without keys: exactly 1 row.
                var aggOp = (Planner.Operator.LogicalAggregate)op;
                op.EstCardinality = aggOp.KeyItems.Count > 0
                    ? op.GetChild(0).EstCardinality * DefaultFilterSelectivity
                    : 1.0;
                break;

            case LogicalOperatorType.LOGICAL_DISTINCT:
                // Distinct reduces rows — estimate as fraction of child.
                op.EstCardinality = op.GetChild(0).EstCardinality * 0.5;
                break;

            case LogicalOperatorType.LOGICAL_UNION_ALL:
                // Union of two branches: sum of both.
                op.EstCardinality = op.GetChild(0).EstCardinality + op.GetChild(1).EstCardinality;
                break;

            case LogicalOperatorType.TABLE_FUNCTION_CALL:
                op.EstCardinality = DefaultTableFunctionRows;
                break;

            case LogicalOperatorType.LOGICAL_UNWIND:
                // Unwind expands each row by the average list length.
                op.EstCardinality = (op.HasChild() ? op.GetChild(0).EstCardinality : 1.0) * DefaultRelFanout;
                break;

            case LogicalOperatorType.LOGICAL_TOP_K:
                var topKOp = (Planner.Operator.LogicalTopK)op;
                childEst = op.GetChild(0).EstCardinality;
                if (topKOp.LimitExpression is Binder.LiteralExpression topKLit && topKLit.Value is long topKVal)
                    op.EstCardinality = System.Math.Min(childEst, topKVal);
                else
                    op.EstCardinality = childEst * DefaultLimitSelectivity;
                break;

            default:
                // Pass-through: projection, order by, accumulate, flatten, etc.
                op.EstCardinality = op.HasChild() ? op.GetChild(0).EstCardinality : 1.0;
                break;
        }
    }

    private double EstimateTableCardinality(string tableName)
    {
        if (string.IsNullOrEmpty(tableName))
            return 1000.0;

        // Use actual storage row count if available.
        if (_database?.NodeTables != null && _database.NodeTables.TryGetValue(tableName, out var nodeTable))
        {
            var count = nodeTable.Count;
            return count > 0 ? count : 1.0;
        }

        // Fallback: unknown table, use default estimate.
        return 1000.0;
    }
}
