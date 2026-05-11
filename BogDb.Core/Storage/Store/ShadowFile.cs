using System;
using System.Collections.Generic;

namespace BogDb.Core.Storage.Store
{
    public struct ShadowPageRecord
    {
        public ulong OriginalFileIdx;
        public ulong OriginalPageIdx;

        public ShadowPageRecord(ulong originalFileIdx, ulong originalPageIdx)
        {
            OriginalFileIdx = originalFileIdx;
            OriginalPageIdx = originalPageIdx;
        }
    }

    public struct ShadowFileHeader
    {
        public ulong NumShadowPages;
        public Guid DatabaseID;
    }

    public class ShadowFile
    {
        private string _shadowFilePath;
        private Dictionary<ulong, Dictionary<ulong, ulong>> _shadowPagesMap;
        private List<ShadowPageRecord> _shadowPageRecords;
        
        // This is a mapping over C++ VirtualFileSystem and FileHandle.
        // In BogDB, FileHandlers are tracked by BufferManager, mocked here.

        public ShadowFile(string databasePath)
        {
            _shadowFilePath = databasePath + ".shadow";
            _shadowPagesMap = new Dictionary<ulong, Dictionary<ulong, ulong>>();
            _shadowPageRecords = new List<ShadowPageRecord>();
        }

        public bool HasShadowPage(ulong originalFile, ulong originalPage)
        {
            return _shadowPagesMap.ContainsKey(originalFile) &&
                   _shadowPagesMap[originalFile].ContainsKey(originalPage);
        }

        public void ClearShadowPage(ulong originalFile, ulong originalPage)
        {
            if (HasShadowPage(originalFile, originalPage))
            {
                _shadowPagesMap[originalFile].Remove(originalPage);
                if (_shadowPagesMap[originalFile].Count == 0)
                {
                    _shadowPagesMap.Remove(originalFile);
                }
            }
        }

        public ulong GetOrCreateShadowPage(ulong originalFile, ulong originalPage)
        {
            if (HasShadowPage(originalFile, originalPage))
            {
                return _shadowPagesMap[originalFile][originalPage];
            }

            // Emulating shadowingFH->addNewPage() sequence append
            ulong shadowPageIdx = (ulong)_shadowPageRecords.Count + 1; // +1 to skip header page

            if (!_shadowPagesMap.ContainsKey(originalFile))
            {
                _shadowPagesMap[originalFile] = new Dictionary<ulong, ulong>();
            }

            _shadowPagesMap[originalFile][originalPage] = shadowPageIdx;
            _shadowPageRecords.Add(new ShadowPageRecord(originalFile, originalPage));
            
            return shadowPageIdx;
        }

        public ulong GetShadowPage(ulong originalFile, ulong originalPage)
        {
            if (!HasShadowPage(originalFile, originalPage))
            {
                throw new InvalidOperationException($"No shadow page existing for (file: {originalFile}, page: {originalPage})");
            }
            return _shadowPagesMap[originalFile][originalPage];
        }

        public void Clear()
        {
            _shadowPagesMap.Clear();
            _shadowPageRecords.Clear();
        }

        public ulong GetNumShadowPages()
        {
            return (ulong)_shadowPageRecords.Count;
        }

        public void Reset()
        {
            Clear();
            if (System.IO.File.Exists(_shadowFilePath))
            {
                System.IO.File.Delete(_shadowFilePath);
            }
        }
    }
}
