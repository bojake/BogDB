using BogDb.Core.Planner;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Optimizer;

/// <summary>
/// Walks the entire operator tree and invokes schema computation on each node.
/// In C++ BogDb this calls computeFactorizedSchema(); in BogDB, we don't have
/// factorized schema so this is a lightweight pass that resets the schema state
/// (currently no-op per operator, but the walk is useful as an extension point
/// and for future schema-aware optimizations).
///
/// C++ parity: schema_populator.cpp
/// </summary>
public sealed class SchemaPopulatorRule : LogicalRule
{
    public override bool Rewrite(LogicalPlan plan)
    {
        if (plan.LastOperator == null) return false;
        Populate(plan.LastOperator);
        // Schema population is idempotent — return false to avoid redundant loop iterations
        return false;
    }

    private static void Populate(LogicalOperator op)
    {
        // Bottom-up so schemas are available to parents
        for (int i = 0; i < op.GetNumChildren(); i++)
            Populate(op.GetChild(i));

        // In C++ this is computeFactorizedSchema(). Since BogDB uses CLR types
        // rather than factorized value vectors, schema computation here is implicit.
        // This call exists as a future extension point and documentation hook.
        op.ComputeSchema();
    }
}
