using System;

namespace BogDb.Core.Common;

/// <summary>
/// Mirrors BogDb C++ `DataChunkState`.
/// Holds the SelectionVector restricting the active elements in a ValueVector.
/// </summary>
public class DataChunkState
{
    private SelectionVector _selVector;

    public DataChunkState(ushort capacity = SelectionVector.DEFAULT_VECTOR_CAPACITY)
    {
        _selVector = new SelectionVector(capacity);
    }

    public ref SelectionVector GetSelVector()
    {
        return ref _selVector;
    }
}
