using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BogDb.Core.Common;

/// <summary>
/// A dynamically expanding unmanaged memory region to hold string data and variable-length
/// sequence arrays safely without truncation during in-memory query execution.
/// </summary>
public sealed class InMemOverflowBuffer : IDisposable
{
    private const int DefaultBlockSize = 256 * 1024; // 256KB Blocks
    private readonly List<IntPtr> _blocks;
    
    private int _currentBlockIndex = -1;
    private int _currentByteOffset = 0;

    public InMemOverflowBuffer()
    {
        _blocks = new List<IntPtr>();
        AllocateNewBlock();
    }

    private void AllocateNewBlock()
    {
        IntPtr newBlock = Marshal.AllocHGlobal(DefaultBlockSize);
        _blocks.Add(newBlock);
        _currentBlockIndex++;
        _currentByteOffset = 0;
    }

    /// <summary>
    /// Allocates continuous space in the overflow buffer, advancing to a new block if necessary.
    /// </summary>
    public unsafe byte* AllocateSpace(int size)
    {
        if (size > DefaultBlockSize)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Allocation exceeds block limits. Use large block allocator.");
        }

        if (_currentByteOffset + size > DefaultBlockSize)
        {
            AllocateNewBlock();
        }

        byte* ptr = (byte*)_blocks[_currentBlockIndex].ToPointer() + _currentByteOffset;
        _currentByteOffset += size;
        return ptr;
    }

    public void Dispose()
    {
        foreach (var block in _blocks)
        {
            if (block != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(block);
            }
        }
        _blocks.Clear();
    }
}
