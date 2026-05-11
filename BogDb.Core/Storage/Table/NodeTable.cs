using BogDb.Core.Common;

namespace BogDb.Core.Storage.Table;

/// <summary>
/// Represents a Node table in Storage.
/// Contains Column collections to deserialize `node_group` values out of the Buffer Manager.
/// </summary>
public class NodeTable 
{
    private readonly uint _tableId;
    private readonly ColumnChunkData[] _propertyColumns;
    private readonly Column[] _columns;

    public NodeTable(uint tableId, ColumnChunkData[] propertyColumns)
    {
        _tableId = tableId;
        _propertyColumns = propertyColumns;
        _columns = Array.Empty<Column>();
    }

    public NodeTable(uint tableId, Column[] columns)
    {
        _tableId = tableId;
        _columns = columns;
        _propertyColumns = Array.Empty<ColumnChunkData>();
    }

    /// <summary>
    /// Reads a set of node properties directly into the ValueVectors for execution.
    /// Replicates `node_table.cpp` logic.
    /// </summary>
    public void Read(Transaction.Transaction tx, ValueVector[] propertyVectors, uint nodeOffsetStart, uint numNodesToRead, BufferManager.BufferManager? bm = null)
    {
        if (_columns.Length > 0)
        {
            for (int i = 0; i < propertyVectors.Length && i < _columns.Length; i++)
            {
                var column = _columns[i];
                var vector = propertyVectors[i];
                var outPos = 0u;
                foreach (var value in column.Scan(tx, nodeOffsetStart, numNodesToRead))
                {
                    ColumnChunkData.WriteValueToVector(vector, outPos++, value);
                }
            }
            return;
        }

        if (bm is null)
            throw new ArgumentNullException(nameof(bm), "BufferManager is required for file-backed column reads.");

        for (int i = 0; i < propertyVectors.Length; i++)
        {
            if (i < _propertyColumns.Length)
            {
                _propertyColumns[i].Scan(bm, propertyVectors[i], nodeOffsetStart, numNodesToRead, 0);
            }
        }
    }
}
