using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Optimizer;

/// <summary>
/// Dynamic programming-based join ordering optimizer (IK-KBB family).
///
/// For queries with 3+ join operands (pattern parts joined via hash join or
/// cross product), this rule explores all possible binary join trees and picks
/// the one with lowest estimated cost.
///
/// The algorithm:
///   1. Extract all leaf operands from a left-deep join tree
///   2. Build a DP table: for each subset of operands, store the best plan
///   3. Start with single-element subsets (base case = leaf cardinality)
///   4. For each subset size 2..N, try all binary partitions into
///      two non-empty subsets and pick the partition with minimum cost
///   5. Replace the original join tree with the optimal one
///
/// Cost model: C_out = estimated output cardinality of the join tree.
/// For hash joins: min(left, right). For cross products: left × right.
/// Total cost = sum of intermediate cardinalities (Cout model).
///
/// Complexity: O(3^N) for N join operands (subset enumeration).
/// Only activated for N >= 3 and N <= 12 (beyond 12, the space is too large).
/// </summary>
public sealed class DPJoinEnumerator : LogicalRule
{
    private const int MinOperandsForDP = 3;
    private const int MaxOperandsForDP = 12;

    // Track already-processed join roots to prevent infinite re-optimization
    // in the optimizer's fixed-point loop.
    private readonly HashSet<LogicalOperator> _optimized = new();

    public override bool Rewrite(LogicalPlan plan)
    {
        bool changed = false;
        if (plan.LastOperator != null)
        {
            plan.LastOperator = OptimizeJoinTree(plan.LastOperator, ref changed);
        }
        return changed;
    }

    private LogicalOperator OptimizeJoinTree(LogicalOperator op, ref bool changed)
    {
        // Recurse into children first (bottom-up)
        for (int i = 0; i < op.Children.Count; i++)
        {
            op.Children[i] = OptimizeJoinTree(op.Children[i], ref changed);
        }

        // Check if this operator is the root of a join tree we can optimize
        if (!IsJoinOperator(op))
            return op;

        // Extract all leaf operands from a contiguous join tree
        var operands = new List<LogicalOperator>();
        var joinEdges = new List<JoinEdge>();
        ExtractJoinTree(op, operands, joinEdges);

        if (operands.Count < MinOperandsForDP || operands.Count > MaxOperandsForDP)
            return op;

        // Skip if already optimized this set of leaf operands
        if (_optimized.Contains(op))
            return op;

        // Run DP enumeration
        var bestPlan = EnumerateJoinOrders(operands, joinEdges);
        if (bestPlan != null)
        {
            _optimized.Add(bestPlan);
            changed = true;
            return bestPlan;
        }

        return op;
    }

    /// <summary>
    /// Represents a join edge between two operand subsets, capturing
    /// shared variables for hash join or absence thereof (cross product).
    /// </summary>
    private readonly struct JoinEdge
    {
        public int LeftIndex { get; }
        public int RightIndex { get; }
        public IReadOnlyList<string>? SharedVariables { get; }

        public JoinEdge(int left, int right, IReadOnlyList<string>? sharedVars)
        {
            LeftIndex = left;
            RightIndex = right;
            SharedVariables = sharedVars;
        }
    }

    /// <summary>
    /// DP table entry: best plan for a given subset of operands.
    /// </summary>
    private sealed class DPEntry
    {
        public LogicalOperator Plan { get; }
        public double Cost { get; }
        public double Cardinality { get; }

        public DPEntry(LogicalOperator plan, double cost, double cardinality)
        {
            Plan = plan;
            Cost = cost;
            Cardinality = cardinality;
        }
    }

    private static bool IsJoinOperator(LogicalOperator op)
        => op.OperatorType == LogicalOperatorType.LOGICAL_HASH_JOIN
        || op.OperatorType == LogicalOperatorType.LOGICAL_CROSS_PRODUCT;

    /// <summary>
    /// Recursively extracts leaf operands from a left-deep (or bushy) join tree.
    /// </summary>
    private void ExtractJoinTree(
        LogicalOperator op,
        List<LogicalOperator> operands,
        List<JoinEdge> joinEdges)
    {
        if (!IsJoinOperator(op) || op.Children.Count != 2)
        {
            operands.Add(op);
            return;
        }

        int leftStart = operands.Count;
        ExtractJoinTree(op.GetChild(0), operands, joinEdges);
        int rightStart = operands.Count;
        ExtractJoinTree(op.GetChild(1), operands, joinEdges);

        // Record join edge between the two subtrees
        IReadOnlyList<string>? sharedVars = null;
        if (op is LogicalValueJoin valueJoin)
        {
            sharedVars = valueJoin.SharedVariables;
        }

        // Record edge between any operand in left range and any in right range
        // For simplicity, we record it between the first operand of each side
        joinEdges.Add(new JoinEdge(leftStart, rightStart, sharedVars));
    }

    /// <summary>
    /// Bottom-up DP enumeration of all possible join orderings.
    /// </summary>
    private LogicalOperator? EnumerateJoinOrders(
        List<LogicalOperator> operands,
        List<JoinEdge> joinEdges)
    {
        int n = operands.Count;
        int totalSubsets = 1 << n;

        // Build a variable-to-operand index for determining shared variables
        var operandVars = new List<HashSet<string>>();
        for (int i = 0; i < n; i++)
        {
            operandVars.Add(CollectPlanVariables(operands[i]));
        }

        // DP table indexed by bitmask
        var dpTable = new DPEntry?[totalSubsets];

        // Base case: single operands
        for (int i = 0; i < n; i++)
        {
            int mask = 1 << i;
            var card = operands[i].EstCardinality;
            dpTable[mask] = new DPEntry(operands[i], 0.0, card);
        }

        // Fill DP table for increasing subset sizes
        for (int size = 2; size <= n; size++)
        {
            for (int mask = 1; mask < totalSubsets; mask++)
            {
                if (BitCount(mask) != size)
                    continue;

                // Try all non-empty proper subsets of mask as the left side
                // Enumerate subsets of mask using the standard bit subset enumeration
                for (int leftMask = (mask - 1) & mask; leftMask > 0; leftMask = (leftMask - 1) & mask)
                {
                    int rightMask = mask & ~leftMask;
                    if (rightMask == 0 || leftMask >= rightMask)
                        continue; // Avoid duplicate pairs

                    var leftEntry = dpTable[leftMask];
                    var rightEntry = dpTable[rightMask];
                    if (leftEntry == null || rightEntry == null)
                        continue;

                    // Determine if there are shared variables between the two subsets
                    var shared = FindSharedVariables(leftMask, rightMask, operandVars, n);
                    var joinCard = shared.Count > 0
                        ? Math.Min(leftEntry.Cardinality, rightEntry.Cardinality) // Hash join
                        : leftEntry.Cardinality * rightEntry.Cardinality;          // Cross product

                    // Cout cost model: sum of all intermediate result sizes
                    var joinCost = leftEntry.Cost + rightEntry.Cost + joinCard;

                    if (dpTable[mask] == null || joinCost < dpTable[mask]!.Cost)
                    {
                        // Build the join operator
                        var joinOp = shared.Count > 0
                            ? (LogicalOperator)new LogicalValueJoin(
                                leftEntry.Plan, rightEntry.Plan, shared)
                            : new LogicalNestedLoopJoin(
                                leftEntry.Plan, rightEntry.Plan);
                        joinOp.EstCardinality = joinCard;

                        dpTable[mask] = new DPEntry(joinOp, joinCost, joinCard);
                    }
                }
            }
        }

        // The full set is mask = (1 << n) - 1
        int fullMask = totalSubsets - 1;
        return dpTable[fullMask]?.Plan;
    }

    /// <summary>
    /// Finds shared variables between two operand subsets.
    /// </summary>
    private static List<string> FindSharedVariables(
        int leftMask, int rightMask,
        List<HashSet<string>> operandVars, int n)
    {
        var leftVars = new HashSet<string>(StringComparer.Ordinal);
        var rightVars = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < n; i++)
        {
            if ((leftMask & (1 << i)) != 0)
                leftVars.UnionWith(operandVars[i]);
            if ((rightMask & (1 << i)) != 0)
                rightVars.UnionWith(operandVars[i]);
        }

        var shared = new List<string>();
        foreach (var v in leftVars)
        {
            if (rightVars.Contains(v))
                shared.Add(v);
        }

        return shared;
    }

    /// <summary>
    /// Collects all variable names referenced in a logical plan subtree.
    /// </summary>
    private static HashSet<string> CollectPlanVariables(LogicalOperator op)
    {
        var vars = new HashSet<string>(StringComparer.Ordinal);
        CollectPlanVariablesRecursive(op, vars);
        return vars;
    }

    private static void CollectPlanVariablesRecursive(LogicalOperator op, HashSet<string> vars)
    {
        switch (op)
        {
            case LogicalScanNodeProperty scan:
                if (!string.IsNullOrEmpty(scan.Node.VariableName))
                    vars.Add(scan.Node.VariableName);
                break;
            case LogicalTraverseRel traverse:
                if (!string.IsNullOrEmpty(traverse.QueryRel.VariableName))
                    vars.Add(traverse.QueryRel.VariableName);
                if (!string.IsNullOrEmpty(traverse.QueryRel.SrcNode.VariableName))
                    vars.Add(traverse.QueryRel.SrcNode.VariableName);
                if (!string.IsNullOrEmpty(traverse.QueryRel.DstNode.VariableName))
                    vars.Add(traverse.QueryRel.DstNode.VariableName);
                break;
            case LogicalIndexScanNode indexScan:
                if (!string.IsNullOrEmpty(indexScan.VariableName))
                    vars.Add(indexScan.VariableName);
                break;
            case LogicalValueJoin vj:
                if (vj.SharedVariables != null)
                    foreach (var sv in vj.SharedVariables)
                        vars.Add(sv);
                break;
        }

        foreach (var child in op.Children)
            CollectPlanVariablesRecursive(child, vars);
    }

    private static int BitCount(int x)
    {
        int count = 0;
        while (x != 0)
        {
            count += x & 1;
            x >>= 1;
        }
        return count;
    }
}
