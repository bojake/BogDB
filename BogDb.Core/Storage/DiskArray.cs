using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BogDb.Core.Common;

namespace BogDb.Core.Storage
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DiskArrayHeader
    {
        public ulong NumElements;
        public uint FirstPIPPageIdx; // Common.page_idx_t
        public uint Padding;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct PIP
    {
        public const int NUM_PAGE_IDXS_PER_PIP = ((int)BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE - sizeof(uint)) / sizeof(uint);

        public uint NextPipPageIdx; // Common.page_idx_t
        public fixed uint PageIdxs[NUM_PAGE_IDXS_PER_PIP];
    }

    public struct PIPWrapper
    {
        public uint PipPageIdx;
        public PIP PipContents;

        public unsafe PIPWrapper(FileHandle fileHandle, uint pipPageIdx)
        {
            PipPageIdx = pipPageIdx;
            PipContents = new PIP();
            // Read 4KB frame from disk directly into PipContents
            var span = new Span<PIP>(Unsafe.AsPointer(ref PipContents), 1);
            fileHandle.ReadPage(pipPageIdx, MemoryMarshal.Cast<PIP, byte>(span));
        }

        public PIPWrapper(uint pipPageIdx)
        {
            PipPageIdx = pipPageIdx;
            PipContents = new PIP();
            PipContents.NextPipPageIdx = BogDb.Core.Common.Constants.INVALID_PAGE_IDX;
            unsafe
            {
                for (int i = 0; i < PIP.NUM_PAGE_IDXS_PER_PIP; i++)
                {
                    PipContents.PageIdxs[i] = BogDb.Core.Common.Constants.INVALID_PAGE_IDX;
                }
            }
        }
    }

    public struct PageStorageInfo
    {
        public uint AlignedElementSize { get; }
        public uint NumElementsPerPage { get; }

        public PageStorageInfo(uint elementSize)
        {
            AlignedElementSize = (uint)BitOperations.RoundUpToPowerOf2(elementSize);
            NumElementsPerPage = (uint)(BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE / AlignedElementSize);
        }
    }

    public struct PIPUpdates
    {
        public PIPWrapper? UpdatedLastPIP;
        public List<PIPWrapper> NewPIPs;

        public void Clear()
        {
            UpdatedLastPIP = null;
            NewPIPs.Clear();
        }
    }

    public class DiskArray<T> where T : unmanaged
    {
        private readonly FileHandle _fileHandle;
        private DiskArrayHeader _headerForReadTrx;
        private DiskArrayHeader _headerForWriteTrx;
        private readonly PageStorageInfo _storageInfo;
        
        private readonly List<PIPWrapper> _pips;
        private PIPUpdates _pipUpdates;
        private bool _hasTransactionalUpdates;

        public DiskArray(FileHandle fileHandle, DiskArrayHeader headerForReadTrx, DiskArrayHeader headerForWriteTrx)
        {
            _fileHandle = fileHandle;
            _headerForReadTrx = headerForReadTrx;
            _headerForWriteTrx = headerForWriteTrx;
            _storageInfo = new PageStorageInfo((uint)Unsafe.SizeOf<T>());
            
            _pips = new List<PIPWrapper>();
            _pipUpdates = new PIPUpdates { NewPIPs = new List<PIPWrapper>() };

            if (_headerForReadTrx.FirstPIPPageIdx != BogDb.Core.Common.Constants.INVALID_PAGE_IDX)
            {
                var pip = new PIPWrapper(_fileHandle, _headerForReadTrx.FirstPIPPageIdx);
                _pips.Add(pip);
                while (pip.PipContents.NextPipPageIdx != BogDb.Core.Common.Constants.INVALID_PAGE_IDX)
                {
                    pip = new PIPWrapper(_fileHandle, pip.PipContents.NextPipPageIdx);
                    _pips.Add(pip);
                }
            }
        }

        private uint GetAPPageIdxNoLock(uint apIdx)
        {
            uint pipIdx = apIdx / (uint)PIP.NUM_PAGE_IDXS_PER_PIP;
            uint offsetInPIP = apIdx % (uint)PIP.NUM_PAGE_IDXS_PER_PIP;

            if (!HasPIPUpdatesNoLock(pipIdx))
            {
                var pip = _pips[(int)pipIdx];
                unsafe { return pip.PipContents.PageIdxs[offsetInPIP]; }
            }
            else if (pipIdx == _pips.Count - 1 && _pipUpdates.UpdatedLastPIP.HasValue)
            {
                var pip = _pipUpdates.UpdatedLastPIP.Value;
                unsafe { return pip.PipContents.PageIdxs[offsetInPIP]; }
            }
            else
            {
                var pip = _pipUpdates.NewPIPs[(int)(pipIdx - _pips.Count)];
                unsafe { return pip.PipContents.PageIdxs[offsetInPIP]; }
            }
        }

        private bool HasPIPUpdatesNoLock(uint pipIdx)
        {
            if (pipIdx >= _pips.Count) return true;
            return (pipIdx == _pips.Count - 1) && _pipUpdates.UpdatedLastPIP.HasValue;
        }

        public T Get(ulong idx)
        {
            if (idx >= _headerForReadTrx.NumElements && !(_hasTransactionalUpdates && idx < _headerForWriteTrx.NumElements))
                throw new IndexOutOfRangeException($"Index {idx} is out of bounds for DiskArray. Read Header: {_headerForReadTrx.NumElements}, Write Header: {_headerForWriteTrx.NumElements}, Has Updates: {_hasTransactionalUpdates}");

            uint apIdx = (uint)(idx / _storageInfo.NumElementsPerPage);
            uint byteOffsetInAP = (uint)(idx % _storageInfo.NumElementsPerPage) * _storageInfo.AlignedElementSize;

            uint apPageIdx = GetAPPageIdxNoLock(apIdx);

            // In BogDB, we read directly via the memory mapped file handle since we skip shadow files for now
            T result = default;
            byte[] pageBuffer = new byte[BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE];
            _fileHandle.ReadPage(apPageIdx, pageBuffer);

            unsafe
            {
                fixed (byte* ptr = &pageBuffer[byteOffsetInAP])
                {
                    result = *(T*)ptr;
                }
            }
            return result;
        }

        public void Update(ulong idx, T val)
        {
            if (idx >= _headerForWriteTrx.NumElements)
                throw new IndexOutOfRangeException($"Index {idx} is out of bounds for DiskArray update.");

            _hasTransactionalUpdates = true;
            uint apIdx = (uint)(idx / _storageInfo.NumElementsPerPage);
            uint byteOffsetInAP = (uint)(idx % _storageInfo.NumElementsPerPage) * _storageInfo.AlignedElementSize;

            uint apPageIdx = GetAPPageIdxNoLock(apIdx);
            
            byte[] pageBuffer = new byte[BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE];
            _fileHandle.ReadPage(apPageIdx, pageBuffer);

            unsafe
            {
                fixed (byte* ptr = &pageBuffer[byteOffsetInAP])
                {
                    *(T*)ptr = val;
                }
            }

            _fileHandle.WritePage(apPageIdx, pageBuffer);
        }

        public uint GetNumElements()
        {
            return (uint)_headerForWriteTrx.NumElements;
        }

        public DiskArrayHeader GetCurrentHeader()
        {
            return _headerForWriteTrx;
        }

        public void Resize(uint newNumElements, T defaultVal = default)
        {
            var originalNumElements = _headerForWriteTrx.NumElements;
            while (_headerForWriteTrx.NumElements < newNumElements)
            {
                PushBack(defaultVal);
            }
        }

        public void PushBack(T val)
        {
            _hasTransactionalUpdates = true;
            ulong idx = _headerForWriteTrx.NumElements;
            uint apIdx = (uint)(idx / _storageInfo.NumElementsPerPage);
            uint byteOffsetInAP = (uint)(idx % _storageInfo.NumElementsPerPage) * _storageInfo.AlignedElementSize;

            uint apPageIdx = GetAPPageIdxAndAddAPToPIPIfNecessary(apIdx);
            
            _headerForWriteTrx.NumElements++;

            byte[] pageBuffer = new byte[BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE];
            if (apIdx * _storageInfo.NumElementsPerPage != idx)
            {
                // Only read if we are appending to an existing page
                _fileHandle.ReadPage(apPageIdx, pageBuffer);
            }

            unsafe
            {
                fixed (byte* ptr = &pageBuffer[byteOffsetInAP])
                {
                    *(T*)ptr = val;
                }
            }

            _fileHandle.WritePage(apPageIdx, pageBuffer);
        }

        private uint GetNumAPs(DiskArrayHeader header)
        {
            return (uint)((header.NumElements + _storageInfo.NumElementsPerPage - 1) / _storageInfo.NumElementsPerPage);
        }

        private uint GetAPPageIdxAndAddAPToPIPIfNecessary(uint apIdx)
        {
            uint numAPs = GetNumAPs(_headerForWriteTrx);
            if (apIdx < numAPs)
            {
                return GetAPPageIdxNoLock(apIdx);
            }

            // Need a new array page
            uint newAPPageIdx = _fileHandle.NumPages; // Simulated allocation
            _fileHandle.AddNewPage();

            uint pipIdx = apIdx / (uint)PIP.NUM_PAGE_IDXS_PER_PIP;
            uint offsetOfNewAPInPIP = apIdx % (uint)PIP.NUM_PAGE_IDXS_PER_PIP;

            if (pipIdx < _pips.Count)
            {
                if (!_pipUpdates.UpdatedLastPIP.HasValue)
                {
                    _pipUpdates.UpdatedLastPIP = _pips[(int)pipIdx];
                }
                var pip = _pipUpdates.UpdatedLastPIP.Value;
                unsafe { pip.PipContents.PageIdxs[offsetOfNewAPInPIP] = newAPPageIdx; }
                _pipUpdates.UpdatedLastPIP = pip;
            }
            else if ((pipIdx - _pips.Count) < _pipUpdates.NewPIPs.Count)
            {
                var pip = _pipUpdates.NewPIPs[(int)(pipIdx - _pips.Count)];
                unsafe { pip.PipContents.PageIdxs[offsetOfNewAPInPIP] = newAPPageIdx; }
                _pipUpdates.NewPIPs[(int)(pipIdx - _pips.Count)] = pip;
            }
            else
            {
                uint pipPageIdx = _fileHandle.NumPages;
                _fileHandle.AddNewPage();
                
                var newPip = new PIPWrapper(pipPageIdx);
                unsafe { newPip.PipContents.PageIdxs[offsetOfNewAPInPIP] = newAPPageIdx; }
                _pipUpdates.NewPIPs.Add(newPip);

                SetNextPIPPageIDxOfPIPNoLock(pipIdx - 1, pipPageIdx);
            }

            return newAPPageIdx;
        }

        private void SetNextPIPPageIDxOfPIPNoLock(uint pipIdxOfPreviousPIP, uint nextPIPPageIdx)
        {
            if (pipIdxOfPreviousPIP == uint.MaxValue)
            {
                _headerForWriteTrx.FirstPIPPageIdx = nextPIPPageIdx;
            }
            else if (_pips.Count == 0)
            {
                var pip = _pipUpdates.NewPIPs[(int)pipIdxOfPreviousPIP];
                pip.PipContents.NextPipPageIdx = nextPIPPageIdx;
                _pipUpdates.NewPIPs[(int)pipIdxOfPreviousPIP] = pip;
            }
            else
            {
                if (!_pipUpdates.UpdatedLastPIP.HasValue)
                {
                    _pipUpdates.UpdatedLastPIP = _pips[(int)pipIdxOfPreviousPIP];
                }
                
                if (pipIdxOfPreviousPIP == _pips.Count - 1)
                {
                    var pip = _pipUpdates.UpdatedLastPIP.Value;
                    pip.PipContents.NextPipPageIdx = nextPIPPageIdx;
                    _pipUpdates.UpdatedLastPIP = pip;
                }
                else
                {
                    var pip = _pipUpdates.NewPIPs[(int)(pipIdxOfPreviousPIP - _pips.Count)];
                    pip.PipContents.NextPipPageIdx = nextPIPPageIdx;
                    _pipUpdates.NewPIPs[(int)(pipIdxOfPreviousPIP - _pips.Count)] = pip;
                }
            }
        }

        public void CheckpointInMemoryIfNecessary()
        {
            if (!_hasTransactionalUpdates) return;

            if (_pipUpdates.UpdatedLastPIP.HasValue)
            {
                _pips[_pips.Count - 1] = _pipUpdates.UpdatedLastPIP.Value;
            }

            foreach (var newPIP in _pipUpdates.NewPIPs)
            {
                _pips.Add(newPIP);
            }

            _pipUpdates.Clear();
            _headerForReadTrx = _headerForWriteTrx;
            _hasTransactionalUpdates = false;
        }

        public void Checkpoint()
        {
            if (_pipUpdates.UpdatedLastPIP.HasValue)
            {
                var pip = _pipUpdates.UpdatedLastPIP.Value;
                byte[] buffer = new byte[BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE];
                unsafe
                {
                    fixed (byte* ptr = buffer)
                    {
                        Buffer.MemoryCopy(&pip.PipContents, ptr, (long)BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE, Unsafe.SizeOf<PIP>());
                    }
                }
                _fileHandle.WritePage(pip.PipPageIdx, buffer);
            }

            foreach (var newPIP in _pipUpdates.NewPIPs)
            {
                byte[] buffer = new byte[BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE];
                unsafe
                {
                    fixed (byte* ptr = buffer)
                    {
                        Buffer.MemoryCopy(&newPIP.PipContents, ptr, (long)BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE, Unsafe.SizeOf<PIP>());
                    }
                }
                _fileHandle.WritePage(newPIP.PipPageIdx, buffer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct HeaderPage
    {
        public const int NUM_HEADERS_PER_PAGE = (4096 - (sizeof(uint) * 2)) / 16; // 16 bytes per DiskArrayHeader -> 255 headers

        public fixed byte HeadersData[NUM_HEADERS_PER_PAGE * 16]; // 16 bytes per DiskArrayHeader
        public uint NextHeaderPage;
        public uint NumHeaders;

        public DiskArrayHeader GetHeader(int index)
        {
            if (index < 0 || index >= NUM_HEADERS_PER_PAGE) throw new IndexOutOfRangeException();
            fixed (byte* ptr = &HeadersData[index * sizeof(DiskArrayHeader)])
            {
                return *(DiskArrayHeader*)ptr;
            }
        }

        public void SetHeader(int index, DiskArrayHeader header)
        {
            if (index < 0 || index >= NUM_HEADERS_PER_PAGE) throw new IndexOutOfRangeException();
            fixed (byte* ptr = &HeadersData[index * sizeof(DiskArrayHeader)])
            {
                *(DiskArrayHeader*)ptr = header;
            }
        }
    }

    public class DiskArrayCollection
    {
        private readonly FileHandle _fileHandle;
        private uint _headerPagesOnDisk;
        private readonly List<HeaderPage> _headersForReadTrx = new();
        private readonly List<HeaderPage> _headersForWriteTrx = new();
        
        public uint NumHeaders { get; private set; }

        public DiskArrayCollection(FileHandle fileHandle, uint firstHeaderPage = BogDb.Core.Common.Constants.INVALID_PAGE_IDX)
        {
            _fileHandle = fileHandle;
            NumHeaders = 0;

            if (firstHeaderPage != BogDb.Core.Common.Constants.INVALID_PAGE_IDX)
            {
                uint headerPageIdx = firstHeaderPage;
                do
                {
                    byte[] buffer = new byte[BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE];
                    _fileHandle.ReadPage(headerPageIdx, buffer);
                    
                    HeaderPage page;
                    unsafe
                    {
                        fixed (byte* ptr = buffer)
                        {
                            page = *(HeaderPage*)ptr;
                        }
                    }

                    _headersForReadTrx.Add(page);
                    _headersForWriteTrx.Add(page);
                    
                    headerPageIdx = page.NextHeaderPage;
                    NumHeaders += page.NumHeaders;

                } while (headerPageIdx != BogDb.Core.Common.Constants.INVALID_PAGE_IDX);
            }
            else
            {
                var emptyPage = new HeaderPage { NextHeaderPage = BogDb.Core.Common.Constants.INVALID_PAGE_IDX, NumHeaders = 0 };
                _headersForReadTrx.Add(emptyPage);
                _headersForWriteTrx.Add(emptyPage);
            }

            _headerPagesOnDisk = (uint)_headersForReadTrx.Count;
        }

        public DiskArray<T> GetDiskArray<T>(uint idx) where T : unmanaged
        {
            if (idx >= NumHeaders)
                throw new IndexOutOfRangeException($"Index {idx} is outside bounds for DiskArrayCollection.");

            int pageIdx = (int)(idx / HeaderPage.NUM_HEADERS_PER_PAGE);
            int offset = (int)(idx % HeaderPage.NUM_HEADERS_PER_PAGE);

            var readHeader = _headersForReadTrx[pageIdx].GetHeader(offset);
            var writeHeader = _headersForWriteTrx[pageIdx].GetHeader(offset);

            return new DiskArray<T>(_fileHandle, readHeader, writeHeader);
        }

        public uint AddDiskArray()
        {
            uint oldSize = NumHeaders++;

            int pageIdx = (int)(oldSize / HeaderPage.NUM_HEADERS_PER_PAGE);
            if (pageIdx >= _headersForWriteTrx.Count)
            {
                var emptyPage = new HeaderPage { NextHeaderPage = BogDb.Core.Common.Constants.INVALID_PAGE_IDX, NumHeaders = 0 };
                _headersForWriteTrx.Add(emptyPage);
                _headersForReadTrx.Add(emptyPage);
            }

            var writePage = _headersForWriteTrx[pageIdx];
            var readPage = _headersForReadTrx[pageIdx];

            int offset = (int)writePage.NumHeaders;
            writePage.SetHeader(offset, new DiskArrayHeader { FirstPIPPageIdx = BogDb.Core.Common.Constants.INVALID_PAGE_IDX, NumElements = 0 });
            writePage.NumHeaders++;
            readPage.NumHeaders++;

            _headersForWriteTrx[pageIdx] = writePage;
            _headersForReadTrx[pageIdx] = readPage;

            return oldSize;
        }

        public void Checkpoint(uint firstHeaderPage)
        {
            uint headerPage = firstHeaderPage;
            for (int i = 0; i < _headersForWriteTrx.Count; i++)
            {
                var writePage = _headersForWriteTrx[i];

                if (writePage.NextHeaderPage == BogDb.Core.Common.Constants.INVALID_PAGE_IDX && i < _headersForWriteTrx.Count - 1)
                {
                    uint nextHeaderPage = _fileHandle.NumPages; // Simulated allocation
                    _fileHandle.AddNewPage();
                    
                    writePage.NextHeaderPage = nextHeaderPage;
                    _headersForWriteTrx[i] = writePage;
                    
                    var readPage = _headersForReadTrx[i];
                    readPage.NextHeaderPage = nextHeaderPage;
                    _headersForReadTrx[i] = readPage;
                }

                if (i >= _headerPagesOnDisk || true) // Simplified equality check
                {
                    byte[] buffer = new byte[BogDb.Core.Common.Constants.BOGDB_PAGE_SIZE];
                    unsafe
                    {
                        fixed (byte* ptr = buffer)
                        {
                            *(HeaderPage*)ptr = writePage;
                        }
                    }
                    _fileHandle.WritePage(headerPage, buffer);
                }

                headerPage = writePage.NextHeaderPage;
            }
            _headerPagesOnDisk = (uint)_headersForWriteTrx.Count;
        }
    }
}
