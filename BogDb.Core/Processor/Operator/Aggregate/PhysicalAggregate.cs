using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BogDb.Core.Binder;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.Aggregate;

/// <summary>
/// Physical operator that exhausts a child plan and emits aggregate results.
/// <para>
/// <b>Global mode</b> (no group keys): emits exactly one row — the aggregate over all input rows.
/// </para>
/// <para>
/// <b>Keyed mode</b> (one or more key items): groups rows by the evaluated key tuple and emits
/// one row per distinct group, matching Cypher's implicit GROUP BY semantics.
/// </para>
/// C++ parity: simple_aggregate / hash_aggregate operators in bogdb-cpp.
/// </summary>
public sealed class PhysicalAggregate : PhysicalOperator
{
    private readonly IReadOnlyList<BoundProjectionItem> _items;
    private readonly IReadOnlyList<BoundProjectionItem> _keyItems;

    // ── State machine ──────────────────────────────────────────────────────────
    private bool _exhausted;          // child has been drained
    private List<object?[]>? _groups; // materialized output rows (keyed mode)
    private int _groupCursor;         // next group row to emit

    public PhysicalAggregate(
        IReadOnlyList<BoundProjectionItem> allItems,
        IReadOnlyList<BoundProjectionItem> keyItems,
        PhysicalOperator child,
        uint id)
        : base(PhysicalOperatorType.AGGREGATE, id)
    {
        _items    = allItems;
        _keyItems = keyItems;
        Children.Add(child);
    }

    // Backward-compatible overload for call sites that don't supply key items
    public PhysicalAggregate(
        IReadOnlyList<BoundProjectionItem> allItems,
        PhysicalOperator child,
        uint id)
        : this(allItems, Array.Empty<BoundProjectionItem>(), child, id) { }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!_exhausted)
        {
            // Drain child and build result set
            if (_keyItems.Count > 0)
                _groups = BuildKeyedGroups(context);
            else
                _groups = BuildGlobalAggregate(context);

            _exhausted = true;
            _groupCursor = 0;

            // After draining the child, individual node-scan context is no longer valid.
            // Clear it so downstream operators (e.g. PhysicalProjection) use CurrentScalarBindings
            // for property lookups rather than the stale last-scanned node's state.
            context.CurrentVariableProperties = null;
            context.CurrentNodeProperties     = null;
            context.CurrentNodeId             = null;
            context.CurrentVariableIds        = null;
        }

        if (_groupCursor >= _groups!.Count)
            return false;

        var row = _groups[_groupCursor++];
        context.CurrentProjectionRow = row;

        // Populate AggregateValues (for ORDER BY aggregate sort keys) and
        // CurrentScalarBindings (for WHERE-on-alias HAVING patterns) from each emitted row.
        if (row.Length > 0)
        {
            context.AggregateValues ??= new System.Collections.Generic.Dictionary<string, object?>();
            context.AggregateValues.Clear();
            context.CurrentScalarBindings ??= new System.Collections.Generic.Dictionary<string, object?>();
            context.CurrentScalarBindings.Clear();

            for (int i = 0; i < _items.Count; i++)
            {
                // Alias binding — enables variable lookups like `WHERE cnt > 5`
                context.CurrentScalarBindings[_items[i].ColumnName] = row[i];

                // Property name binding — enables GetPropertyValue fallback for n.region when
                // n is out of scope after aggregation (stores "region" → val, not just alias "r" → val)
                if (_items[i].Expression is PropertyExpression pe)
                    context.CurrentScalarBindings[pe.PropertyName] = row[i];

                // Aggregate key binding — enables ORDER BY count(n) via AggregateValues cache
                if (_items[i].Expression is BoundFunctionExpression bfe && bfe.IsAggregate)
                    context.AggregateValues[ExpressionExecutionHelper.GetAggKey(bfe)] = row[i];
            }
        }

        return true;
    }

    // ── Global (non-grouped) aggregation ──────────────────────────────────────

    private List<object?[]> BuildGlobalAggregate(ExecutionContext context)
    {
        var state = new GroupState(_items.Count);
        while (Children[0].GetNextTuple(context))
            AccumulateRow(context, state, _items);

        return [FinalizeRow(state, _items)];
    }

    // ── Keyed (grouped) aggregation ───────────────────────────────────────────

    private List<object?[]> BuildKeyedGroups(ExecutionContext context)
    {
        // Ordered dict so groups come out in first-seen order (stable output)
        var groups      = new Dictionary<string, GroupState>();
        var keyOrder    = new List<string>(); // insertion order
        var keyValueMap = new Dictionary<string, object?[]>(); // key → evaluated key values

        while (Children[0].GetNextTuple(context))
        {
            // Evaluate each group-key expression and build the composite key string
            var keyVals = new object?[_keyItems.Count];
            var kb = new StringBuilder();
            for (int k = 0; k < _keyItems.Count; k++)
            {
                var val = TypeCoercionHelper.Normalize(
                    ExpressionExecutionHelper.Evaluate(_keyItems[k].Expression, context));
                keyVals[k] = val;
                if (k > 0) kb.Append('\x01'); // unit-separator — unlikely in data
                kb.Append(TypeCoercionHelper.ToBogDbString(val) ?? "\x00");
            }
            var keyStr = kb.ToString();

            if (!groups.TryGetValue(keyStr, out var state))
            {
                state = new GroupState(_items.Count);
                groups[keyStr] = state;
                keyOrder.Add(keyStr);
                keyValueMap[keyStr] = keyVals;
                // Seed the key slots of the state with the key values
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!IsAggregate(_items[i].Expression, out _, out _))
                        state.LastVals[i] = keyVals[GetKeyIndex(i)];
                }
            }

            AccumulateRow(context, state, _items);
        }

        return keyOrder.Select(k => FinalizeRow(groups[k], _items)).ToList();
    }

    // ── Shared accumulation + finalization ────────────────────────────────────

    private static void AccumulateRow(
        ExecutionContext context,
        GroupState state,
        IReadOnlyList<BoundProjectionItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var expr = items[i].Expression;
            if (IsAggregate(expr, out var fname, out var argExpr))
            {
                var nLower = fname.ToLowerInvariant();

                if (nLower == "count_star" || (nLower == "count" && argExpr == null))
                {
                    // COUNT(*) — always increments
                    state.Counts[i]++;
                }
                else if (nLower == "count" && argExpr != null)
                {
                    // COUNT(expr) — count non-null values; expr may be a node var (string id)
                    var raw = ExpressionExecutionHelper.Evaluate(argExpr, context);
                    if (raw != null)
                    {
                        var norm = TypeCoercionHelper.Normalize(raw);
                        if (!IsDistinctDuplicate(state, i, expr, norm))
                            state.Counts[i]++;
                    }
                    state.Rows[i]++;
                }
                else if (nLower == "collect" && argExpr != null)
                {
                    // COLLECT(expr) — accumulate non-null values into a list
                    var raw  = ExpressionExecutionHelper.Evaluate(argExpr, context);
                    var norm = TypeCoercionHelper.Normalize(raw);
                    if (norm != null)
                    {
                        if (IsDistinctDuplicate(state, i, expr, norm))
                            continue;
                        state.CollectedLists[i] ??= new List<object?>();
                        state.CollectedLists[i]!.Add(norm);
                        state.Rows[i]++;
                    }
                }
                else if (nLower == "count_if" && argExpr != null)
                {
                    // COUNT_IF(expr) — count rows where expr is truthy
                    var raw  = ExpressionExecutionHelper.Evaluate(argExpr, context);
                    var norm = TypeCoercionHelper.Normalize(raw);
                    if (norm is bool b && b)
                        state.Counts[i]++;
                    else if (norm is long l && l != 0)
                        state.Counts[i]++;
                    state.Rows[i]++;
                }
                else if (argExpr != null)
                {
                    // Numeric aggregates: SUM / AVG / MIN / MAX
                    var raw  = ExpressionExecutionHelper.Evaluate(argExpr, context);
                    var norm = TypeCoercionHelper.Normalize(raw);
                    if (norm != null)
                    {
                        if (IsDistinctDuplicate(state, i, expr, norm))
                            continue;
                        double dval = TypeCoercionHelper.ToDouble(norm);
                        if (nLower is "sum" or "avg") state.Sums[i] += dval;
                        if (nLower == "min") state.Mins[i] = Math.Min(state.Mins[i], dval);
                        if (nLower == "max") state.Maxes[i] = Math.Max(state.Maxes[i], dval);
                        state.Rows[i]++;
                        state.LastVals[i] = norm;
                    }
                }
            }
            else
            {
                state.LastVals[i] = TypeCoercionHelper.Normalize(
                    ExpressionExecutionHelper.Evaluate(expr, context));
            }
        }
    }

    private static object?[] FinalizeRow(GroupState state, IReadOnlyList<BoundProjectionItem> items)
    {
        var row = new object?[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            if (IsAggregate(items[i].Expression, out var fname, out _))
            {
                var nLower = fname.ToLowerInvariant();
                object? result = nLower switch
                {
                    "count_star" or "count" => (object?)state.Counts[i],
                    "count_if" => (object?)state.Counts[i],
                    "sum"  => state.Rows[i] > 0 ? (object?)state.Sums[i]  : null,
                    "avg"  => state.Rows[i] > 0 ? (object?)(state.Sums[i] / state.Rows[i]) : null,
                    "min"  => state.Rows[i] > 0 ? (object?)state.Mins[i]  : null,
                    "max"  => state.Rows[i] > 0 ? (object?)state.Maxes[i] : null,
                    "collect" => (object?)(state.CollectedLists[i] ?? new List<object?>()),
                    _      => (object?)state.Counts[i]
                };
                // Promote double count to long for type consistency
                if (result is double d && d == Math.Floor(d) && nLower is "count" or "count_star" or "count_if")
                    result = (long)d;
                row[i] = result;
            }
            else
            {
                row[i] = state.LastVals[i];
            }
        }
        return row;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int GetKeyIndex(int itemIndex)
    {
        // Returns the 0-based index of itemIndex among non-aggregate items
        int ki = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            if (!IsAggregate(_items[i].Expression, out _, out _))
            {
                if (i == itemIndex) return ki;
                ki++;
            }
        }
        return 0;
    }

    private static bool IsAggregate(Expression expr, out string funcName, out Expression? argExpr)
    {
        if (expr is BoundFunctionExpression bfe && bfe.IsAggregate)
        {
            funcName = bfe.FunctionName;
            argExpr  = bfe.Arguments.Count > 0 ? bfe.Arguments[0] : null;
            return true;
        }
        funcName = string.Empty;
        argExpr  = null;
        return false;
    }

    private static bool IsDistinctDuplicate(GroupState state, int itemIndex, Expression expr, object? value)
    {
        if (expr is not BoundFunctionExpression bfe || !bfe.IsDistinct)
            return false;

        var normalized = TypeCoercionHelper.Normalize(value);
        state.DistinctSeen[itemIndex] ??= new HashSet<object?>(StructuralValueComparer.Instance);
        return !state.DistinctSeen[itemIndex]!.Add(normalized);
    }

    // ── Per-group state ───────────────────────────────────────────────────────

    private sealed class GroupState
    {
        public long[]    Counts;
        public double[]  Sums;
        public double[]  Mins;
        public double[]  Maxes;
        public long[]    Rows;
        public object?[] LastVals;
        public HashSet<object?>?[] DistinctSeen;
        public List<object?>?[] CollectedLists;

        public GroupState(int n)
        {
            Counts   = new long[n];
            Sums     = new double[n];
            Mins     = new double[n];
            Maxes    = new double[n];
            Rows     = new long[n];
            LastVals = new object?[n];
            DistinctSeen = new HashSet<object?>?[n];
            CollectedLists = new List<object?>?[n];
            for (int i = 0; i < n; i++) { Mins[i] = double.MaxValue; Maxes[i] = double.MinValue; }
        }
    }
}
