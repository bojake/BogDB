using System;
using System.Runtime.InteropServices;

namespace BogDb.Core.Storage.MemoryManager;

/// <summary>
/// A contiguous block of unmanaged memory allocated explicitly by the MemoryManager.
/// Serves as the base representation for Hash table bounds or Sort arrays tracking
/// explicit physical buffer lifecycles gracefully preventing C# GC pressure overheads.
/// </summary>
public unsafe class MemoryBlock : IDisposable
{
    public uint Size { get; }
    public byte* Data { get; private set; }
    
    // Internal tracker defining whether THIS block can be swapped/spilled natively
    public bool CanSpill { get; }
    
    // Tracks the usage pointer within the block
    public uint UsedSize { get; private set; }

    public MemoryBlock(uint size, bool canSpill = true)
    {
        Size = size;
        CanSpill = canSpill;
        Data = (byte*)NativeMemory.AllocZeroed((nuint)size);
        UsedSize = 0;
    }

    /// <summary>
    /// Appends raw bytes bumping the internal pointer safely. Returns true if successful.
    /// </summary>
    public bool Append(byte* source, uint length)
    {
        if (UsedSize + length > Size)
            return false;
            
        Buffer.MemoryCopy(source, Data + UsedSize, Size - UsedSize, length);
        UsedSize += length;
        return true;
    }

    /// <summary>
    /// Directly re-assigns a managed buffer onto the unmanaged scope bypassing Buffer.MemoryCopy loop constraints.
    /// </summary>
    public bool AppendSpan(ReadOnlySpan<byte> data)
    {
        if (UsedSize + data.Length > Size)
            return false;
            
        data.CopyTo(new Span<byte>(Data + UsedSize, data.Length));
        UsedSize += (uint)data.Length;
        return true;
    }

    public void Reset()
    {
        UsedSize = 0;
    }

    public void Dispose()
    {
        if (Data != null)
        {
            NativeMemory.Free(Data);
            Data = null;
        }
    }
}
