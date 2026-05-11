using System;
using System.Collections.Generic;

namespace BogDb.Core.Storage.Table;

internal sealed class CompatibilityColumnMirror
{
    private readonly IDictionary<string, List<object?>> _columns;
    private readonly IDictionary<string, Column> _columnStores;
    private ColumnFactory? _columnFactory;

    public CompatibilityColumnMirror(
        IDictionary<string, List<object?>> columns,
        IDictionary<string, Column> columnStores)
    {
        _columns = columns;
        _columnStores = columnStores;
    }

    internal void SetColumnFactory(ColumnFactory? factory) => _columnFactory = factory;

    public void AppendRow(int rowCount, Dictionary<string, object> properties)
    {
        var rowIndex = rowCount - 1;
        foreach (var column in _columns.Values)
            column.Add(null);
        foreach (var columnStore in _columnStores.Values)
            columnStore.Append(null);

        ApplyValues(rowIndex, rowCount, properties);
    }

    public void OverwriteRow(int rowIndex, int rowCount, Dictionary<string, object> properties)
    {
        foreach (var column in _columns.Values)
            column[rowIndex] = null;
        foreach (var columnStore in _columnStores.Values)
            columnStore.Update(rowIndex, null);

        ApplyValues(rowIndex, rowCount, properties);
    }

    public void Truncate(int rowCount)
    {
        foreach (var column in _columns.Values)
        {
            if (column.Count > rowCount)
                column.RemoveRange(rowCount, column.Count - rowCount);
        }

        foreach (var columnStore in _columnStores.Values)
            columnStore.Truncate(rowCount);
    }

    private void ApplyValues(int rowIndex, int rowCount, Dictionary<string, object> properties)
    {
        foreach (var (name, value) in properties)
        {
            if (!_columns.TryGetValue(name, out var column))
            {
                column = new List<object?>(rowCount);
                for (var i = 0; i < rowCount; i++)
                    column.Add(null);
                _columns[name] = column;
            }

            if (!_columnStores.TryGetValue(name, out var columnStore))
            {
                columnStore = _columnFactory?.CreateColumn(name, Math.Max(1024, rowCount))
                    ?? new Column(name, Math.Max(1024, rowCount));
                for (var i = 0; i < rowCount; i++)
                    columnStore.Append(null);
                _columnStores[name] = columnStore;
            }

            column[rowIndex] = value;
            columnStore.Update(rowIndex, value);
        }
    }
}
