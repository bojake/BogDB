using System;
using System.Collections.Generic;
using BogDb.Core.Binder;
using BogDb.Core.Common;
using BogDb.Core.Processor.Operator;

namespace BogDb.Core.Processor.Operator.Scan;

/// <summary>
/// Physical operator that unwinds a list expression into individual row tuples.
/// Each call to GetNextTuple() exposes the next element via context.CurrentScalarBindings.
/// C++ parity: UNWIND physical operator in bogdb-cpp.
/// </summary>
public sealed class PhysicalUnwind : PhysicalOperator
{
    private readonly Expression _collectionExpression;
    private readonly string _alias;

    private List<object?>? _list;
    private int _index;
    private bool _initialized;

    public PhysicalUnwind(Expression collectionExpression, string alias, PhysicalOperator? child, uint id)
        : base(PhysicalOperatorType.UNWIND, id)
    {
        _collectionExpression = collectionExpression;
        _alias = alias;
        // child operator is used when UNWIND appears after a MATCH source;
        // for bare [list] unwinding, child is null — we are our own source
        if (child != null) Children.Add(child);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!_initialized)
        {
            // Evaluate the collection expression once to get the list
            var val = ExpressionExecutionHelper.Evaluate(_collectionExpression, context);
            _list = ExtractList(val);
            _index = 0;
            _initialized = true;
        }

        if (_list == null || _index >= _list.Count)
            return false;

        // Expose the current element as a scalar binding for the alias variable
        context.CurrentScalarBindings ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        context.CurrentScalarBindings[_alias] = TypeCoercionHelper.Normalize(_list[_index]);
        _index++;
        return true;
    }

    private static List<object?> ExtractList(object? val)
    {
        if (val is List<object?> genericList)
            return genericList;
        if (val is System.Collections.IEnumerable enumerable and not string)
        {
            var result = new List<object?>();
            foreach (var item in enumerable)
                result.Add(item);
            return result;
        }
        // Scalar → wrap as single-element list
        if (val != null)
            return new List<object?> { val };
        return new List<object?>();
    }
}
