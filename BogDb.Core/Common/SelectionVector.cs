using System;
using System.Runtime.CompilerServices;

namespace BogDb.Core.Common;

/// <summary>
/// Maintains a list of selected positions in a ValueVector.
/// Mirrors BogDb C++ `SelectionVector`.
/// </summary>
public sealed class SelectionVector
{
    public const ushort DEFAULT_VECTOR_CAPACITY = 2048;
    
    private readonly ushort[] _selectedPositions;
    private ushort _selectedSize;

    public SelectionVector(ushort capacity = DEFAULT_VECTOR_CAPACITY)
    {
        _selectedPositions = new ushort[capacity];
        _selectedSize = capacity;
        
        // By default, a selection vector is un-filtered (1:1 mapping)
        for (ushort i = 0; i < capacity; i++)
        {
            _selectedPositions[i] = i;
        }
    }

    public void SetSelSize(ushort size)
    {
        _selectedSize = size;
    }

    public ushort GetSelSize()
    {
        return _selectedSize;
    }

    public ref ushort GetSelectedPositions()
    {
        return ref _selectedPositions[0];
    }

    // Exposed for SIMD / Intrinsics writing
    public Span<ushort> GetMutableBuffer()
    {
        return _selectedPositions.AsSpan(0, _selectedSize);
    }
}
