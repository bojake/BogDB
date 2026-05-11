using BogDb.Core.Binder;
using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Optimizer;

/// <summary>
/// Pushes a resolved LIMIT count downward through transparent operators
/// (Projection, Accumulate) until it reaches an operator that can exploit it
/// (RecursiveExtend, UnionAll branches).
///
/// C++ parity: limit_push_down_optimizer.cpp
///
/// Effect: MATCH (a)-[path*1..5]->(b) LIMIT 10 annotates the RecursiveExtend with
/// EarlyStopLimit=10 so the BFS/DFS halts once 10 paths have been emitted.
/// </summary>
public sealed class LimitPushDownRule : LogicalRule
{
    private const long NoLimit = -1;

    public override bool Rewrite(LogicalPlan plan)
    {
        if (plan.LastOperator == null) return false;
        var ctx = new PushContext();
        Visit(plan.LastOperator, ctx);
        return ctx.Changed;
    }

    private static void Visit(LogicalOperator op, PushContext ctx)
    {
        switch (op.OperatorType)
        {
            // ── LIMIT: evaluate constant expression and propagate ─────────────
            case LogicalOperatorType.LOGICAL_LIMIT:
                var limit = (LogicalLimit)op;
                var resolved = EvaluateLongLiteral(limit.LimitExpression);
                if (resolved.HasValue)
                {
                    var combined = resolved.Value + ctx.SkipNum;
                    ctx.LimitNum = ctx.LimitNum == NoLimit ? combined : System.Math.Min(ctx.LimitNum, combined);
                }
                if (op.GetNumChildren() > 0) Visit(op.GetChild(0), ctx);
                break;

            // ── Transparent operators: pass limit context through ─────────────
            case LogicalOperatorType.LOGICAL_PROJECTION:
            case LogicalOperatorType.LOGICAL_ACCUMULATE:
                if (op.GetNumChildren() > 0) Visit(op.GetChild(0), ctx);
                break;
            case LogicalOperatorType.LOGICAL_SKIP:
                var skip = (LogicalSkip)op;
                var resolvedSkip = EvaluateLongLiteral(skip.SkipExpression);
                if (resolvedSkip.HasValue)
                {
                    ctx.SkipNum += resolvedSkip.Value;
                    if (ctx.LimitNum != NoLimit)
                    {
                        ctx.LimitNum += resolvedSkip.Value;
                    }
                }
                if (op.GetNumChildren() > 0) Visit(op.GetChild(0), ctx);
                break;

            // ── RecursiveExtend: annotate for early BFS/DFS halt ──────────────
            case LogicalOperatorType.LOGICAL_RECURSIVE_EXTEND when ctx.LimitNum != NoLimit:
            {
                var extend = (LogicalRecursiveExtend)op;
                var target = ctx.LimitNum;
                if (extend.EarlyStopLimit == NoLimit || extend.EarlyStopLimit > target)
                {
                    extend.EarlyStopLimit = target;
                    ctx.Changed = true;
                }
                break;
            }

            // ── UnionAll: push into each branch independently ─────────────────
            case LogicalOperatorType.LOGICAL_UNION_ALL:
                for (int i = 0; i < op.GetNumChildren(); i++)
                {
                    var branchCtx = new PushContext { LimitNum = ctx.LimitNum, SkipNum = ctx.SkipNum };
                    Visit(op.GetChild(i), branchCtx);
                    ctx.Changed |= branchCtx.Changed;
                }
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Attempts to evaluate a LIMIT/SKIP expression as a constant long.
    /// Returns null for non-literal (dynamic) expressions.
    /// </summary>
    private static long? EvaluateLongLiteral(Expression? expr)
    {
        if (expr is LiteralExpression lit)
        {
            if (lit.Value is long   lv) return lv;
            if (lit.Value is int    iv) return iv;
            if (lit.Value is double dv) return (long)dv;
        }
        return null;
    }

    private sealed class PushContext
    {
        public long LimitNum { get; set; } = NoLimit;
        public long SkipNum { get; set; } = 0;
        public bool Changed  { get; set; } = false;
    }
}
