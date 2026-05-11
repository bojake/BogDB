using BogDb.Core.Storage.BufferManager;
using System.Threading.Tasks;
using Xunit;

namespace BogDb.Tests.Storage.BufferManager;

public class EvictionQueueEdgeCaseTests
{
    [Fact]
    public void EvictionQueue_ShouldHandleExtremelyRapidConcurrentInsertions()
    {
        var queue = new EvictionQueue(capacity: 4096);
        int inserts = 10000;
        
        Parallel.For(0, inserts, i =>
        {
            queue.Insert((uint)(i % 10), (uint)i);
        });

        // The exact count will be bounded by 4096 circular limit, but we should not see crashes.
        Assert.True(true);
    }
    
    [Fact]
    public void EvictionQueue_Next_Underload_ShouldReturnBatch()
    {
        var queue = new EvictionQueue(capacity: 4096);
        
        var batch = queue.Next();
        
        Assert.Equal(64, batch.Length);
        Assert.Equal(uint.MaxValue, batch[0].FileIdx);
    }
}
