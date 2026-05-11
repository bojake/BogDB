using System;
using System.Diagnostics;
using BogDb.Core.Main.QueryResult;

namespace BogDb.Core.Common.Diagnostics;

internal sealed class QueryMetricsContext
{
    private readonly Stopwatch _stopwatch = new();
    private readonly BogDbQueryMetricsOptions _options;
    private bool _stopped;
    private long _bytesRead;
    private long _bytesWritten;
    private long _tempBytesWritten;
    private int _spillCount;
    private int _spillRunCount;
    private int _maxMergeFanIn;
    private int _nodeReads;
    private int _relReads;
    private int _indexLookups;
    private int _nodesWritten;
    private int _relationshipsWritten;

    public QueryMetricsContext(BogDbQueryMetricsOptions? options = null)
    {
        _options = options ?? new BogDbQueryMetricsOptions();
    }

    public void Start()
    {
        _stopped = false;
        _stopwatch.Restart();
    }

    public double Stop()
    {
        if (!_stopped)
        {
            _stopwatch.Stop();
            _stopped = true;
        }

        return _stopwatch.Elapsed.TotalMilliseconds;
    }

    public double GetElapsedMilliseconds()
        => _stopwatch.Elapsed.TotalMilliseconds;

    public void AddBytesRead(long bytes)
        => _bytesRead += bytes;

    public void AddBytesWritten(long bytes)
        => _bytesWritten += bytes;

    public void AddTempBytesWritten(long bytes)
        => _tempBytesWritten += bytes;

    public void IncrementSpillCount()
        => _spillCount++;

    public void AddSpillRuns(int count)
        => _spillRunCount += count;

    public void ObserveMergeFanIn(int fanIn)
        => _maxMergeFanIn = Math.Max(_maxMergeFanIn, fanIn);

    public void AddNodeReads(int count)
        => _nodeReads += count;

    public void AddRelReads(int count)
        => _relReads += count;

    public void AddIndexLookups(int count)
        => _indexLookups += count;

    public void AddNodesWritten(int count)
        => _nodesWritten += count;

    public void AddRelationshipsWritten(int count)
        => _relationshipsWritten += count;

    public BogDbQueryExecutionMetrics? BuildExecutionMetrics(ulong rowCount)
    {
        if (!_options.CaptureExecutionMetrics)
        {
            return null;
        }

        return new BogDbQueryExecutionMetrics
        {
            TotalMilliseconds = GetElapsedMilliseconds(),
            RowsProduced = rowCount,
            BytesRead = _bytesRead,
            BytesWritten = _bytesWritten,
            TempBytesWritten = _tempBytesWritten,
            SpillCount = _spillCount,
            SpillRunCount = _spillRunCount,
            MaxMergeFanIn = _maxMergeFanIn,
            NodeReads = _nodeReads,
            RelReads = _relReads,
            IndexLookups = _indexLookups,
            NodesWritten = _nodesWritten,
            RelationshipsWritten = _relationshipsWritten
        };
    }
}
