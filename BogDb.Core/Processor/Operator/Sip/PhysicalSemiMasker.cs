using System;
using System.Collections;
using System.Collections.Generic;
using BogDb.Core.Binder;
using BogDb.Core.Main;
using BogDb.Core.Planner.Operator.Sip;

namespace BogDb.Core.Processor.Operator.Sip;

/// <summary>
/// Simplified semi-masker: collects keys into ExecutionContext.SemiMasks while
/// streaming child tuples unchanged.
/// </summary>
public sealed class PhysicalSemiMasker : PhysicalOperator
{
    private readonly SemiMaskKeyType _keyType;
    private readonly SemiMaskTargetType _targetType;
    private readonly Expression _keyExpression;
    private readonly IReadOnlyList<ulong> _nodeTableIds;
    private readonly ExtraKeyInfo? _extraKeyInfo;
    private readonly PhysicalOperator _child;

    public PhysicalSemiMasker(
        SemiMaskKeyType keyType,
        SemiMaskTargetType targetType,
        Expression keyExpression,
        IReadOnlyList<ulong> nodeTableIds,
        PhysicalOperator child,
        ExtraKeyInfo? extraKeyInfo,
        uint id)
        : base(PhysicalOperatorType.SEMI_MASKER, id)
    {
        _keyType = keyType;
        _targetType = targetType;
        _keyExpression = keyExpression;
        _nodeTableIds = nodeTableIds;
        _extraKeyInfo = extraKeyInfo;
        _child = child;
        Children.Add(child);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!_child.GetNextTuple(context))
            return false;

        var keyValue = ExpressionExecutionHelper.Evaluate(_keyExpression, context);
        AddKeyToMasks(context, keyValue);

        if (_keyType == SemiMaskKeyType.NODE_ID_LIST && _extraKeyInfo is ExtraNodeIdListKeyInfo idListInfo)
        {
            var src = ExpressionExecutionHelper.Evaluate(idListInfo.SrcNodeId, context);
            var dst = ExpressionExecutionHelper.Evaluate(idListInfo.DstNodeId, context);
            AddKeyToMasks(context, src);
            AddKeyToMasks(context, dst);
        }
        return true;
    }

    private void AddKeyToMasks(ExecutionContext context, object? value)
    {
        if (value == null) return;

        if (context.SemiMasks == null)
            context.SemiMasks = new Dictionary<ulong, HashSet<object>>();

        IEnumerable valuesToAdd = value is string
            ? new object[] { value }
            : value is IEnumerable enumerable
                ? enumerable
                : new object[] { value };

        foreach (var tableId in _nodeTableIds)
        {
            if (!context.SemiMasks.TryGetValue(tableId, out var set))
            {
                set = new HashSet<object>();
                context.SemiMasks[tableId] = set;
            }

            foreach (var entry in valuesToAdd)
            {
                if (entry == null)
                    continue;

                if (entry is EdgeKey edge)
                {
                    set.Add(edge.From);
                    set.Add(edge.To);
                    continue;
                }

                set.Add(entry);
            }
        }
    }
}
