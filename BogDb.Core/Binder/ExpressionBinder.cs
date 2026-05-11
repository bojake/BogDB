using System;
using BogDb.Core.Common;
using BogDb.Core.Parser;

namespace BogDb.Core.Binder;

/// <summary>
/// The ExpressionBinder converts ParsedExpressions (from ANTLR) into strongly-typed
/// Expressions ready for the query optimizer, enforcing LogicalType bounds rules.
/// Phase 8: added LITERAL, PROPERTY, FUNCTION support.
/// </summary>
public class ExpressionBinder
{
    private readonly Binder _queryBinder;

    public ExpressionBinder(Binder queryBinder)
    {
        _queryBinder = queryBinder;
    }

    /// <summary>Binds an unstructured parser expression into a strongly typed bound expression.</summary>
    public Expression BindExpression(ParsedExpression parsedExpression)
    {
        Expression bound;
        switch (parsedExpression.GetExpressionType())
        {
            case ExpressionType.AND:
            case ExpressionType.OR:
            case ExpressionType.XOR:
            case ExpressionType.NOT:
                bound = BindBooleanExpression(parsedExpression);
                break;

            case ExpressionType.EQUALS:
            case ExpressionType.NOT_EQUALS:
            case ExpressionType.GREATER_THAN:
            case ExpressionType.GREATER_THAN_EQUALS:
            case ExpressionType.LESS_THAN:
            case ExpressionType.LESS_THAN_EQUALS:
                bound = BindComparisonExpression(parsedExpression);
                break;

            case ExpressionType.IS_NULL:
            case ExpressionType.IS_NOT_NULL:
                bound = BindIsNullExpression(parsedExpression);
                break;

            case ExpressionType.VARIABLE:
                bound = BindVariableExpression(parsedExpression);
                break;

            case ExpressionType.PARAMETER:
                bound = BindParameterExpression((ParsedParameterExpression)parsedExpression);
                break;

            case ExpressionType.PROPERTY:
                bound = BindPropertyExpression((ParsedPropertyExpression)parsedExpression);
                break;

            case ExpressionType.LITERAL:
                bound = BindLiteralExpression((ParsedLiteralExpression)parsedExpression);
                break;

            case ExpressionType.FUNCTION:
                bound = BindFunctionExpression((ParsedFunctionExpression)parsedExpression);
                break;

            case ExpressionType.LAMBDA:
                if (parsedExpression is ParsedQuantifierExpression quantifierExpr)
                    bound = BindQuantifierExpression(quantifierExpr);
                else if (parsedExpression is ParsedLambdaExpression lambdaExpr)
                    bound = BindLambdaExpression(lambdaExpr);
                else
                    bound = new LiteralExpression(null, LogicalTypeID.ANY);
                break;

            case ExpressionType.STAR:
                // SELECT * - return a sentinel STAR expression
                bound = new LiteralExpression(null, LogicalTypeID.ANY);
                break;

            case ExpressionType.SUBQUERY:
                bound = BindSubqueryExpression((ParsedSubqueryExpression)parsedExpression);
                break;

            case ExpressionType.INVALID:
                bound = new LiteralExpression(null, LogicalTypeID.ANY);
                break;

            default:
                bound = BindOpaqueExpression(parsedExpression);
                break;
        }

        if (parsedExpression.HasAlias())
            bound.SetAlias(parsedExpression.GetAlias());

        return bound;
    }

    public Expression BindBooleanExpression(ParsedExpression parsedExpression)
    {
        if (parsedExpression.GetExpressionType() == ExpressionType.NOT)
        {
            var boundChild = BindExpression(parsedExpression.GetChild(0));
            ValidateExpectedDataType(boundChild, LogicalTypeID.BOOL);
            return new BoundBooleanExpression(ExpressionType.NOT, boundChild);
        }

        var left = BindExpression(parsedExpression.GetChild(0));
        var right = BindExpression(parsedExpression.GetChild(1));
        ValidateExpectedDataType(left, LogicalTypeID.BOOL);
        ValidateExpectedDataType(right, LogicalTypeID.BOOL);
        return new BoundBooleanExpression(parsedExpression.GetExpressionType(), left, right);
    }

    public Expression BindComparisonExpression(ParsedExpression parsedExpression)
    {
        var left = BindExpression(parsedExpression.GetChild(0));
        var right = BindExpression(parsedExpression.GetChild(1));

        if (left is BoundParameterExpression leftParameter && right.DataType != LogicalTypeID.ANY)
            _queryBinder.RegisterParameterExpectedType(leftParameter.ParameterName, right.DataType);
        if (right is BoundParameterExpression rightParameter && left.DataType != LogicalTypeID.ANY)
            _queryBinder.RegisterParameterExpectedType(rightParameter.ParameterName, left.DataType);

        // Allow comparing with ANY type (e.g. nulls, unresolved), and allow numeric conversions
        // across the full logical numeric family, including unsigned widths and DECIMAL.
        var isNumericLeft = LogicalTypeUtils.IsNumerical(left.DataType);
        var isNumericRight = LogicalTypeUtils.IsNumerical(right.DataType);

        if (left.DataType != right.DataType
            && left.DataType != LogicalTypeID.ANY
            && right.DataType != LogicalTypeID.ANY
            && !(isNumericLeft && isNumericRight)
            && !(LogicalTypeUtils.IsTimestamp(left.DataType) && LogicalTypeUtils.IsTimestamp(right.DataType)))
        {
            throw new InvalidOperationException(
                $"Cannot compare differing types: {left.DataType} and {right.DataType}");
        }

        return new BoundComparisonExpression(parsedExpression.GetExpressionType(), left, right);
    }

    public Expression BindIsNullExpression(ParsedExpression parsedExpression)
    {
        var child = BindExpression(parsedExpression.GetChild(0));
        return new BoundBooleanExpression(parsedExpression.GetExpressionType(), child);
    }

    public Expression BindVariableExpression(ParsedExpression parsedExpression)
    {
        // Accept any ParsedExpression with type VARIABLE - use GetRawName() as the variable name
        // so that subclasses (e.g. DummyVariableParsedExpression in tests) work without casting.
        var varName = parsedExpression is ParsedVariableExpression pve
            ? pve.VariableName
            : parsedExpression.GetRawName();

        if (_queryBinder.Scope.Contains(varName))
            return _queryBinder.Scope.GetExpression(varName);

        throw new InvalidOperationException($"Variable '{varName}' is not in scope. Available: {string.Join(", ", _queryBinder.Scope.GetAllExpressions().Select(x => x.Item1))}");
    }

    public Expression BindParameterExpression(ParsedParameterExpression parameter)
        => new BoundParameterExpression(parameter.ParameterName);

    public Expression BindPropertyExpression(ParsedPropertyExpression parsedProperty)
    {
        // The child should be a variable expression (e.g. `n`)
        var childExpr = BindExpression(parsedProperty.GetChildExpression());

        if (childExpr is VariableExpression varExpr && varExpr.QueryNode != null)
        {
            var node = varExpr.QueryNode;
            var propExpr = node.GetPropertyExpression(parsedProperty.PropertyName);
            if (propExpr != null)
                return propExpr;

            throw new InvalidOperationException(
                $"Property '{parsedProperty.PropertyName}' not found on node '{node.VariableName}' " +
                $"(tables: {string.Join(",", node.TableNames)}).");
        }

        if (childExpr is VariableExpression relVarExpr && relVarExpr.QueryRel != null)
        {
            var rel = relVarExpr.QueryRel;
            var propExpr = rel.GetPropertyExpression(parsedProperty.PropertyName);
            if (propExpr != null)
                return propExpr;

            throw new InvalidOperationException(
                $"Property '{parsedProperty.PropertyName}' not found on relationship '{rel.VariableName}' " +
                $"(tables: {string.Join(",", rel.TableNames)}).");
        }

        // Fallback - return an untyped property expression
        return new PropertyExpression(parsedProperty.PropertyName,
            parsedProperty.GetChildExpression().GetRawName(), LogicalTypeID.ANY);
    }

    public Expression BindLiteralExpression(ParsedLiteralExpression literal)
        => new LiteralExpression(literal.Value, literal.LiteralTypeId);

    public Expression BindFunctionExpression(ParsedFunctionExpression func)
    {
        var macroEntry = _queryBinder.Catalog.GetScalarMacroCatalogEntry(null, func.FunctionName);
        if (macroEntry != null)
        {
            var expanded = ExpandMacroInvocation(macroEntry, func);
            return BindExpression(expanded);
        }

        // Bind all child arguments first
        var args = new List<Expression>();
        for (var i = 0; i < func.GetNumChildren(); i++)
            args.Add(BindExpression(func.GetChild(i)));

        // Constant-fold literal+literal arithmetic at bind time
        if (args.Count == 2 &&
            (func.FunctionName == "+" || func.FunctionName == "-" ||
             func.FunctionName == "*" || func.FunctionName == "/"))
        {
            if (args[0] is LiteralExpression litL && args[1] is LiteralExpression litR)
            {
                if (func.FunctionName == "+" &&
                    (litL.DataType == LogicalTypeID.STRING || litR.DataType == LogicalTypeID.STRING))
                {
                    var left = litL.Value?.ToString();
                    var right = litR.Value?.ToString();
                    return new LiteralExpression($"{left}{right}", LogicalTypeID.STRING);
                }

                if (litL.Value is not IConvertible cL || litR.Value is not IConvertible cR)
                    goto SkipConstantFold;

                var hasDouble = litL.DataType == LogicalTypeID.DOUBLE || litR.DataType == LogicalTypeID.DOUBLE;
                if (hasDouble)
                {
                    var l = cL.ToDouble(null);
                    var r = cR.ToDouble(null);
                    var res = func.FunctionName switch { "+" => l + r, "-" => l - r, "*" => l * r, _ => r != 0 ? l / r : double.NaN };
                    return new LiteralExpression(res, LogicalTypeID.DOUBLE);
                }
                else
                {
                    var l = cL.ToInt64(null);
                    var r = cR.ToInt64(null);
                    var res = func.FunctionName switch { "+" => l + r, "-" => l - r, "*" => l * r, _ => r != 0 ? l / r : 0L };
                    return new LiteralExpression(res, LogicalTypeID.INT64);
                }
            }
        }

SkipConstantFold:

        // Determine return type based on function category
        var name = func.FunctionName.ToLowerInvariant();
        ApplyFunctionArgumentExpectations(name, args);
        if (name is "case" or "case_simple")
        {
            var startIndex = name == "case_simple" ? 1 : 0;
            var resultType = LogicalTypeID.ANY;
            for (var i = startIndex + 1; i < args.Count; i += 2)
            {
                var candidate = args[i].DataType;
                if (candidate == LogicalTypeID.ANY)
                    continue;
                if (resultType == LogicalTypeID.ANY)
                    resultType = candidate;
                else if (resultType != candidate)
                {
                    resultType = LogicalTypeID.ANY;
                    break;
                }
            }

            if ((args.Count - startIndex) % 2 == 1)
            {
                var elseType = args[^1].DataType;
                if (elseType != LogicalTypeID.ANY)
                {
                    if (resultType == LogicalTypeID.ANY)
                        resultType = elseType;
                    else if (resultType != elseType)
                        resultType = LogicalTypeID.ANY;
                }
            }

            return new BoundFunctionExpression(func.FunctionName, args, resultType);
        }

        var returnType = name switch
        {
            // Arithmetic -> propagate widest numeric type
            "+"
                => args.Any(a => a.DataType == LogicalTypeID.STRING) ? LogicalTypeID.STRING
                    : args.Any(a => a.DataType == LogicalTypeID.DOUBLE) ? LogicalTypeID.DOUBLE : LogicalTypeID.INT64,
            "-" or "*" or "/" or "%" or "^" or "pow" or "power" or "mod" or "abs"
                => args.Any(a => a.DataType == LogicalTypeID.DOUBLE) ? LogicalTypeID.DOUBLE : LogicalTypeID.INT64,
            "ceil" or "ceiling" or "floor" or "round" or "sqrt"
                => LogicalTypeID.DOUBLE,

            // String predicates -> BOOL
            "starts_with" or "startswith" or "ends_with" or "endswith"
            or "contains" or "regexp_matches" or "regexp_extract"
                => LogicalTypeID.BOOL,

            // String transformations -> STRING
            "tolower" or "lower" or "toupper" or "upper" or "trim" or "ltrim" or "rtrim"
            or "concat" or "||" or "left" or "right" or "substring" or "substr"
            or "replace" or "reverse" or "tostring" or "str"
                => LogicalTypeID.STRING,

            // Size / length -> INT64
            "size" or "length" or "strlen" or "char_length" or "character_length"
            or "array_length" or "len"
                => LogicalTypeID.INT64,

            // Type conversions
            "tointeger" or "toint" or "int" => LogicalTypeID.INT64,
            "tofloat" or "todouble" or "todecimal" or "float" or "double" => LogicalTypeID.DOUBLE,
            "interval" or "duration" or "tointerval" => LogicalTypeID.INTERVAL,
            "toyears" or "toyear" or "to_months" or "tomonths" or "tomonth"
            or "toquarters" or "toquarter" or "todecades" or "todecade"
            or "tocenturies" or "tocentury" or "tomillennia" or "tomillennium"
            or "todays" or "today" or "toweeks" or "toweek"
            or "tohours" or "tohour" or "tominutes" or "tominute"
            or "toseconds" or "tosecond" or "tomilliseconds" or "tomillisecond"
            or "tomicroseconds" or "tomicrosecond"
                => LogicalTypeID.INTERVAL,
            "date" => LogicalTypeID.DATE,
            "timestamp" => LogicalTypeID.TIMESTAMP,
            "timestamptz" => LogicalTypeID.TIMESTAMP_TZ,
            "date_part" or "datepart" => LogicalTypeID.INT64,
            "year" or "month" or "day" or "hour" or "minute" or "second"
            or "millisecond" or "microsecond" or "quarter" or "week"
            or "decade" or "century" or "millennium"
                => LogicalTypeID.INT64,

            // List/struct literals -> ANY (elements may be mixed types)
            "list_literal" or "struct_literal" => LogicalTypeID.ANY,

            // Aggregates
            "count" or "count_star" => LogicalTypeID.INT64,
            "sum" => LogicalTypeID.DOUBLE,
            "avg" => LogicalTypeID.DOUBLE,
            "min" or "max" => args.Count > 0 ? args[0].DataType : LogicalTypeID.ANY,
            "collect" => LogicalTypeID.LIST,
            "count_if" => LogicalTypeID.INT64,

            _ => LogicalTypeID.ANY
        };

        // Determine if this is an aggregate function
        var isAggregate = name is "count" or "count_star" or "sum" or "avg" or "min" or "max" or "collect" or "count_if";

        return new BoundFunctionExpression(func.FunctionName, args, returnType)
        {
            IsAggregate = isAggregate,
            IsDistinct = func.IsDistinct,
        };
    }

    private void ApplyFunctionArgumentExpectations(string name, IReadOnlyList<Expression> args)
    {
        switch (name)
        {
            case "interval":
            case "duration":
            case "tointerval":
            case "date":
            case "timestamp":
            case "timestamptz":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                return;

            case "date_part":
            case "datepart":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                return;

            case "date_trunc":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                return;

            case "date_add":
            case "date_sub":
            case "datediff":
            case "date_diff":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                RegisterExpectedType(args, 2, LogicalTypeID.INT64);
                return;

            case "timestamp_add":
            case "timestamp_diff":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                RegisterExpectedType(args, 1, LogicalTypeID.TIMESTAMP_TZ);
                RegisterExpectedType(args, 2, LogicalTypeID.INT64);
                return;

            case "make_date":
                RegisterExpectedType(args, 0, LogicalTypeID.INT64);
                RegisterExpectedType(args, 1, LogicalTypeID.INT64);
                RegisterExpectedType(args, 2, LogicalTypeID.INT64);
                return;

            case "make_timestamp":
                for (var i = 0; i < args.Count; i++)
                    RegisterExpectedType(args, i, LogicalTypeID.INT64);
                return;

            case "to_epoch_ms":
            case "epoch_ms_from_timestamp":
            case "to_epoch_us":
            case "epoch_us_from_timestamp":
            case "to_epoch_s":
            case "epoch_s_from_timestamp":
            case "timestamp_year":
            case "timestamp_month":
            case "timestamp_day":
            case "timestamp_hour":
            case "timestamp_minute":
            case "timestamp_second":
            case "timestamp_millisecond":
                RegisterExpectedType(args, 0, LogicalTypeID.TIMESTAMP_TZ);
                return;

            case "ms_to_timestamp":
            case "us_to_timestamp":
            case "epoch_s":
                RegisterExpectedType(args, 0, LogicalTypeID.INT64);
                return;

            case "starts_with":
            case "startswith":
            case "ends_with":
            case "endswith":
            case "contains":
            case "trim":
            case "ltrim":
            case "rtrim":
            case "tolower":
            case "lower":
            case "toupper":
            case "upper":
            case "regexp_matches":
            case "position":
            case "locate":
            case "instr":
                for (var i = 0; i < args.Count; i++)
                    RegisterExpectedType(args, i, LogicalTypeID.STRING);
                return;

            case "concat":
            case "||":
            case "concat_ws":
                for (var i = 0; i < args.Count; i++)
                    RegisterExpectedType(args, i, LogicalTypeID.STRING);
                return;

            case "replace":
            case "regexp_replace":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                RegisterExpectedType(args, 1, LogicalTypeID.STRING);
                RegisterExpectedType(args, 2, LogicalTypeID.STRING);
                return;

            case "substring":
            case "substr":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                RegisterExpectedType(args, 1, LogicalTypeID.INT64);
                RegisterExpectedType(args, 2, LogicalTypeID.INT64);
                return;

            case "left":
            case "right":
            case "repeat":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                RegisterExpectedType(args, 1, LogicalTypeID.INT64);
                return;

            case "lpad":
            case "rpad":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                RegisterExpectedType(args, 1, LogicalTypeID.INT64);
                RegisterExpectedType(args, 2, LogicalTypeID.STRING);
                return;

            case "regexp_extract":
            case "regexp_extract_all":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                RegisterExpectedType(args, 1, LogicalTypeID.STRING);
                RegisterExpectedType(args, 2, LogicalTypeID.INT64);
                return;

            case "regexp_split_to_array":
            case "split":
            case "string_split":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                RegisterExpectedType(args, 1, LogicalTypeID.STRING);
                return;

            case "split_part":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                RegisterExpectedType(args, 1, LogicalTypeID.STRING);
                RegisterExpectedType(args, 2, LogicalTypeID.INT64);
                return;

            case "size":
            case "length":
            case "strlen":
            case "char_length":
            case "character_length":
            case "array_length":
            case "len":
                return;

            case "range":
            case "generate_series":
                RegisterExpectedType(args, 0, LogicalTypeID.INT64);
                RegisterExpectedType(args, 1, LogicalTypeID.INT64);
                RegisterExpectedType(args, 2, LogicalTypeID.INT64);
                return;

            case "list_element":
            case "list_extract":
            case "array_extract":
            case "list_slice":
                RegisterExpectedType(args, 1, LogicalTypeID.INT64);
                RegisterExpectedType(args, 2, LogicalTypeID.INT64);
                return;

            case "list_slice_from":
                RegisterExpectedType(args, 1, LogicalTypeID.INT64);
                return;

            case "list_transform":
            case "list_apply":
                RegisterExpectedType(args, 1, LogicalTypeID.STRING);
                return;

            case "map":
                RegisterExpectedType(args, 0, LogicalTypeID.LIST);
                RegisterExpectedType(args, 1, LogicalTypeID.LIST);
                return;

            case "map_extract":
            case "element_at":
            case "map_contains":
            case "map_has_key":
            case "map_keys":
            case "map_values":
            case "map_size":
            case "map_cardinality":
            case "map_entries":
                RegisterExpectedType(args, 0, LogicalTypeID.MAP);
                return;

            case "tointeger":
            case "toint":
            case "int":
            case "tofloat":
            case "todouble":
            case "todecimal":
            case "float":
            case "double":
            case "to_hex":
                RegisterExpectedType(args, 0, LogicalTypeID.DOUBLE);
                return;

            case "from_hex":
            case "ascii":
            case "unicode":
            case "base64_encode":
            case "base64_decode":
            case "url_encode":
            case "url_decode":
            case "bit_length":
            case "octet_length":
            case "strftime":
            case "date_format":
                RegisterExpectedType(args, 0, LogicalTypeID.STRING);
                return;

            case "chr":
                RegisterExpectedType(args, 0, LogicalTypeID.INT64);
                return;

            case "ceil":
            case "ceiling":
            case "floor":
            case "round":
            case "sqrt":
            case "abs":
            case "pow":
            case "power":
            case "mod":
            case "+":
            case "-":
            case "*":
            case "/":
            case "%":
            case "^":
                for (var i = 0; i < args.Count; i++)
                    RegisterExpectedType(args, i, LogicalTypeID.DOUBLE);
                return;
        }
    }

    private void RegisterExpectedType(IReadOnlyList<Expression> args, int index, LogicalTypeID expectedType)
    {
        if (index < 0 || index >= args.Count)
            return;

        if (args[index] is BoundParameterExpression parameter)
            _queryBinder.RegisterParameterExpectedType(parameter.ParameterName, expectedType);
    }

    private static ParsedExpression ExpandMacroInvocation(Catalog.ScalarMacroCatalogEntry macroEntry, ParsedFunctionExpression invocation)
    {
        var parameterBindings = new Dictionary<string, ParsedExpression>(StringComparer.OrdinalIgnoreCase);
        var invocationArgCount = invocation.GetNumChildren();
        var parameters = macroEntry.Parameters;
        var requiredCount = parameters.Count(p => p.DefaultExpression == null);

        if (invocationArgCount < requiredCount || invocationArgCount > parameters.Count)
        {
            throw new InvalidOperationException(
                $"Macro '{macroEntry.Name}' expects between {requiredCount} and {parameters.Count} arguments, but got {invocationArgCount}.");
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            if (i < invocationArgCount)
            {
                parameterBindings[parameters[i].Name] = invocation.GetChild(i).Copy();
                continue;
            }

            if (parameters[i].DefaultExpression == null)
                throw new InvalidOperationException($"Macro '{macroEntry.Name}' is missing required argument '{parameters[i].Name}'.");

            parameterBindings[parameters[i].Name] = parameters[i].DefaultExpression.Copy();
        }

        return SubstituteMacroParameters(macroEntry.BodyExpression, parameterBindings);
    }

    private static ParsedExpression SubstituteMacroParameters(
        ParsedExpression expression,
        IReadOnlyDictionary<string, ParsedExpression> parameterBindings)
    {
        if (expression is ParsedVariableExpression variableExpression &&
            parameterBindings.TryGetValue(variableExpression.VariableName, out var replacement))
        {
            return replacement.Copy();
        }

        var copy = expression.Copy();
        for (var i = 0; i < copy.GetNumChildren(); i++)
            copy.SetChild(i, SubstituteMacroParameters(copy.GetChild(i), parameterBindings));
        return copy;
    }

    public Expression BindQuantifierExpression(ParsedQuantifierExpression quantifier)
    {
        var boundCollection = BindExpression(quantifier.CollectionExpression);
        Expression? priorExpression = null;
        var hadPriorExpression = _queryBinder.Scope.Contains(quantifier.VariableName);
        if (hadPriorExpression)
        {
            priorExpression = _queryBinder.Scope.GetExpression(quantifier.VariableName);
            _queryBinder.Scope.RemoveExpression(quantifier.VariableName);
        }

        _queryBinder.Scope.AddExpression(
            quantifier.VariableName,
            new VariableExpression(quantifier.VariableName, LogicalTypeID.ANY));

        try
        {
            var boundPredicate = BindExpression(quantifier.PredicateExpression);
            ValidateExpectedDataType(boundPredicate, LogicalTypeID.BOOL);
            return new BoundQuantifierExpression(
                quantifier.QuantifierName,
                quantifier.VariableName,
                boundCollection,
                boundPredicate);
        }
        finally
        {
            _queryBinder.Scope.RemoveExpression(quantifier.VariableName);
            if (hadPriorExpression && priorExpression != null)
                _queryBinder.Scope.AddExpression(quantifier.VariableName, priorExpression);
        }
    }

    public Expression BindSubqueryExpression(ParsedSubqueryExpression subqueryExpr)
    {
        // BogDb bindings enforce scoping parameters
        // Binding a subquery creates an independent compilation context natively over the AST.
        var subqueryBinder = new Binder(_queryBinder.Catalog);
        // Propagate the parent's scoped variables so the subquery can reference them
        foreach (var (key, expr) in _queryBinder.Scope.GetAllExpressions())
        {
            subqueryBinder.Scope.AddExpression(key, expr);
        }

        var boundQuery = subqueryBinder.BindQuery(subqueryExpr.Subquery, preserveInitialScope: true);
        return new BoundSubqueryExpression(subqueryExpr.SubqueryType, boundQuery, subqueryExpr.GetRawName());
    }

    private Expression BindOpaqueExpression(ParsedExpression parsedExpression)
    {
        var args = new List<Expression>();
        for (var i = 0; i < parsedExpression.GetNumChildren(); i++)
        {
            var child = parsedExpression.GetChild(i);
            if (child == null)
                continue;

            args.Add(BindExpression(child));
        }

        return new BoundFunctionExpression(parsedExpression.GetExpressionType().ToString(), args, LogicalTypeID.ANY);
    }

    public Expression ImplicitCastIfNecessary(Expression expression, LogicalTypeID targetType)
    {
        if (expression.DataType == targetType || expression.DataType == LogicalTypeID.ANY
            || targetType == LogicalTypeID.ANY)
            return expression;

        throw new InvalidOperationException(
            $"Cannot implicitly cast {expression.DataType} to {targetType}");
    }

    public void ValidateExpectedDataType(Expression expr, LogicalTypeID expectedType)
    {
        if (expr is BoundParameterExpression parameter && expectedType != LogicalTypeID.ANY)
        {
            _queryBinder.RegisterParameterExpectedType(parameter.ParameterName, expectedType);
            return;
        }

        var isNumericExpr = expr.DataType == LogicalTypeID.INT32 || expr.DataType == LogicalTypeID.INT64 || expr.DataType == LogicalTypeID.DOUBLE || expr.DataType == LogicalTypeID.FLOAT;
        var isNumericExpected = expectedType == LogicalTypeID.INT32 || expectedType == LogicalTypeID.INT64 || expectedType == LogicalTypeID.DOUBLE || expectedType == LogicalTypeID.FLOAT;

        if (expr.DataType != expectedType &&
            expr.DataType != LogicalTypeID.ANY &&
            expectedType != LogicalTypeID.ANY &&
            !(isNumericExpr && isNumericExpected) &&
            !(LogicalTypeUtils.IsTimestamp(expr.DataType) && LogicalTypeUtils.IsTimestamp(expectedType)))
        {
            throw new InvalidOperationException($"Type mismatch: expected {expectedType} but got {expr.DataType}.");
        }
    }

    public static bool AreEquivalent(Expression a, Expression b)
    {
        if (a.ExpressionType != b.ExpressionType) return false;
        if (a is VariableExpression va && b is VariableExpression vb)
            return va.VariableName == vb.VariableName;
        if (a is PropertyExpression pa && b is PropertyExpression pb)
            return pa.PropertyName == pb.PropertyName && pa.NodeVariableName == pb.NodeVariableName;
        if (a is LiteralExpression la && b is LiteralExpression lb)
            return Equals(la.Value, lb.Value);
        return false;
    }

    public Expression BindLambdaExpression(ParsedLambdaExpression lambda)
    {
        // Temporarily add lambda parameter variables to scope
        var priors = new List<(string name, Expression? expr, bool had)>();
        foreach (var paramName in lambda.ParameterNames)
        {
            var had = _queryBinder.Scope.Contains(paramName);
            Expression? prior = had ? _queryBinder.Scope.GetExpression(paramName) : null;
            if (had) _queryBinder.Scope.RemoveExpression(paramName);
            priors.Add((paramName, prior, had));
            _queryBinder.Scope.AddExpression(
                paramName, new VariableExpression(paramName, LogicalTypeID.ANY));
        }

        try
        {
            var boundBody = BindExpression(lambda.Body);
            return new BoundLambdaExpression(lambda.ParameterNames, boundBody);
        }
        finally
        {
            foreach (var (name, expr, had) in priors)
            {
                _queryBinder.Scope.RemoveExpression(name);
                if (had && expr != null)
                    _queryBinder.Scope.AddExpression(name, expr);
            }
        }
    }
}

public class BoundBooleanExpression : Expression
{
    public Expression Left { get; }
    public Expression? Right { get; }

    public BoundBooleanExpression(ExpressionType type, Expression child)
        : base(type, LogicalTypeID.BOOL) { Left = child; Right = null; }

    public BoundBooleanExpression(ExpressionType type, Expression left, Expression right)
        : base(type, LogicalTypeID.BOOL) { Left = left; Right = right; }
}

public sealed class BoundQuantifierExpression : Expression
{
    public string QuantifierName { get; }
    public string VariableName { get; }
    public Expression CollectionExpression { get; }
    public Expression PredicateExpression { get; }

    public BoundQuantifierExpression(
        string quantifierName,
        string variableName,
        Expression collectionExpression,
        Expression predicateExpression)
        : base(ExpressionType.LAMBDA, LogicalTypeID.BOOL)
    {
        QuantifierName = quantifierName.ToUpperInvariant();
        VariableName = variableName;
        CollectionExpression = collectionExpression;
        PredicateExpression = predicateExpression;
    }
}


public sealed class BoundParameterExpression : Expression
{
    public string ParameterName { get; }

    public BoundParameterExpression(string parameterName)
        : base(ExpressionType.PARAMETER, LogicalTypeID.ANY)
    {
        ParameterName = parameterName;
    }
}

/// <summary>
/// A bound lambda expression, e.g. <c>x -> x > 0</c>.
/// Used by list_filter, list_reduce, and list_transform with arrow syntax.
/// </summary>
public sealed class BoundLambdaExpression : Expression
{
    public List<string> ParameterNames { get; }
    public Expression Body { get; }

    public BoundLambdaExpression(List<string> parameterNames, Expression body)
        : base(ExpressionType.LAMBDA, LogicalTypeID.ANY)
    {
        ParameterNames = parameterNames;
        Body = body;
    }
}

public class BoundComparisonExpression : Expression
{
    public Expression Left { get; }
    public Expression Right { get; }

    public BoundComparisonExpression(ExpressionType type, Expression left, Expression right)
        : base(type, LogicalTypeID.BOOL) { Left = left; Right = right; }
}
