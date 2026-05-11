using BogDb.Core.Planner;

namespace BogDb.Core.Optimizer;

/// <summary>
/// C++ parity: <c>remove_factorization_rewriter.cpp</c>
///
/// In BogDb C++, this rule is the inverse of <see cref="FactorizationRewriterRule"/>:
/// it de-factorizes intermediate results when the cost model determines that
/// full materialisation is cheaper than maintaining factorized multiplicity vectors.
///
/// BogDB rows are always fully materialised, so neither factorization nor
/// de-factorization applies. Like its counterpart, this rule is a deliberate
/// architectural no-op that completes the 11/11 C++ optimizer coverage.
///
/// Effects when active: none (always returns false → no change).
/// </summary>
public sealed class RemoveFactorizationRewriterRule : LogicalRule
{
    public override bool Rewrite(LogicalPlan plan)
    {
        // BogDB does not use factorized tuples — de-factorization is N/A.
        return false;
    }
}
