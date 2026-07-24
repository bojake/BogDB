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
    private List<AggregateRow>? _groups; // materialized output rows (keyed mode)
    private int _groupCursor;         // next group row to emit

    // One emitted group: its projection row plus the variable bindings for any GROUP-KEY that is a node
    // or relationship variable (e.g. `a` in `WITH a, count(*)`). Those variables stay in scope after the
    // aggregation and must be restored so a following clause — a MATCH (a)-[:R]->(b), a RETURN a.name —
    // can still resolve them. A global aggregate (no keys) carries nothing.
    private readonly record struct AggregateRow(
        object?[] Row,
        Dictionary<string, object>? CarriedIds,
        Dictionary<string, Dictionary<string, object>>? CarriedProps);

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
        }

        if (_groupCursor >= _groups!.Count)
            return false;

        var group = _groups[_groupCursor++];
        var row = group.Row;
        context.CurrentProjectionRow = row;

        // Re-establish this group's scope. The last-scanned node's transient state is stale after a full
        // drain, but the group-key node/rel variables remain in scope and are restored here so a following
        // clause can resolve them; anything else (including a global aggregate) resets to null so downstream
        // operators fall back to CurrentScalarBindings rather than a stale last-scanned node.
        context.CurrentNodeProperties = null;
        context.CurrentNodeId         = null;
        context.CurrentVariableIds        = group.CarriedIds;
        context.CurrentVariableProperties = group.CarriedProps;

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

    private List<AggregateRow> BuildGlobalAggregate(ExecutionContext context)
    {
        var state = new GroupState(_items.Count);
        while (Children[0].GetNextTuple(context))
            AccumulateRow(context, state, _items);

        // A global aggregate has no group keys, so nothing stays bound.
        return [new AggregateRow(FinalizeRow(state, _items), null, null)];
    }

    // ── Keyed (grouped) aggregation ───────────────────────────────────────────

    private List<AggregateRow> BuildKeyedGroups(ExecutionContext context)
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
                // Capture the node/rel variable bindings for this group's keys from the first row that
                // opened it, so they can be restored when the group is emitted.
                CaptureCarriedBindings(context, state);
            }

            AccumulateRow(context, state, _items);
        }

        return keyOrder
            .Select(k => new AggregateRow(FinalizeRow(groups[k], _items), groups[k].CarriedIds, groups[k].CarriedProps))
            .ToList();
    }

    // Records, for each group-key that is a node or relationship variable, its id and properties as bound
    // on the row that first opened the group. Scalar keys (e.g. `WITH n.region`) live in scalar bindings,
    // not CurrentVariableIds, so they are naturally skipped here.
    private void CaptureCarriedBindings(ExecutionContext context, GroupState state)
    {
        foreach (var item in _keyItems)
        {
            if (item.Expression is not VariableExpression ve || string.IsNullOrEmpty(ve.VariableName))
                continue;

            var name = ve.VariableName;
            if (context.CurrentVariableIds != null && context.CurrentVariableIds.TryGetValue(name, out var id))
                (state.CarriedIds ??= new Dictionary<string, object>())[name] = id;
            if (context.CurrentVariableProperties != null &&
                context.CurrentVariableProperties.TryGetValue(name, out var props))
                (state.CarriedProps ??= new Dictionary<string, Dictionary<string, object>>())[name] = props;
        }
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
                    // SUM / AVG / MIN / MAX
                    var raw  = ExpressionExecutionHelper.Evaluate(argExpr, context);
                    var norm = TypeCoercionHelper.Normalize(raw);
                    if (norm != null)
                    {
                        if (IsDistinctDuplicate(state, i, expr, norm))
                            continue;

                        if (nLower == "min")
                        {
                            // MIN/MAX are defined for any orderable type (strings, temporals, numbers) —
                            // use the same total order ORDER BY uses instead of forcing the value through
                            // ToDouble, which threw a FormatException on a STRING or temporal column.
                            if (state.MinVals[i] is null ||
                                StructuralValueOrderComparer.CompareValues(norm, state.MinVals[i]) < 0)
                                state.MinVals[i] = norm;
                        }
                        else if (nLower == "max")
                        {
                            if (state.MaxVals[i] is null ||
                                StructuralValueOrderComparer.CompareValues(norm, state.MaxVals[i]) > 0)
                                state.MaxVals[i] = norm;
                        }
                        else // sum / avg — numeric only
                        {
                            if (!TryToNumeric(norm, out var dval))
                                throw new InvalidOperationException(
                                    $"{nLower}() requires numeric values but received '{norm}' of type {norm.GetType().Name}.");
                            state.Sums[i] += dval;
                        }
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
                    "min"  => state.MinVals[i],
                    "max"  => state.MaxVals[i],
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

    // Non-throwing numeric coercion for SUM/AVG. Mirrors TypeCoercionHelper.ToDouble (including numeric
    // strings) but returns false instead of throwing, so a non-numeric value yields a clean aggregate
    // error rather than an unhandled FormatException.
    private static bool TryToNumeric(object value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case float f: result = f; return true;
            case long l: result = l; return true;
            case int i: result = i; return true;
            case short s: result = s; return true;
            case byte b: result = b; return true;
            case sbyte sb: result = sb; return true;
            case ushort us: result = us; return true;
            case uint ui: result = ui; return true;
            case ulong ul: result = ul; return true;
            case decimal dec: result = (double)dec; return true;
            case bool bl: result = bl ? 1.0 : 0.0; return true;
            case string str when double.TryParse(str, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                result = parsed; return true;
            default: result = 0; return false;
        }
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
        // MIN/MAX hold the winning VALUE (any orderable type), not a running double, so they work for
        // strings and temporals and preserve the original type on output.
        public object?[] MinVals;
        public object?[] MaxVals;
        public long[]    Rows;
        public object?[] LastVals;
        public HashSet<object?>?[] DistinctSeen;
        public List<object?>?[] CollectedLists;

        // Node/rel variable bindings for this group's keys, restored when the group is emitted.
        public Dictionary<string, object>? CarriedIds;
        public Dictionary<string, Dictionary<string, object>>? CarriedProps;

        public GroupState(int n)
        {
            Counts   = new long[n];
            Sums     = new double[n];
            MinVals  = new object?[n];
            MaxVals  = new object?[n];
            Rows     = new long[n];
            LastVals = new object?[n];
            DistinctSeen = new HashSet<object?>?[n];
            CollectedLists = new List<object?>?[n];
        }
    }
}
