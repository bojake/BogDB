using System.Collections.Generic;

namespace BogDb.Core.Common;

/// <summary>
/// Represents a batch of rows evaluating together natively within pipelines.
/// Mimics BogDb C++ `DataChunk` by encapsulating multiple ValueVectors
/// mapped against a coherent SelectionVector identifying active boundaries.
/// </summary>
public sealed class DataChunk
{
    private readonly List<ValueVector> _valueVectors;
    public SelectionVector SelectionVector { get; private set; }

    public DataChunk(int initialCapacity = 2)
    {
        _valueVectors = new List<ValueVector>(initialCapacity);
        SelectionVector = new SelectionVector();
    }

    public void Insert(int index, ValueVector valueVector)
    {
        if (index >= _valueVectors.Count)
        {
            _valueVectors.Insert(index, valueVector);
        }
        else
        {
            _valueVectors[index] = valueVector;
        }
    }

    public ValueVector GetValueVector(int idx)
    {
        return _valueVectors[idx];
    }

    public int GetNumValueVectors() => _valueVectors.Count;

    public void ResetToEmpty()
    {
        SelectionVector.SetSelSize(0);
    }
}
