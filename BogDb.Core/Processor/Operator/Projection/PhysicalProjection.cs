using System.Collections.Generic;
using BogDb.Core.Binder;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.Projection;

public sealed class PhysicalProjection : PhysicalOperator
{
    private readonly IReadOnlyList<BoundProjectionItem> _projectionItems;

    public PhysicalProjection(IReadOnlyList<BoundProjectionItem> projectionItems, PhysicalOperator child, uint id)
        : base(PhysicalOperatorType.PROJECTION, child, id)
    {
        _projectionItems = projectionItems;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!Children[0].GetNextTuple(context))
        {
            return false;
        }

        var values = new object?[_projectionItems.Count];
        context.CurrentScalarBindings ??= new Dictionary<string, object?>(System.StringComparer.Ordinal);
        for (var i = 0; i < _projectionItems.Count; i++)
        {
            // Normalize each value to canonical BogDb CLR types so BogRow.GetInt64(),
            // GetString() etc. always receive safe, directly-castable values.
            values[i] = TypeCoercionHelper.Normalize(
                ExpressionExecutionHelper.Evaluate(_projectionItems[i].Expression, context));
            context.CurrentScalarBindings[_projectionItems[i].ColumnName] = values[i];
        }

        context.CurrentProjectionRow = values;
        return true;
    }
}
