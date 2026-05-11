using System;
using System.Collections.Generic;
using BogDb.Core.Binder;
using BogDb.Core.Common;
using BogDb.Core.Processor.Operator;

namespace BogDb.Core.Processor.Operator.TopK;

/// <summary>
/// Physical operator for top-K selection (ORDER BY … LIMIT N).
/// Drains the child once and maintains a fixed-size max-heap of size K
/// so that the final heap contains the K cheapest rows.
///
/// Time: O(n·log K)  Space: O(K) — significantly better than sorting all n rows.
///
/// C++ parity: result of top_k_optimizer.cpp fusion; equivalent to
///             <c>PhysicalOrderBy</c> + <c>PhysicalLimit</c> but merged.
/// </summary>
public sealed class PhysicalTopK : PhysicalOperator
{
    private readonly IReadOnlyList<Expression> _orderByExpressions;
    private readonly IReadOnlyList<bool>       _isAscending;
    private readonly Expression               _limitExpression;

    private bool   _initialized;
    private int    _currentIndex;
    private List<(object?[] keys, ExecutionState state)>? _topK;

    public PhysicalTopK(
        PhysicalOperator           child,
        IReadOnlyList<Expression>  orderByExpressions,
        IReadOnlyList<bool>        isAscending,
        Expression                 limitExpression,
        uint                       id)
        : base(PhysicalOperatorType.TOP_K, id)
    {
        Children.Add(child);
        _orderByExpressions = orderByExpressions;
        _isAscending        = isAscending;
        _limitExpression    = limitExpression;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!_initialized)
        {
            _initialized = true;
            _currentIndex = 0;

            // Evaluate the LIMIT expression to determine K.
            long k = TypeCoercionHelper.ToInt64(
                ExpressionExecutionHelper.Evaluate(_limitExpression, context));
            if (k <= 0)
            {
                _topK = new List<(object?[], ExecutionState)>();
                return false;
            }

            _topK = BuildTopK(context, (int)k);
            // Sort the heap ascending by key so we can emit in order.
            _topK.Sort(new OrderByComparer(_isAscending));
        }

        if (_topK == null || _currentIndex >= _topK.Count)
            return false;

        context.RestoreState(_topK[_currentIndex].state);
        _currentIndex++;
        return true;
    }

    /// <summary>
    /// Drains the child and maintains a max-heap of size K.
    /// Rows with "larger" keys than the heap root are discarded immediately.
    /// </summary>
    private List<(object?[] keys, ExecutionState state)> BuildTopK(
        ExecutionContext context, int k)
    {
        // Max-heap: the root (worst) element is at [0].
        // cmpMax (inverted) floats the largest (worst) element to index 0.
        // cmpFwd (forward) is used to test "is new element < heap root?" = "is new element better?"
        var heap   = new List<(object?[] keys, ExecutionState state)>(k + 1);
        var cmpMax = new OrderByComparer(_isAscending, invert: true);  // heap structure
        var cmpFwd = new OrderByComparer(_isAscending, invert: false); // replacement check

        var child = Children[0];
        while (child.GetNextTuple(context))
        {
            var keys = new object?[_orderByExpressions.Count];
            for (int i = 0; i < _orderByExpressions.Count; i++)
            {
                keys[i] = TypeCoercionHelper.Normalize(
                    ExpressionExecutionHelper.Evaluate(_orderByExpressions[i], context));
            }
            var state = context.CaptureState();

            if (heap.Count < k)
            {
                heap.Add((keys, state));
                if (heap.Count == k)
                    Heapify(heap, cmpMax); // build initial max-heap once full
            }
            else
            {
                // Replace the current worst (root) only when the new element is better.
                // "Better" = comes earlier in sort order = cmpFwd(new, root) < 0.
                if (cmpFwd.Compare((keys, state), heap[0]) < 0)
                {
                    heap[0] = (keys, state);
                    SiftDown(heap, 0, cmpMax); // restore max-heap property
                }
            }
        }

        return heap;
    }

    // ── Min-heap helpers ───────────────────────────────────────────────────────

    private static void Heapify(
        List<(object?[] keys, ExecutionState state)> h,
        IComparer<(object?[] keys, ExecutionState state)> cmp)
    {
        for (int i = h.Count / 2 - 1; i >= 0; i--)
            SiftDown(h, i, cmp);
    }

    private static void SiftDown(
        List<(object?[] keys, ExecutionState state)> h,
        int i,
        IComparer<(object?[] keys, ExecutionState state)> cmp)
    {
        int n = h.Count;
        while (true)
        {
            int largest = i;
            int l = 2 * i + 1, r = 2 * i + 2;
            if (l < n && cmp.Compare(h[l], h[largest]) < 0) largest = l;
            if (r < n && cmp.Compare(h[r], h[largest]) < 0) largest = r;
            if (largest == i) break;
            (h[i], h[largest]) = (h[largest], h[i]);
            i = largest;
        }
    }

    // ── Comparers ─────────────────────────────────────────────────────────────

    private sealed class OrderByComparer
        : IComparer<(object?[] keys, ExecutionState state)>
    {
        private readonly int _sign; // +1 = ascending semantics, -1 = max-heap (inverted)
        private readonly SortKeyComparer _keyComparer;

        internal OrderByComparer(IReadOnlyList<bool> asc, bool invert = false)
        {
            _sign = invert ? -1 : 1;
            _keyComparer = new SortKeyComparer(asc);
        }

        public int Compare(
            (object?[] keys, ExecutionState state) x,
            (object?[] keys, ExecutionState state) y)
        {
            return _sign * _keyComparer.Compare(x.keys, y.keys);
        }
    }
}
