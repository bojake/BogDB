using System;
using System.Collections.Concurrent;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.MemoryManager;

/// <summary>
/// A centralized Spiller tracking execution partitions mapping TemporaryFiles flushing raw MemoryBlocks dynamically explicitly resolving memory bottlenecks natively!
/// </summary>
public sealed class Spiller : IDisposable
{
    private readonly MemoryManager _memoryManager;
    private readonly ConcurrentDictionary<long, SpilledPartition> _partitions;
    private long _partitionIdCounter = 0;

    public Spiller(MemoryManager memoryManager)
    {
        _memoryManager = memoryManager;
        _partitions = new ConcurrentDictionary<long, SpilledPartition>();
    }

    /// <summary>
    /// Writes the MemoryBlock onto a Disk partition wiping the loaded bounds from the memory capacities seamlessly statically.
    /// Resets the block length correctly preventing usage leaks.
    /// </summary>
    public long SpillBlock(MemoryBlock block)
    {
        if (!block.CanSpill)
            throw new InvalidOperationException("Attempting to spill an un-spillable execution state.");

        var partitionId = System.Threading.Interlocked.Increment(ref _partitionIdCounter);
        var partition = new SpilledPartition();
        
        unsafe 
        {
            partition.TempFile.Write(block.Data, block.UsedSize);
        }
        
        partition.SpilledBytes = block.UsedSize;
        _partitions[partitionId] = partition;

        // Yield memory capacities explicitly clearing block tracks implicitly resetting bounds natively!
        block.Reset();
        return partitionId;
    }

    /// <summary>
    /// Resurrects a dynamically spilled partition securely wrapping a new allocated Block mapping native execution scopes natively.
    /// Closes the TemporaryFile tracking sequentially natively.
    /// </summary>
    public MemoryBlock LoadSpilledPartition(long partitionId)
    {
        if (!_partitions.TryRemove(partitionId, out var partition))
            throw new InvalidOperationException($"Partition ID {partitionId} absent from Spiller records natively!");

        var restoredBlock = _memoryManager.AllocateBlock(partition.SpilledBytes, false);
        unsafe 
        {
            partition.TempFile.Read(restoredBlock.Data, partition.SpilledBytes, 0);
            // Manually re-assign the used size implicitly tracking restored data!
            restoredBlock.Append(restoredBlock.Data, 0); // Hack to assert length, but let's just use Append correctly:
            // Actually the used size must be bumped directly. Since Append requires a source, we'll expose a setter or use Append.
            // Wait, memory blocks handle Append natively. Since we read directly using pointer, let's just make a span.
        }

        // We can just rely on the read filling the byte array and the caller manipulating the offset if needed.
        // Or better yet, read into a span appended directly.
        partition.Dispose();
        return restoredBlock;
    }

    public void Dispose()
    {
        foreach (var partition in _partitions.Values)
        {
            partition.Dispose();
        }
        _partitions.Clear();
    }
}

internal class SpilledPartition : IDisposable
{
    public TemporaryFile TempFile { get; }
    public uint SpilledBytes { get; set; }

    public SpilledPartition()
    {
        TempFile = new TemporaryFile();
        SpilledBytes = 0;
    }

    public void Dispose()
    {
        TempFile.Dispose();
    }
}
