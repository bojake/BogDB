using System.Threading;
using BogDb.Core.Main.QueryResult;

namespace BogDb.Core.Common.Diagnostics;

internal sealed class DatabaseMetricsRegistry
{
    private long _queryCount;
    private long _failedQueryCount;
    private long _transactionCommitCount;
    private long _transactionRollbackCount;
    private long _bytesRead;
    private long _bytesWritten;
    private long _tempBytesWritten;
    private long _nodeReadCount;
    private long _relReadCount;
    private long _indexLookupCount;
    private long _nodesWritten;
    private long _relationshipsWritten;
    private long _extractionCount;
    private long _totalQueryTicks;
    private long _totalExtractionTicks;

    public void RecordQuery(bool success, double totalMilliseconds, BogDbQueryExecutionMetrics? executionMetrics = null)
    {
        Interlocked.Increment(ref _queryCount);
        if (!success)
        {
            Interlocked.Increment(ref _failedQueryCount);
        }

        Interlocked.Add(ref _totalQueryTicks, ToScaledTicks(totalMilliseconds));

        if (executionMetrics == null)
        {
            return;
        }

        AddBytesRead(executionMetrics.BytesRead);
        AddBytesWritten(executionMetrics.BytesWritten);
        AddTempBytesWritten(executionMetrics.TempBytesWritten);
        AddNodeReads(executionMetrics.NodeReads);
        AddRelReads(executionMetrics.RelReads);
        AddIndexLookups(executionMetrics.IndexLookups);
        AddNodesWritten(executionMetrics.NodesWritten);
        AddRelationshipsWritten(executionMetrics.RelationshipsWritten);
    }

    public void RecordCommit()
        => Interlocked.Increment(ref _transactionCommitCount);

    public void RecordRollback()
        => Interlocked.Increment(ref _transactionRollbackCount);

    public void AddBytesRead(long bytes)
        => Interlocked.Add(ref _bytesRead, bytes);

    public void AddBytesWritten(long bytes)
        => Interlocked.Add(ref _bytesWritten, bytes);

    public void AddTempBytesWritten(long bytes)
        => Interlocked.Add(ref _tempBytesWritten, bytes);

    public void AddNodeReads(long count)
        => Interlocked.Add(ref _nodeReadCount, count);

    public void AddRelReads(long count)
        => Interlocked.Add(ref _relReadCount, count);

    public void AddIndexLookups(long count)
        => Interlocked.Add(ref _indexLookupCount, count);

    public void AddNodesWritten(long count)
        => Interlocked.Add(ref _nodesWritten, count);

    public void AddRelationshipsWritten(long count)
        => Interlocked.Add(ref _relationshipsWritten, count);

    public void RecordExtraction(double totalMilliseconds)
    {
        Interlocked.Increment(ref _extractionCount);
        Interlocked.Add(ref _totalExtractionTicks, ToScaledTicks(totalMilliseconds));
    }

    public BogDatabaseMetricsSnapshot Snapshot()
    {
        return new BogDatabaseMetricsSnapshot
        {
            QueryCount = Interlocked.Read(ref _queryCount),
            FailedQueryCount = Interlocked.Read(ref _failedQueryCount),
            TransactionCommitCount = Interlocked.Read(ref _transactionCommitCount),
            TransactionRollbackCount = Interlocked.Read(ref _transactionRollbackCount),
            BytesRead = Interlocked.Read(ref _bytesRead),
            BytesWritten = Interlocked.Read(ref _bytesWritten),
            TempBytesWritten = Interlocked.Read(ref _tempBytesWritten),
            NodeReadCount = Interlocked.Read(ref _nodeReadCount),
            RelReadCount = Interlocked.Read(ref _relReadCount),
            IndexLookupCount = Interlocked.Read(ref _indexLookupCount),
            NodesWritten = Interlocked.Read(ref _nodesWritten),
            RelationshipsWritten = Interlocked.Read(ref _relationshipsWritten),
            ExtractionCount = Interlocked.Read(ref _extractionCount),
            TotalQueryMilliseconds = FromScaledTicks(Interlocked.Read(ref _totalQueryTicks)),
            TotalExtractionMilliseconds = FromScaledTicks(Interlocked.Read(ref _totalExtractionTicks))
        };
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _queryCount, 0);
        Interlocked.Exchange(ref _failedQueryCount, 0);
        Interlocked.Exchange(ref _transactionCommitCount, 0);
        Interlocked.Exchange(ref _transactionRollbackCount, 0);
        Interlocked.Exchange(ref _bytesRead, 0);
        Interlocked.Exchange(ref _bytesWritten, 0);
        Interlocked.Exchange(ref _tempBytesWritten, 0);
        Interlocked.Exchange(ref _nodeReadCount, 0);
        Interlocked.Exchange(ref _relReadCount, 0);
        Interlocked.Exchange(ref _indexLookupCount, 0);
        Interlocked.Exchange(ref _nodesWritten, 0);
        Interlocked.Exchange(ref _relationshipsWritten, 0);
        Interlocked.Exchange(ref _extractionCount, 0);
        Interlocked.Exchange(ref _totalQueryTicks, 0);
        Interlocked.Exchange(ref _totalExtractionTicks, 0);
    }

    private static long ToScaledTicks(double milliseconds)
        => (long)(milliseconds * 1000d);

    private static double FromScaledTicks(long scaledTicks)
        => scaledTicks / 1000d;
}
