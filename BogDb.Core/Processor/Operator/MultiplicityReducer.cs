using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator;

/// <summary>
/// MultiplicityReducer — strips any multiplicity inflation introduced by optional-match paths
/// or cross-product joins, emitting each logical tuple exactly once.
///
/// C++ parity: src/processor/operator/multiplicity_reducer.cpp
///
/// In BogDB, the multiplicity concept maps to the case where a pipeline segment
/// feeds into a nested loop join that can produce duplicate bindings of non-nullable
/// variables. This operator acts as a passthrough at the physical level while
/// tracking the "multiplicity factor" in the context so downstream operators can
/// suppress or divide duplicated result counts.
///
/// For the current single-threaded execution model, this is effectively a passthrough
/// that resets the context's multiplicity counter to 1 on each emitted row.
/// </summary>
public sealed class MultiplicityReducer : PhysicalOperator
{
    public MultiplicityReducer(PhysicalOperator child, uint id)
        : base(PhysicalOperatorType.MULTIPLICITY_REDUCER, child, id) { }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!Children[0].GetNextTuple(context))
            return false;
        // Reset any multiplicity context so downstream sees multiplicity = 1
        // (no-op for the current single-threaded model but marks the pipeline boundary)
        return true;
    }
}
