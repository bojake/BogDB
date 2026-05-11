namespace BogDb.Core.Processor.Operator.UnionAll;

/// <summary>
/// Implements UNION ALL: exhausts the left child pipeline first, then exhausts
/// the right child pipeline. Both sides must produce the same column shape.
/// C++ parity: union_all_scan.h
/// </summary>
public sealed class PhysicalUnionAll : PhysicalOperator
{
    private bool _leftExhausted;

    public PhysicalUnionAll(PhysicalOperator left, PhysicalOperator right, uint id)
        : base(PhysicalOperatorType.UNION_ALL_SCAN, id)
    {
        Children.Add(left);
        Children.Add(right);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        // Drain left side first
        if (!_leftExhausted)
        {
            if (Children[0].GetNextTuple(context))
                return true;
            _leftExhausted = true;
        }

        // Then drain right side
        return Children[1].GetNextTuple(context);
    }
}
