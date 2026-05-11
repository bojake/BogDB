using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Binder;
using BogDb.Core.Catalog;
using BogDb.Core.Common;

namespace BogDb.Core.Processor.Operator.Update;

/// <summary>
/// Evaluates SET property assignments for nodes, supporting any RHS expression
/// (literal, arithmetic, function call, etc.) via ExpressionExecutionHelper.
/// </summary>
public sealed class PhysicalSetNodeProperty : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly List<Expression> _setItems;

    public PhysicalSetNodeProperty(
        Main.BogDatabase database,
        List<Expression> setItems,
        PhysicalOperator child,
        uint id)
        : base(PhysicalOperatorType.SET_NODE_PROPERTY, id)
    {
        _database = database;
        _setItems = setItems;
        Children.Add(child);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!Children[0].GetNextTuple(context))
            return false;

        foreach (var setItem in _setItems)
        {
            if (setItem is not BoundComparisonExpression comp ||
                comp.ExpressionType != ExpressionType.EQUALS)
                continue;

            if (comp.Left is VariableExpression variableExpr &&
                (variableExpr.QueryNode != null || variableExpr.DataType == LogicalTypeID.NODE))
            {
                ApplyPropertyBagReplacement(variableExpr.VariableName, comp.Right, context);
                continue;
            }

            if (comp.Left is not PropertyExpression propExpr)
                continue;

            var varName = propExpr.NodeVariableName;
            var propName = propExpr.PropertyName;
            var rawValue = ExpressionExecutionHelper.Evaluate(comp.Right, context);
            object? assignedValue = null;

            if (context.CurrentVariableIds != null &&
                context.CurrentVariableIds.TryGetValue(varName, out var nodeId))
            {
                foreach (var (tableName, table) in _database.NodeTables)
                {
                    var tableEntry = _database.Catalog.GetTableCatalogEntry(context.Transaction, tableName, useInternal: false);
                    var newValue = tableEntry != null && tableEntry.ContainsProperty(propName)
                        ? PropertyValueCoercion.CoercePropertyValue(tableEntry.GetProperty(propName), rawValue)
                        : TypeCoercionHelper.Normalize(rawValue);
                    if (table.SetProperty(context.Transaction, nodeId!, propName, newValue))
                    {
                        assignedValue = newValue;
                        _database.RefreshNodeIndexesForVisibleRow(
                            tableName,
                            context.Transaction,
                            nodeId!,
                            table);
                        break;
                    }
                }
            }

            var normalizedValue = assignedValue ?? string.Empty;
            if (context.CurrentVariableProperties != null &&
                context.CurrentVariableProperties.TryGetValue(varName, out var props))
            {
                var updatedProps = new Dictionary<string, object>(props)
                {
                    [propName] = normalizedValue
                };
                context.CurrentVariableProperties[varName] = updatedProps;
                if (ReferenceEquals(context.CurrentNodeProperties, props))
                    context.CurrentNodeProperties = updatedProps;
            }
            else if (context.CurrentNodeProperties != null)
            {
                context.CurrentNodeProperties = new Dictionary<string, object>(context.CurrentNodeProperties)
                {
                    [propName] = normalizedValue
                };
            }
        }
        return true;
    }

    private void ApplyPropertyBagReplacement(string varName, Expression propertyBagExpression, ExecutionContext context)
    {
        if (context.CurrentVariableIds == null ||
            !context.CurrentVariableIds.TryGetValue(varName, out var nodeId))
            return;

        var replacement = EvaluateNodePropertyBag(propertyBagExpression, context);
        foreach (var (tableName, table) in _database.NodeTables)
        {
            if (!table.TryGetProperties(context.Transaction, nodeId!, out _))
                continue;

            var tableEntry = _database.Catalog.GetTableCatalogEntry(context.Transaction, tableName, useInternal: false);
            replacement = PropertyValueCoercion.CoerceProperties(tableEntry, replacement);
            table.Upsert(context.Transaction, nodeId!, replacement);
            _database.RefreshNodeIndexesForVisibleRow(
                tableName,
                context.Transaction,
                nodeId!,
                table);
            break;
        }

        var visibleProps = new Dictionary<string, object>(replacement, StringComparer.OrdinalIgnoreCase);
        context.CurrentVariableProperties ??= new Dictionary<string, Dictionary<string, object>>();
        context.CurrentVariableProperties[varName] = visibleProps;
        if (context.CurrentNodeId == null || Equals(context.CurrentNodeId, nodeId))
            context.CurrentNodeProperties = visibleProps;
    }

    private static Dictionary<string, object> EvaluateNodePropertyBag(Expression propertyBagExpression, ExecutionContext context)
    {
        var raw = ExpressionExecutionHelper.Evaluate(propertyBagExpression, context);
        if (raw is not Dictionary<string, object> dict)
            throw new InvalidOperationException("SET property bag expressions must evaluate to a dictionary.");

        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in dict)
        {
            if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
                continue;
            result[key] = TypeCoercionHelper.Normalize(value) ?? string.Empty;
        }

        return result;
    }
}

/// <summary>
/// Evaluates SET property assignments for relationships, including whole-bag replacement.
/// </summary>
public sealed class PhysicalSetRelProperty : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly List<Expression> _setItems;

    public PhysicalSetRelProperty(
        Main.BogDatabase database,
        List<Expression> setItems,
        PhysicalOperator child,
        uint id)
        : base(PhysicalOperatorType.SET_REL_PROPERTY, id)
    {
        _database = database;
        _setItems = setItems;
        Children.Add(child);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!Children[0].GetNextTuple(context))
            return false;

        foreach (var setItem in _setItems)
        {
            if (setItem is not BoundComparisonExpression comp ||
                comp.ExpressionType != ExpressionType.EQUALS)
                continue;

            if (comp.Left is VariableExpression variableExpr &&
                (variableExpr.QueryRel != null || variableExpr.DataType == LogicalTypeID.REL))
            {
                ApplyPropertyBagReplacement(variableExpr.VariableName, comp.Right, context);
                continue;
            }

            if (comp.Left is not PropertyExpression propExpr)
                continue;

            var varName = propExpr.NodeVariableName;
            var propName = propExpr.PropertyName;
            var rawValue = ExpressionExecutionHelper.Evaluate(comp.Right, context);
            object? newValue = TypeCoercionHelper.Normalize(rawValue);
            object? assignedValue = null;

            if (context.CurrentVariableIds != null &&
                context.CurrentVariableIds.TryGetValue(varName, out var edgeKeyObj) &&
                edgeKeyObj is Main.RelRowRef relRef)
            {
                if (_database.RelTables.TryGetValue(relRef.TableName, out var table))
                {
                    var tableEntry = _database.Catalog.GetTableCatalogEntry(context.Transaction, relRef.TableName, useInternal: false);
                    if (tableEntry != null && tableEntry.ContainsProperty(propName))
                        newValue = PropertyValueCoercion.CoercePropertyValue(tableEntry.GetProperty(propName), rawValue);
                    table.SetProperty(context.Transaction, relRef.RowIndex, propName, newValue);
                    assignedValue = newValue;
                }
            }
            else if (context.CurrentVariableIds != null &&
                     context.CurrentVariableIds.TryGetValue(varName, out edgeKeyObj) &&
                     edgeKeyObj is Main.EdgeKey edgeKey)
            {
                foreach (var (tableName, table) in _database.RelTables)
                {
                    var tableEntry = _database.Catalog.GetTableCatalogEntry(context.Transaction, tableName, useInternal: false);
                    var coercedValue = tableEntry != null && tableEntry.ContainsProperty(propName)
                        ? PropertyValueCoercion.CoercePropertyValue(tableEntry.GetProperty(propName), rawValue)
                        : newValue;
                    if (table.SetProperty(context.Transaction, edgeKey, propName, coercedValue))
                    {
                        assignedValue = coercedValue;
                        break;
                    }
                }
            }

            var normalizedValue = assignedValue ?? string.Empty;
            if (context.CurrentVariableProperties != null &&
                context.CurrentVariableProperties.TryGetValue(varName, out var props))
            {
                context.CurrentVariableProperties[varName] = new Dictionary<string, object>(props)
                {
                    [propName] = normalizedValue
                };
            }
        }
        return true;
    }

    private void ApplyPropertyBagReplacement(string varName, Expression propertyBagExpression, ExecutionContext context)
    {
        if (context.CurrentVariableIds == null ||
            !context.CurrentVariableIds.TryGetValue(varName, out var edgeKeyObj) ||
            edgeKeyObj is null)
            return;

        var replacement = EvaluateRelPropertyBag(propertyBagExpression, context);
        if (edgeKeyObj is Main.RelRowRef relRef)
        {
            if (_database.RelTables.TryGetValue(relRef.TableName, out var refTable))
            {
                var tableEntry = _database.Catalog.GetTableCatalogEntry(context.Transaction, relRef.TableName, useInternal: false);
                replacement = PropertyValueCoercion.CoerceProperties(tableEntry, replacement);
                refTable.ReplaceRow(context.Transaction, relRef.RowIndex, replacement);
            }
        }
        else if (edgeKeyObj is Main.EdgeKey edgeKey)
        {
            foreach (var (tableName, table) in _database.RelTables)
            {
                if (!table.TryGetProperties(context.Transaction, edgeKey, out _))
                    continue;

                var tableEntry = _database.Catalog.GetTableCatalogEntry(context.Transaction, tableName, useInternal: false);
                replacement = PropertyValueCoercion.CoerceProperties(tableEntry, replacement);
                table.Upsert(context.Transaction, edgeKey, replacement);
                break;
            }
        }

        context.CurrentVariableProperties ??= new Dictionary<string, Dictionary<string, object>>();
        context.CurrentVariableProperties[varName] = new Dictionary<string, object>(replacement, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, object> EvaluateRelPropertyBag(Expression propertyBagExpression, ExecutionContext context)
    {
        var raw = ExpressionExecutionHelper.Evaluate(propertyBagExpression, context);
        if (raw is not Dictionary<string, object> dict)
            throw new InvalidOperationException("SET property bag expressions must evaluate to a dictionary.");

        return dict.ToDictionary(
            kvp => kvp.Key,
            kvp => TypeCoercionHelper.Normalize(kvp.Value) ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Deletes matched nodes from all node tables in the database.
/// Each delete expression is a variable expression whose value is the node ID.
/// Searches all NodeTables for the ID and removes it.
/// </summary>
public sealed class PhysicalDeleteNode : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly List<Expression> _deleteNodes;

    public PhysicalDeleteNode(
        Main.BogDatabase database,
        List<Expression> deleteNodes,
        PhysicalOperator child,
        uint id)
        : base(PhysicalOperatorType.DELETE_NODE, id)
    {
        _database = database;
        _deleteNodes = deleteNodes;
        Children.Add(child);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!Children[0].GetNextTuple(context))
            return false;

        foreach (var deleteExpr in _deleteNodes)
        {
            string varName = deleteExpr is VariableExpression varExpr
                ? varExpr.VariableName
                : deleteExpr.ToString() ?? "";

            if (string.IsNullOrEmpty(varName))
                continue;

            object? nodeId = null;
            context.CurrentVariableIds?.TryGetValue(varName, out nodeId);
            if (nodeId == null)
                continue;

            foreach (var (tableName, table) in _database.NodeTables)
            {
                if (!table.TryGetProperties(context.Transaction, nodeId, out _))
                    continue;

                // Remove index entries BEFORE deleting the row, while properties are still readable
                _database.RemoveNodeFromIndexes(tableName, context.Transaction, nodeId, table);
                table.Remove(context.Transaction, nodeId);
                break;
            }
        }
        return true;
    }
}

/// <summary>
/// Deletes matched relationship edges from all rel tables in the database.
/// </summary>
public sealed class PhysicalDeleteRel : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly List<Expression> _deleteRels;

    public PhysicalDeleteRel(
        Main.BogDatabase database,
        List<Expression> deleteRels,
        PhysicalOperator child,
        uint id)
        : base(PhysicalOperatorType.DELETE_REL, id)
    {
        _database = database;
        _deleteRels = deleteRels;
        Children.Add(child);
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (!Children[0].GetNextTuple(context))
            return false;

        foreach (var deleteExpr in _deleteRels)
        {
            string varName = deleteExpr is VariableExpression varExpr
                ? varExpr.VariableName
                : deleteExpr.ToString() ?? "";

            if (string.IsNullOrEmpty(varName))
                continue;

            object? edgeKey = null;
            context.CurrentVariableIds?.TryGetValue(varName, out edgeKey);
            if (edgeKey is Main.RelRowRef relRef)
            {
                if (_database.RelTables.TryGetValue(relRef.TableName, out var table))
                    table.Remove(context.Transaction, relRef.RowIndex);
                continue;
            }

            if (edgeKey is not Main.EdgeKey key)
                continue;

            foreach (var table in _database.RelTables.Values)
            {
                if (table.Remove(context.Transaction, key))
                    break;
            }
        }
        return true;
    }
}
