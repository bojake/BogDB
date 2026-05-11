using BogDb.Core.Storage.BufferManager;
using System.Threading.Tasks;
using Xunit;

namespace BogDb.Tests.Storage.BufferManager;

public class EvictionQueueTests
{
    [Fact]
    public void EvictionQueue_ShouldInitializeWithCorrectCapacity()
    {
        var queue = new EvictionQueue(100);
        // It should round up to the nearest multiple of EvictionQueue.BATCH_SIZE (64)
        Assert.Equal(128, queue.Capacity);
        Assert.Equal(0, queue.Size);
    }

    [Fact]
    public void EvictionQueue_BasicInsertAndClear()
    {
        var queue = new EvictionQueue(64);

        // Insert should succeed
        Assert.True(queue.Insert(1, 42));
        Assert.Equal(1, queue.Size);

        // Read batch 
        var batch = queue.Next();
        Assert.Equal(EvictionQueue.BATCH_SIZE, batch.Length);
        
        // Find the inserted candidate
        var candidate = batch[0]; // First insertion should be at index 0
        Assert.NotEqual(EvictionCandidate.EMPTY, candidate);
        Assert.Equal(1u, candidate.FileIdx);
        Assert.Equal(42u, candidate.PageIdx);

        // Clear it
        queue.ClearFromIndex(0); // For the test, we know index 0 is where the first insert lands
        Assert.Equal(0, queue.Size);
        Assert.Equal(EvictionCandidate.EMPTY, batch[0]);
    }

    [Fact]
    public async Task EvictionQueue_ConcurrentInsertions()
    {
        int capacity = 1000;
        var queue = new EvictionQueue(capacity); // Becomes 1024 capacity

        // Spawn 1000 tasks trying to insert concurrently
        var tasks = new Task[1000];
        for (int i = 0; i < 1000; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                queue.Insert(0, (uint)index);
            });
        }

        await Task.WhenAll(tasks);

        // We expect exactly 1000 items in the queue, no data races
        Assert.Equal(1000, queue.Size);
    }
}
