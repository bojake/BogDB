using System;
using System.Runtime.InteropServices;
using Xunit;
using BogDb.Core.Storage;
using BogDb.Core.Storage.Index;
using BogDb.Core.Transaction;

namespace BogDb.Tests.Storage;

public class BTreeIndexTests
{
    [Fact]
    public void UndoBuffer_WriteTransaction_AllocatesAndIteratesRecords()
    {
        var trx = new BogDb.Core.Transaction.Transaction(TransactionType.WRITE, 1, 100);
        Assert.NotNull(trx.UndoBuffer);

        unsafe
        {
            // Simulate 5 INSERT_INFO records
            for (int i = 0; i < 5; i++)
            {
                var payloadSize = (uint)sizeof(long);
                var headerSize = (uint)sizeof(UndoRecordHeader);
                
                byte* raw = trx.UndoBuffer.CreateUndoRecord(headerSize + payloadSize);
                
                UndoRecordHeader* header = (UndoRecordHeader*)raw;
                header->RecordType = UndoRecordType.INSERT_INFO;
                header->RecordSize = payloadSize;

                // Write a dummy payload (e.g. node offset)
                long* payloadPos = (long*)(raw + headerSize);
                *payloadPos = i * 10;
            }

            int forwardCount = 0;
            trx.UndoBuffer.Iterate((recordType, payloadPtr, size) =>
            {
                Assert.Equal(UndoRecordType.INSERT_INFO, recordType);
                long payload = Marshal.PtrToStructure<long>(payloadPtr);
                Assert.Equal(forwardCount * 10, payload);
                Assert.Equal(8u, size); // sizeof(long)
                forwardCount++;
            });
            Assert.Equal(5, forwardCount);

            int backwardCount = 5;
            trx.UndoBuffer.ReverseIterate((recordType, payloadPtr, size) =>
            {
                backwardCount--;
                Assert.Equal(UndoRecordType.INSERT_INFO, recordType);
                long payload = Marshal.PtrToStructure<long>(payloadPtr);
                Assert.Equal(backwardCount * 10, payload);
                Assert.Equal(8u, size);
            });
            Assert.Equal(0, backwardCount);
        }

        trx.Rollback(); // Disposes the buffer
    }

    [Fact]
    public void HashIndex_MapsSlotHeaderOverMemoryUnchanged()
    {
        // Allocate a mock page of 4096 bytes representing a BufferManager frame
        var pageBytes = new byte[4096];
        Span<byte> slotSpan = pageBytes.AsSpan(0, HashIndexUtils.SLOT_CAPACITY_BYTES);

        // 1. Map header ref
        ref SlotHeader header = ref HashIndexUtils.GetSlotHeader(slotSpan);
        header.ValidityMask = 0;
        header.NextOvfSlotId = SlotHeader.INVALID_OVERFLOW_SLOT_ID;
        
        Assert.False(header.IsEntryValid(0));
        header.SetEntryValid(0, 15);
        Assert.True(header.IsEntryValid(0));

        // 2. Map strong-typed struct entry access over remaining memory
        Span<SlotEntry<long>> entries = HashIndexUtils.GetSlotEntries<long>(slotSpan);
        
        // Slot capacity for long should be restricted to FINGERPRINT_CAPACITY=20
        Assert.True(entries.Length <= SlotHeader.FINGERPRINT_CAPACITY);
        
        // Edit entry directly via ref Span bounds
        entries[0].Key = 100500;
        entries[0].Value = 99;

        // Verify the edits persist linearly inside the raw byte buffer simulating I/O mapping
        var extractedSpan = HashIndexUtils.GetSlotEntries<long>(pageBytes.AsSpan(0, HashIndexUtils.SLOT_CAPACITY_BYTES));
        Assert.Equal(100500, extractedSpan[0].Key);
        Assert.Equal(99, extractedSpan[0].Value);
        
        ref SlotHeader extractedHeader = ref HashIndexUtils.GetSlotHeader(pageBytes.AsSpan(0, HashIndexUtils.SLOT_CAPACITY_BYTES));
        Assert.True(extractedHeader.IsEntryValid(0));
        
        unsafe 
        {
            Assert.Equal(15, extractedHeader.Fingerprints[0]);
            Assert.Equal(SlotHeader.INVALID_OVERFLOW_SLOT_ID, extractedHeader.NextOvfSlotId);
        }
    }
}
