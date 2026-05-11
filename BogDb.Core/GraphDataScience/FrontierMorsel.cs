using System;
using System.Collections.Generic;
using System.Threading;

namespace BogDb.Core.GraphDataScience;

/// <summary>
/// A range of node offsets assigned as a single unit of work to one parallel worker.
/// C++ parity: <c>src/include/function/gds/frontier_morsel.h</c> — <c>FrontierMorsel</c>.
/// </summary>
public readonly struct FrontierMorsel
{
    /// <summary>First node offset in this morsel (inclusive).</summary>
    public readonly ulong BeginOffset;
    /// <summary>One past the last node offset in this morsel (exclusive).</summary>
    public readonly ulong EndOffset;
    /// <summary>Number of nodes in this morsel.</summary>
    public ulong Size => EndOffset - BeginOffset;

    public FrontierMorsel(ulong beginOffset, ulong endOffset)
    {
        BeginOffset = beginOffset;
        EndOffset   = endOffset;
    }

    public bool IsValid => EndOffset > BeginOffset;

    public override string ToString() => $"[{BeginOffset}, {EndOffset})";
}

/// <summary>
/// Atomically dispatches contiguous ranges of node offsets to competing worker threads.
/// Workers call <see cref="TryGetNext"/> in a tight loop until it returns false.
///
/// C++ parity: <c>FrontierMorselDispatcher</c> in <c>frontier_morsel.h</c>.
///   — MIN_FRONTIER_MORSEL_SIZE = 512 nodes
///   — MIN_NUMBER_OF_FRONTIER_MORSELS = 128 morsels aimed-for
/// </summary>
public sealed class FrontierMorselDispatcher
{
    // Mirror C++ constants
    public const ulong MinMorselSize      = 512;   // minimum nodes per morsel
    public const ulong MinMorselCount     = 128;   // target number of morsels

    private readonly ulong   _maxOffset;     // exclusive upper bound
    private readonly ulong   _morselSize;    // nodes per morsel
    private long             _nextOffset;    // atomic cursor (stored as long for Interlocked)

    /// <param name="maxOffset">Exclusive upper bound (= graph.NodeCount).</param>
    /// <param name="maxThreads">Number of parallel workers.</param>
    public FrontierMorselDispatcher(ulong maxOffset, int maxThreads)
    {
        _maxOffset  = maxOffset;
        _nextOffset = 0;

        if (maxOffset == 0)
        {
            _morselSize = MinMorselSize;
            return;
        }

        // Compute morsel size: aim for MinMorselCount morsels, but never smaller than MinMorselSize
        var idealSize = (ulong)Math.Ceiling((double)maxOffset / MinMorselCount);
        _morselSize = Math.Max(idealSize, MinMorselSize);
    }

    /// <summary>Resets the dispatcher so worker threads can consume offsets from the start again.</summary>
    public void Reset() => Interlocked.Exchange(ref _nextOffset, 0);

    /// <summary>
    /// Atomically claims the next morsel. Returns false when all offsets are exhausted.
    /// Safe to call concurrently from multiple threads.
    /// </summary>
    public bool TryGetNext(out FrontierMorsel morsel)
    {
        while (true)
        {
            long cur  = Interlocked.Read(ref _nextOffset);
            if ((ulong)cur >= _maxOffset)
            {
                morsel = default;
                return false;
            }

            ulong begin = (ulong)cur;
            ulong end   = Math.Min(begin + _morselSize, _maxOffset);

            // CAS: claim [begin, end) by moving cursor to end
            if (Interlocked.CompareExchange(ref _nextOffset, (long)end, cur) == cur)
            {
                morsel = new FrontierMorsel(begin, end);
                return true;
            }
            // Another thread got there first — retry
        }
    }

    /// <summary>Total number of morsels this dispatcher will produce.</summary>
    public ulong TotalMorsels => _maxOffset == 0
        ? 0
        : (ulong)Math.Ceiling((double)_maxOffset / _morselSize);
}

/// <summary>
/// Collects per-morsel results and merges them into a single output dictionary.
/// Each parallel worker writes to its own <see cref="MorselResult"/> to avoid contention.
/// </summary>
public sealed class MorselResultAccumulator
{
    private readonly object _lock = new();
    private readonly Dictionary<NodeId, Dictionary<string, object?>> _merged = new();

    /// <summary>Merges one worker's results into the shared accumulator.</summary>
    public void Merge(Dictionary<NodeId, Dictionary<string, object?>> workerResults)
    {
        lock (_lock)
        {
            foreach (var (k, v) in workerResults)
                _merged[k] = v;
        }
    }

    /// <summary>Returns the final merged result table.</summary>
    public Dictionary<NodeId, Dictionary<string, object?>> GetMerged() => _merged;
}
