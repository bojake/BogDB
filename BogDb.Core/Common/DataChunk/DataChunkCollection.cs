using System;
using System.Collections.Generic;

namespace BogDb.Core.Common;

/// <summary>
/// A managed collection wrapping arrays of DataChunks utilized across hash joins or grouping
/// pipelines caching massive states. Mimics C++ `DataChunkCollection`.
/// </summary>
public sealed class DataChunkCollection
{
    private readonly List<DataChunk> _chunks;

    public DataChunkCollection()
    {
        _chunks = new List<DataChunk>();
    }

    public void Append(DataChunk chunk)
    {
        _chunks.Add(chunk);
    }

    public DataChunk GetChunk(int idx)
    {
        return _chunks[idx];
    }

    public int GetNumChunks() => _chunks.Count;

    public void Clear()
    {
        _chunks.Clear();
    }
}
