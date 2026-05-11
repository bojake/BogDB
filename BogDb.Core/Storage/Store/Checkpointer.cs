using System;

namespace BogDb.Core.Storage.Store
{
    public class Checkpointer
    {
        private bool _isInMemory;

        public Checkpointer(bool isInMemory)
        {
            _isInMemory = isInMemory;
        }

        public void WriteCheckpoint(StorageManager storageManager)
        {
            if (_isInMemory) return;

            // In BogDb C++, Checkpointer writes the Catalog and Metadata headers.
            // Then it invokes `logCheckpointAndApplyShadowPages`
            
            bool hasStorageChanges = CheckpointStorage(storageManager);
            if (!hasStorageChanges)
            {
                return;
            }
            
            // SerializeCatalogAndMetadata(...)
            // WriteDatabaseHeader(...)
            
            LogCheckpointAndApplyShadowPages(storageManager);
            
            storageManager.FinalizeCheckpoint();
            
            storageManager.GetShadowFile().Clear();
        }

        private bool CheckpointStorage(StorageManager storageManager)
        {
            return storageManager.Checkpoint();
        }

        private void LogCheckpointAndApplyShadowPages(StorageManager storageManager)
        {
            var shadowFile = storageManager.GetShadowFile();
            
            // Emulate shadow file flush
            // shadowFile.FlushAll();
            
            // In BogDb C++, here the WAL is flushed to indicate that shadow pages 
            // are safely stored before applying them physically.
            var wal = storageManager.GetWAL();
            wal.LogAndFlushCheckpoint();
            
            // Apply the shadow pages over the physical buffers
            // shadowFile.ApplyShadowPages();
            
            wal.Clear();
        }

        public void Rollback(StorageManager storageManager)
        {
            if (_isInMemory) return;
            
            storageManager.RollbackCheckpoint();
        }
    }
}
