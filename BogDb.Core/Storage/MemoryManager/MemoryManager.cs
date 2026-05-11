using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BogDb.Core.Storage.MemoryManager;

/// <summary>
/// Controls massive allocation budgets across operator memory scopes (e.g HashJoin tables).
/// Throttles executions spilling temporary artifacts tracking global memory capacities safely.
/// </summary>
public class MemoryManager : IDisposable
{
    private readonly Storage.BufferManager.BufferManager _bufferManager;
    private readonly ConcurrentDictionary<long, MemoryBlock> _activeBlocks;
    private long _blockIdCounter;

    // A limit beyond which allocations MUST evaluate spelling constraints.
    // Pulled directly from BufferManager boundaries dynamically natively.
    private readonly long _memoryLimit;
    private long _usedMemory;

    public MemoryManager(Storage.BufferManager.BufferManager bufferManager)
    {
        _bufferManager = bufferManager;
        _activeBlocks = new ConcurrentDictionary<long, MemoryBlock>();
        _blockIdCounter = 0;
        
        // Match the BufferManager bounds or default to 1GB for mock isolation tests.
        _memoryLimit = bufferManager?.MemoryLimit ?? (1024L * 1024L * 1024L);
        _usedMemory = 0;
    }

    /// <summary>
    /// Allocates an explicitly sized contiguous chunk of unmanaged memory dynamically.
    /// Emits a MemoryBlock reference tracking physical span mappings accurately.
    /// </summary>
    /// <param name="sizeBytes">Capacity of the memory block requested.</param>
    /// <param name="canSpill">Flag identifying whether the requested buffer can gracefully flush if pressured.</param>
    public MemoryBlock AllocateBlock(uint sizeBytes, bool canSpill = true)
    {
        if (!TryReserve(sizeBytes) && !canSpill)
        {
            throw new OutOfMemoryException($"MemoryManager: Failed to allocate {sizeBytes} bytes. Global limit exceeded without spill capacities!");
        }

        var block = new MemoryBlock(sizeBytes, canSpill);
        var id = Interlocked.Increment(ref _blockIdCounter);
        
        _activeBlocks[id] = block;
        return block;
    }

    public void FreeBlock(MemoryBlock block)
    {
        long blockKey = -1;
        foreach (var kvp in _activeBlocks)
        {
            if (ReferenceEquals(kvp.Value, block))
            {
                blockKey = kvp.Key;
                break;
            }
        }
        
        if (blockKey != -1 && _activeBlocks.TryRemove(blockKey, out var removedBlock))
        {
            Interlocked.Add(ref _usedMemory, -(long)removedBlock.Size);
            removedBlock.Dispose();
        }
    }

    private bool TryReserve(uint sizeBytes)
    {
        // Try locally tracking the pool internally scaling out toward the explicit BufferManager bounds.
        // If we exceed capacity, we must command the BufferManager to attempt evictions mapping Page bounds explicitly.
        
        if (Interlocked.Read(ref _usedMemory) + sizeBytes > _memoryLimit)
        {
            // Push eviction sweep natively flushing least-recently-used bounds implicitly scaling constraints.
            _bufferManager?.EvictPages();
            
            if (Interlocked.Read(ref _usedMemory) + sizeBytes > _memoryLimit)
            {
                return false;
            }
        }
        
        Interlocked.Add(ref _usedMemory, sizeBytes);
        return true;
    }

    public void Dispose()
    {
        foreach (var block in _activeBlocks.Values)
        {
            block.Dispose();
        }
        _activeBlocks.Clear();
    }
}
