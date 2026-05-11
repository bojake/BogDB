using System;
using System.Collections;
using System.Linq;
using BogDb.Core.Binder;
using BogDb.Core.Common;
using BogDb.Core.Planner;


namespace BogDb.Core.Processor.Operator;

internal static class ExpressionExecutionHelper
{
    public static object? Evaluate(Expression expression, ExecutionContext context)
    {
        return expression switch
        {
            LiteralExpression literal => literal.Value,
            PropertyExpression property => GetPropertyValue(property, context),
            VariableExpression variable => GetVariableValue(variable, context),
            BoundParameterExpression parameter => GetParameterValue(parameter, context),
            BoundComparisonExpression comparison => EvaluateComparison(comparison, context),
            BoundBooleanExpression booleanExpression => EvaluateBoolean(booleanExpression, context),
            BoundQuantifierExpression quantifier => EvaluateQuantifier(quantifier, context),
            BoundSubqueryExpression subquery => EvaluateSubquery(subquery, context),
            BoundFunctionExpression func => EvaluateFunction(func, context),
            // Graceful fallback for any other bound expression types
            _ => null
        };
    }

    public static bool EvaluatePredicate(Expression expression, ExecutionContext context)
    {
        var value = Evaluate(expression, context);
        return value is bool boolValue && boolValue;
    }

    private static object? GetPropertyValue(PropertyExpression property, ExecutionContext context)
    {
        if (context.CurrentVariableProperties != null &&
            context.CurrentVariableProperties.TryGetValue(property.NodeVariableName, out var properties))
        {
            return properties.TryGetValue(property.PropertyName, out var mappedValue)
                ? TypeCoercionHelper.Normalize(mappedValue)
                : null;
        }

        // Fallback: after a WITH aggregation the node variable goes out of scope; the property
        // value was captured in CurrentScalarBindings by PhysicalAggregate (e.g. "dept" → "Eng").
        if (context.CurrentScalarBindings != null &&
            context.CurrentScalarBindings.TryGetValue(property.PropertyName, out var bound))
            return TypeCoercionHelper.Normalize(bound);

        if (context.CurrentNodeProperties == null)
        {
            return null;
        }

        if (context.CurrentVariableIds != null &&
            (!context.CurrentVariableIds.TryGetValue(property.NodeVariableName, out _) ||
             context.CurrentVariableIds.Count != 1))
        {
            return null;
        }

        return context.CurrentNodeProperties.TryGetValue(property.PropertyName, out var value)
            ? TypeCoercionHelper.Normalize(value)
            : null;
    }

    private static bool EvaluateComparison(BoundComparisonExpression comparison, ExecutionContext context)
    {
        var left = Evaluate(comparison.Left, context);
        var right = Evaluate(comparison.Right, context);
        if (comparison.ExpressionType is ExpressionType.EQUALS or ExpressionType.NOT_EQUALS)
        {
            var equals = AreStructurallyEqual(left, right);
            return comparison.ExpressionType == ExpressionType.EQUALS ? equals : !equals;
        }

        var compareResult = CompareValues(left, right);

        return comparison.ExpressionType switch
        {
            ExpressionType.GREATER_THAN => compareResult > 0,
            ExpressionType.GREATER_THAN_EQUALS => compareResult >= 0,
            ExpressionType.LESS_THAN => compareResult < 0,
            ExpressionType.LESS_THAN_EQUALS => compareResult <= 0,
            _ => throw new NotSupportedException($"Comparison {comparison.ExpressionType} is not supported.")
        };
    }

    private static bool EvaluateBoolean(BoundBooleanExpression booleanExpression, ExecutionContext context)
    {
        return booleanExpression.ExpressionType switch
        {
            ExpressionType.NOT => !EvaluatePredicate(booleanExpression.Left, context),
            ExpressionType.AND => EvaluatePredicate(booleanExpression.Left, context) &&
                EvaluatePredicate(booleanExpression.Right!, context),
            ExpressionType.OR => EvaluatePredicate(booleanExpression.Left, context) ||
                EvaluatePredicate(booleanExpression.Right!, context),
            ExpressionType.XOR => EvaluatePredicate(booleanExpression.Left, context) ^
                EvaluatePredicate(booleanExpression.Right!, context),
            ExpressionType.IS_NULL => Evaluate(booleanExpression.Left, context) == null,
            ExpressionType.IS_NOT_NULL => Evaluate(booleanExpression.Left, context) != null,
            _ => throw new NotSupportedException($"Boolean expression {booleanExpression.ExpressionType} is not supported.")
        };
    }

    private static bool EvaluateQuantifier(BoundQuantifierExpression quantifier, ExecutionContext context)
    {
        var collection = Evaluate(quantifier.CollectionExpression, context);
        if (collection == null)
            return quantifier.QuantifierName is "ALL" or "NONE";

        if (collection is string || collection is not IEnumerable enumerable)
            return false;

        context.CurrentScalarBindings ??= new System.Collections.Generic.Dictionary<string, object?>(StringComparer.Ordinal);
        var hadPriorValue = context.CurrentScalarBindings.TryGetValue(quantifier.VariableName, out var priorValue);

        try
        {
            var sawAny = false;
            var matchCount = 0;
            foreach (var item in enumerable)
            {
                sawAny = true;
                context.CurrentScalarBindings[quantifier.VariableName] = TypeCoercionHelper.Normalize(item);
                var matches = EvaluatePredicate(quantifier.PredicateExpression, context);

                switch (quantifier.QuantifierName)
                {
                    case "ALL":
                        if (!matches)
                            return false;
                        break;
                    case "ANY":
                        if (matches)
                            return true;
                        break;
                    case "NONE":
                        if (matches)
                            return false;
                        break;
                    case "SINGLE":
                        if (matches)
                        {
                            matchCount++;
                            if (matchCount > 1)
                                return false;
                        }
                        break;
                    default:
                        throw new NotSupportedException($"Quantifier {quantifier.QuantifierName} is not supported.");
                }
            }

            return quantifier.QuantifierName switch
            {
                "ALL" => true,
                "ANY" => false,
                "NONE" => true,
                "SINGLE" => sawAny && matchCount == 1,
                _ => throw new NotSupportedException($"Quantifier {quantifier.QuantifierName} is not supported.")
            };
        }
        finally
        {
            if (hadPriorValue)
                context.CurrentScalarBindings[quantifier.VariableName] = priorValue;
            else
                context.CurrentScalarBindings.Remove(quantifier.VariableName);
        }
    }

    private static object? EvaluateSubquery(BoundSubqueryExpression subquery, ExecutionContext context)
    {
        if (context.Database == null)
            throw new InvalidOperationException("Subquery evaluation requires a database-bound execution context.");

        var preBoundVariables = context.CurrentVariableIds == null
            ? null
            : new System.Collections.Generic.HashSet<string>(
                context.CurrentVariableIds.Keys,
                System.StringComparer.Ordinal);

        var planner = new BogDb.Core.Planner.Planner();
        var logicalPlan = planner.GetBestPlan(subquery.BoundQuery, preBoundVariables);
        var optimizer = new Optimizer.Optimizer(context.Database);
        optimizer.Optimize(logicalPlan);
        var mapper = new PlanMapper(context.Database);
        var physicalPlan = mapper.MapLogicalPlanToPhysical(logicalPlan);

        var childContext = new ExecutionContext(context.Transaction, context.BufferManager, context.Database)
        {
            QueryMetrics = context.QueryMetrics
        };
        var captured = context.CaptureState();
        childContext.RestoreState(captured);
        childContext.ParameterBindings = context.ParameterBindings == null
            ? null
            : new System.Collections.Generic.Dictionary<string, object?>(context.ParameterBindings);

        long rowCount = 0;
        while (physicalPlan.LastOperator.GetNextTuple(childContext))
        {
            rowCount++;
            if (subquery.SubqueryType == BogDb.Core.Parser.SubqueryType.EXISTS)
                break;
        }

        return subquery.SubqueryType == BogDb.Core.Parser.SubqueryType.EXISTS
            ? (object?)(rowCount > 0)
            : rowCount;
    }

    // ── Function expression evaluation ────────────────────────────────────────

    private static object? EvaluateFunction(BoundFunctionExpression func, ExecutionContext context)
    {
        var name = func.FunctionName.ToLowerInvariant();

        // ── Context-aware cases: evaluated before dispatching ──────────────────
        // Aggregate stubs: PhysicalAggregate handles the real accumulation;
        // these stubs just pass through a per-row value for it to collect.
        if (func.IsAggregate)
        {
            // If PhysicalAggregate has already computed this value (e.g. during ORDER BY post-aggregation),
            // return the cached value. Key = "funcname(arg0,arg1,...)" for cross-instance matching.
            if (context.AggregateValues != null)
            {
                var aggKey = GetAggKey(func);
                if (context.AggregateValues.TryGetValue(aggKey, out var cached))
                    return cached;
            }

            var aggArgs = func.Arguments.Select(a => TypeCoercionHelper.Normalize(Evaluate(a, context))).ToArray();
            return name switch
            {
                "count" or "count_star" or "count(*)" => 1L,
                "count_if" => (aggArgs.Length >= 1 && aggArgs[0] is bool b && b) ? 1L : 0L,
                "sum" or "avg" or "min" or "max"      => aggArgs.Length >= 1 ? aggArgs[0] : null,
                "collect"                              => aggArgs.Length >= 1 ? aggArgs[0] : null,
                _                                     => null
            };
        }

        // list_literal: returns a List<object?> for UNWIND / list functions to iterate
        if (name == "list_literal")
        {
            var items = func.Arguments.Select(a => TypeCoercionHelper.Normalize(Evaluate(a, context))).ToArray();
            return new System.Collections.Generic.List<object?>(items);
        }

        if (name == "struct_literal")
        {
            var dict = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < func.Arguments.Count; i += 2)
            {
                var key = Evaluate(func.Arguments[i], context)?.ToString() ?? string.Empty;
                dict[key] = TypeCoercionHelper.Normalize(Evaluate(func.Arguments[i + 1], context));
            }
            return dict;
        }

        if (name is "case" or "case_simple")
            return EvaluateCaseFunction(func, context, name == "case_simple");

        // ── Lambda-backed list functions ──────────────────────────────────────
        // list_filter(list, lambda), list_reduce(list, lambda), list_transform(list, lambda)
        if ((name is "list_filter" or "list_reduce" or "list_transform" or "list_apply") &&
            func.Arguments.Count >= 2 &&
            func.Arguments[1] is BoundLambdaExpression)
        {
            return EvaluateLambdaListFunction(name, func, context);
        }

        // ── Eager argument evaluation for all remaining functions ──────────────
        var args = func.Arguments.Select(a => TypeCoercionHelper.Normalize(Evaluate(a, context))).ToArray();

        // ── Delegate to the central function registry ──────────────────────────
        if (context.Database?.ScalarFunctionRegistry.TryGetContextAware(name, out var contextAwareScalar) == true)
            return contextAwareScalar(func, args, context);

        if (context.Database?.ScalarFunctionRegistry.TryGet(name, out var extensionScalar) == true)
            return extensionScalar(args);

        return BogDb.Core.Function.FunctionDispatcher.Invoke(name, args);
    }

    /// <summary>
    /// Evaluates a lambda-backed list function (list_filter, list_reduce, list_transform)
    /// by iterating the collection and evaluating the lambda body with bound parameters.
    /// </summary>
    private static object? EvaluateLambdaListFunction(
        string name, BoundFunctionExpression func, ExecutionContext context)
    {
        var collection = TypeCoercionHelper.Normalize(Evaluate(func.Arguments[0], context));
        var lambda = (BoundLambdaExpression)func.Arguments[1];

        if (collection == null) return null;
        if (collection is string || collection is not IEnumerable enumerable) return null;

        var list = enumerable.Cast<object?>().ToList();

        context.CurrentScalarBindings ??= new System.Collections.Generic.Dictionary<string, object?>(StringComparer.Ordinal);

        // Save prior bindings for lambda parameters
        var priors = new List<(string name, object? value, bool had)>();
        foreach (var paramName in lambda.ParameterNames)
        {
            var had = context.CurrentScalarBindings.TryGetValue(paramName, out var prior);
            priors.Add((paramName, prior, had));
        }

        try
        {
            return name switch
            {
                "list_filter" => EvalListFilter(list, lambda, context),
                "list_reduce" => EvalListReduce(list, lambda, context),
                "list_transform" or "list_apply" => EvalListTransform(list, lambda, context),
                _ => null
            };
        }
        finally
        {
            // Restore prior bindings
            foreach (var (pName, pValue, pHad) in priors)
            {
                if (pHad) context.CurrentScalarBindings[pName] = pValue;
                else context.CurrentScalarBindings.Remove(pName);
            }
        }
    }

    private static object? EvalListFilter(
        List<object?> list, BoundLambdaExpression lambda, ExecutionContext context)
    {
        var result = new System.Collections.Generic.List<object?>();
        var paramName = lambda.ParameterNames[0];
        foreach (var item in list)
        {
            context.CurrentScalarBindings![paramName] = TypeCoercionHelper.Normalize(item);
            var matches = EvaluatePredicate(lambda.Body, context);
            if (matches) result.Add(item);
        }
        return result;
    }

    private static object? EvalListTransform(
        List<object?> list, BoundLambdaExpression lambda, ExecutionContext context)
    {
        var result = new System.Collections.Generic.List<object?>();
        var paramName = lambda.ParameterNames[0];
        foreach (var item in list)
        {
            context.CurrentScalarBindings![paramName] = TypeCoercionHelper.Normalize(item);
            result.Add(Evaluate(lambda.Body, context));
        }
        return result;
    }

    private static object? EvalListReduce(
        List<object?> list, BoundLambdaExpression lambda, ExecutionContext context)
    {
        if (list.Count == 0) return null;
        if (list.Count == 1) return TypeCoercionHelper.Normalize(list[0]);

        var accumParam = lambda.ParameterNames[0];
        var elementParam = lambda.ParameterNames.Count >= 2 ? lambda.ParameterNames[1] : accumParam;

        var accumulator = TypeCoercionHelper.Normalize(list[0]);
        for (var i = 1; i < list.Count; i++)
        {
            context.CurrentScalarBindings![accumParam] = accumulator;
            context.CurrentScalarBindings[elementParam] = TypeCoercionHelper.Normalize(list[i]);
            accumulator = Evaluate(lambda.Body, context);
        }
        return accumulator;
    }

    // ── Arithmetic helpers ────────────────────────────────────────────────────

    private static object? EvaluateArithmetic(object?[] args,
        Func<long, long, long> intOp, Func<double, double, double> dblOp)
    {
        if (args.Length < 2) return null;
        var l = args[0];
        var r = args[1];
        // All-integer path preserves INT64 result
        if (l is long li && r is long ri) return intOp(li, ri);
        // Mixed or double path
        return dblOp(TypeCoercionHelper.ToDouble(l), TypeCoercionHelper.ToDouble(r));
    }

    private static object? EvaluateUnaryNegate(object? arg)
    {
        return arg switch
        {
            long l => -l,
            double d => -d,
            BogDbInterval interval => new BogDbInterval(-interval.Months, -interval.Days, -interval.Microseconds),
            _ => null
        };
    }

    private static object? EvaluateUnaryNumeric(object?[] args,
        Func<long, long> intOp, Func<double, double> dblOp)
    {
        if (args.Length < 1) return null;
        return args[0] switch
        {
            long l => (object?)intOp(l),
            _ => (object?)dblOp(TypeCoercionHelper.ToDouble(args[0]))
        };
    }

    private static object? EvaluateCaseFunction(
        BoundFunctionExpression func,
        ExecutionContext context,
        bool isSimpleCase)
    {
        var argIndex = 0;
        object? baseValue = null;
        if (isSimpleCase)
            baseValue = TypeCoercionHelper.Normalize(Evaluate(func.Arguments[argIndex++], context));

        while (argIndex + 1 < func.Arguments.Count)
        {
            var whenValue = TypeCoercionHelper.Normalize(Evaluate(func.Arguments[argIndex], context));
            var thenValue = TypeCoercionHelper.Normalize(Evaluate(func.Arguments[argIndex + 1], context));
            argIndex += 2;

            var matches = isSimpleCase
                ? AreStructurallyEqual(baseValue, whenValue)
                : whenValue is bool predicate && predicate;
            if (matches)
                return thenValue;
        }

        return argIndex < func.Arguments.Count
            ? TypeCoercionHelper.Normalize(Evaluate(func.Arguments[argIndex], context))
            : null;
    }

    // ── String helpers ────────────────────────────────────────────────────────

    private static bool? EvaluateStringPredicate(object?[] args, Func<string, string, bool> predicate)
    {
        if (args.Length < 2) return null;
        var s = TypeCoercionHelper.ToBogDbString(args[0]);
        var pat = TypeCoercionHelper.ToBogDbString(args[1]);
        return s != null && pat != null ? predicate(s, pat) : (bool?)null;
    }

    private static object? EvaluateSubstring(object?[] args)
    {
        var s = TypeCoercionHelper.ToBogDbString(args[0]);
        if (s == null) return null;
        var start = (int)TypeCoercionHelper.ToInt64(args[1]);
        if (start < 0) start = Math.Max(0, s.Length + start);
        if (start >= s.Length) return "";
        if (args.Length >= 3)
        {
            var len = (int)TypeCoercionHelper.ToInt64(args[2]);
            return s.Substring(start, Math.Min(len, s.Length - start));
        }
        return s.Substring(start);
    }

    private static object? EvaluateStringRight(object?[] args)
    {
        var s = TypeCoercionHelper.ToBogDbString(args[0]);
        if (s == null) return null;
        var n = (int)TypeCoercionHelper.ToInt64(args[1]);
        return n >= s.Length ? s : s.Substring(s.Length - n);
    }

    private static object? TryToInt64(object? v)
    {
        try { return TypeCoercionHelper.ToInt64(v); }
        catch { return null; }
    }

    // ── Comparison helpers ────────────────────────────────────────────────────

    private static bool AreStructurallyEqual(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        left = TypeCoercionHelper.Normalize(left);
        right = TypeCoercionHelper.Normalize(right);

        if (TryGetDictionaryEntries(left, out var leftEntries) &&
            TryGetDictionaryEntries(right, out var rightEntries))
        {
            return DictionaryEntriesEqual(leftEntries, rightEntries);
        }

        if (left is not string && right is not string &&
            left is System.Collections.IEnumerable leftEnumerable &&
            right is System.Collections.IEnumerable rightEnumerable)
        {
            return EnumerablesEqual(leftEnumerable, rightEnumerable);
        }

        try
        {
            return CompareValues(left, right) == 0;
        }
        catch (InvalidOperationException)
        {
            return Equals(left, right);
        }
    }

    private static int CompareValues(object? left, object? right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        // Normalize both sides so JsonNode/int/float become canonical BogDb types
        left  = TypeCoercionHelper.Normalize(left);
        right = TypeCoercionHelper.Normalize(right);

        if (left.GetType() == right.GetType() && left is IComparable comparable)
        {
            return comparable.CompareTo(right);
        }

        if (left is IConvertible && right is IConvertible)
        {
            var leftDouble = Convert.ToDouble(left);
            var rightDouble = Convert.ToDouble(right);
            return leftDouble.CompareTo(rightDouble);
        }

        throw new InvalidOperationException($"Cannot compare values '{left}' and '{right}'.");
    }

    private static bool EnumerablesEqual(System.Collections.IEnumerable left, System.Collections.IEnumerable right)
    {
        var leftEnumerator = left.GetEnumerator();
        var rightEnumerator = right.GetEnumerator();

        while (true)
        {
            var leftHasNext = leftEnumerator.MoveNext();
            var rightHasNext = rightEnumerator.MoveNext();
            if (leftHasNext != rightHasNext)
                return false;
            if (!leftHasNext)
                return true;
            if (!AreStructurallyEqual(leftEnumerator.Current, rightEnumerator.Current))
                return false;
        }
    }

    private static bool TryGetDictionaryEntries(object value, out List<KeyValuePair<string, object?>> entries)
    {
        entries = new List<KeyValuePair<string, object?>>();

        switch (value)
        {
            case IDictionary<string, object?> typedDictionary:
                foreach (var entry in typedDictionary)
                    entries.Add(entry);
                return true;
            case System.Collections.IDictionary dictionary:
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                    entries.Add(new KeyValuePair<string, object?>(entry.Key?.ToString() ?? "null", entry.Value));
                return true;
            case IEnumerable<KeyValuePair<string, object?>> stringPairs:
                entries.AddRange(stringPairs);
                return true;
            case IEnumerable<KeyValuePair<object?, object?>> objectPairs:
                foreach (var entry in objectPairs)
                    entries.Add(new KeyValuePair<string, object?>(entry.Key?.ToString() ?? "null", entry.Value));
                return true;
            default:
                return false;
        }
    }

    private static bool DictionaryEntriesEqual(
        List<KeyValuePair<string, object?>> leftEntries,
        List<KeyValuePair<string, object?>> rightEntries)
    {
        if (leftEntries.Count != rightEntries.Count)
            return false;

        var leftDictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        var rightDictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in leftEntries)
            leftDictionary[entry.Key] = entry.Value;
        foreach (var entry in rightEntries)
            rightDictionary[entry.Key] = entry.Value;

        if (leftDictionary.Count != rightDictionary.Count)
            return false;

        foreach (var entry in leftDictionary)
        {
            if (!rightDictionary.TryGetValue(entry.Key, out var rightValue))
                return false;
            if (!AreStructurallyEqual(entry.Value, rightValue))
                return false;
        }

        return true;
    }

    private static object? GetVariableValue(VariableExpression variable, ExecutionContext context)
    {
        // Check scalar scalar bindings first (UNWIND aliases, etc.)
        if (context.CurrentScalarBindings != null &&
            context.CurrentScalarBindings.TryGetValue(variable.VariableName, out var scalar))
            return scalar;

        if (context.CurrentVariableProperties != null &&
            context.CurrentVariableProperties.TryGetValue(variable.VariableName, out var properties))
        {
            return properties;
        }

        if (variable.QueryNode != null &&
            context.CurrentNodeProperties != null &&
            context.CurrentVariableIds != null &&
            context.CurrentVariableIds.ContainsKey(variable.VariableName) &&
            context.CurrentVariableIds.Count == 1)
        {
            return context.CurrentNodeProperties;
        }

        return null;
    }

    private static object? GetParameterValue(BoundParameterExpression parameter, ExecutionContext context)
    {
        if (context.ParameterBindings != null &&
            context.ParameterBindings.TryGetValue(parameter.ParameterName, out var value))
        {
            return TypeCoercionHelper.Normalize(value);
        }

        throw new InvalidOperationException($"Parameter '${parameter.ParameterName}' was not provided.");
    }

    /// <summary>
    /// Derives a stable string key for an aggregate expression that is consistent
    /// across independently-bound expression instances (e.g. RETURN vs ORDER BY).
    /// Examples: count(n) → "count(n)", sum(n.salary) → "sum(n.salary)".
    /// </summary>
    internal static string GetAggKey(BoundFunctionExpression func)
    {
        static string ArgStr(Expression a) => a switch
        {
            PropertyExpression p => $"{p.NodeVariableName}.{p.PropertyName}",
            VariableExpression v => v.VariableName,
            LiteralExpression  l => l.Value?.ToString() ?? "null",
            _                    => a.GetType().Name
        };
        var argSig = func.Arguments.Count == 0 ? "*"
            : string.Join(",", func.Arguments.Select(ArgStr));
        return $"{func.FunctionName.ToLower()}({argSig})";
    }
}
