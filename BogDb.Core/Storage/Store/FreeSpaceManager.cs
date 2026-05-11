using System;
using System.Collections.Generic;
using System.Linq;

namespace BogDb.Core.Storage.Store
{
    public struct PageRange : IComparable<PageRange>
    {
        public ulong StartPageIdx;
        public ulong NumPages;

        public PageRange(ulong startPageIdx, ulong numPages)
        {
            StartPageIdx = startPageIdx;
            NumPages = numPages;
        }

        public int CompareTo(PageRange other)
        {
            if (NumPages == other.NumPages)
            {
                return StartPageIdx.CompareTo(other.StartPageIdx);
            }
            return NumPages.CompareTo(other.NumPages);
        }
    }

    public class FreeSpaceManager
    {
        private List<SortedSet<PageRange>> _freeLists;
        private List<PageRange> _uncheckpointedFreePageRanges;
        private ulong _numEntries;
        private bool _needClearEvictedEntries;

        public FreeSpaceManager()
        {
            _freeLists = new List<SortedSet<PageRange>>();
            _uncheckpointedFreePageRanges = new List<PageRange>();
            _numEntries = 0;
            _needClearEvictedEntries = false;
        }

        private SortedSet<PageRange> GetFreeList(int level)
        {
            while (level >= _freeLists.Count)
            {
                _freeLists.Add(new SortedSet<PageRange>());
            }
            return _freeLists[level];
        }

        public static int GetLevel(ulong numPages)
        {
            if (numPages == 0) return 0;
            
            // Port of `CountZeros<common::page_idx_t>::Trailing(std::bit_floor(numPages))`
            // Essentially log2 of the largest power of 2 that is <= numPages
            ulong bitFloor = 1UL << (63 - System.Numerics.BitOperations.LeadingZeroCount(numPages));
            return System.Numerics.BitOperations.TrailingZeroCount(bitFloor);
        }

        public void AddFreePages(PageRange entry)
        {
            int entryLevel = GetLevel(entry.NumPages);
            var freeList = GetFreeList(entryLevel);
            
            if (!freeList.Contains(entry))
            {
                freeList.Add(entry);
                _numEntries++;
            }
        }

        public void AddUncheckpointedFreePages(PageRange entry)
        {
            _uncheckpointedFreePageRanges.Add(entry);
        }

        public void RollbackCheckpoint()
        {
            _uncheckpointedFreePageRanges.Clear();
        }

        public PageRange? PopFreePages(ulong numPages)
        {
            if (numPages > 0)
            {
                int levelToSearch = GetLevel(numPages);
                for (; levelToSearch < _freeLists.Count; ++levelToSearch)
                {
                    var curList = _freeLists[levelToSearch];
                    // Find first element >= {0, numPages}
                    var searchDummy = new PageRange(0, numPages);
                    
                    // In a C# SortedSet, we use GetViewBetween to emulate lower_bound
                    var view = curList.GetViewBetween(searchDummy, new PageRange(ulong.MaxValue, ulong.MaxValue));
                    var entryIt = view.FirstOrDefault();
                    
                    if (entryIt.NumPages >= numPages)
                    {
                        var entry = entryIt;
                        curList.Remove(entry);
                        _numEntries--;
                        return SplitPageRange(entry, numPages);
                    }
                }
            }
            return null;
        }

        private PageRange SplitPageRange(PageRange chunk, ulong numRequiredPages)
        {
            PageRange ret = new PageRange(chunk.StartPageIdx, numRequiredPages);
            if (numRequiredPages < chunk.NumPages)
            {
                PageRange remainingEntry = new PageRange(
                    chunk.StartPageIdx + numRequiredPages,
                    chunk.NumPages - numRequiredPages
                );
                AddFreePages(remainingEntry);
            }
            return ret;
        }

        private void ResetFreeLists()
        {
            _freeLists.Clear();
            _numEntries = 0;
        }

        public void MergePageRanges(List<PageRange> newInitialEntries)
        {
            var allEntries = new List<PageRange>(newInitialEntries);
            foreach (var freeList in _freeLists)
            {
                allEntries.AddRange(freeList);
            }

            if (allEntries.Count == 0) return;

            ResetFreeLists();
            
            // Sort by StartPageIdx
            allEntries.Sort((a, b) => a.StartPageIdx.CompareTo(b.StartPageIdx));

            PageRange prevEntry = allEntries[0];
            for (int i = 1; i < allEntries.Count; ++i)
            {
                var entry = allEntries[i];
                if (prevEntry.StartPageIdx + prevEntry.NumPages == entry.StartPageIdx)
                {
                    prevEntry.NumPages += entry.NumPages;
                }
                else
                {
                    AddFreePages(prevEntry);
                    prevEntry = entry;
                }
            }
            
            // Notice: C++ handleLastPageRange checks file size bounds. 
            // In C# we'll simply add the last free span until FileHandle integration.
            AddFreePages(prevEntry);
        }

        public void FinalizeCheckpoint()
        {
            MergePageRanges(_uncheckpointedFreePageRanges);
            _uncheckpointedFreePageRanges.Clear();
        }

        public ulong GetNumEntries() => _numEntries;

        public bool HasUncheckpointedChanges()
        {
            return _uncheckpointedFreePageRanges.Count > 0;
        }
    }
}
