using System.Diagnostics;
using System.Threading;

namespace BogDb.Core.Common.Profiler;

/// <summary>
/// Execution Profiler designed to wrap pipeline iterations tracking real-time durations safely natively.
/// Mimics BogDb C++ 'TimeMetric' tracking structures.
/// </summary>
public sealed class Profiler
{
    private readonly Stopwatch _stopwatch;
    private long _elapsedMilliseconds;

    public Profiler()
    {
        _stopwatch = new Stopwatch();
        _elapsedMilliseconds = 0;
    }

    public void Start()
    {
        _stopwatch.Start();
    }

    public void Stop()
    {
        _stopwatch.Stop();
        Interlocked.Add(ref _elapsedMilliseconds, _stopwatch.ElapsedMilliseconds);
        _stopwatch.Reset();
    }

    public double GetElapsedTimeMs()
    {
        return Interlocked.Read(ref _elapsedMilliseconds);
    }
}
