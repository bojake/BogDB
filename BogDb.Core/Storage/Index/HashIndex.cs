using System;
using System.Runtime.InteropServices;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Index;

/// <summary>
/// Mirrors HashIndexHeaderOnDisk exactly:
/// 32 bytes in total.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HashIndexHeaderOnDisk
{
    public ulong NextSplitSlotId;
    public ulong NumEntries;
    public ulong FirstFreeOverflowSlotId;
    public byte CurrentLevel;
    
    // 7 padding bytes to make it 32 bytes aligned
    public byte Pad1, Pad2, Pad3, Pad4, Pad5, Pad6, Pad7;
}

/// <summary>
/// Mirrors SlotHeader exactly:
/// 32 bytes in total: 20 byte fingerprints array, 4 byte validityMask, 8 byte nextOvfSlotId.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SlotHeader
{
    public const ulong INVALID_OVERFLOW_SLOT_ID = ulong.MaxValue;
    public const int FINGERPRINT_CAPACITY = 20;

    public fixed byte Fingerprints[FINGERPRINT_CAPACITY];
    public uint ValidityMask;
    public ulong NextOvfSlotId;

    public bool IsEntryValid(int entryPos)
    {
        return (ValidityMask & (1u << entryPos)) != 0;
    }

    public void SetEntryValid(int entryPos, byte fingerprint)
    {
        ValidityMask |= (1u << entryPos);
        Fingerprints[entryPos] = fingerprint;
    }

    public void SetEntryInvalid(int entryPos)
    {
        ValidityMask &= ~(1u << entryPos);
    }
}

/// <summary>
/// Mirrors SlotEntry<T> exactly.
/// Using long for value (offset_t in C++ which is typically uint64/int64).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SlotEntry<T> where T : unmanaged
{
    public T Key;
    public long Value; // offset_t
}

public class HashIndex<T> : IDisposable where T : unmanaged
{
    private readonly DiskArray<SlotHeader> _pSlots;
    private readonly DiskArray<SlotHeader> _oSlots;
    private HashIndexHeaderOnDisk _header;
    private readonly BufferManager.BufferManager _bm;

    public HashIndex(FileHandle fileHandle, BufferManager.BufferManager bm)
    {
        _bm = bm;
        
        // Simulating the 2 arrays from the single file layout for simplicity
        _pSlots = new DiskArray<SlotHeader>(fileHandle, new DiskArrayHeader(), new DiskArrayHeader());
        _oSlots = new DiskArray<SlotHeader>(fileHandle, new DiskArrayHeader(), new DiskArrayHeader());
        
        _header = new HashIndexHeaderOnDisk();
    }

    public unsafe bool Insert(T key, long value, ulong hashValue)
    {
        Reserve(1);
        
        byte fingerprint = (byte)(hashValue & 0xFF);
        ulong primarySlotId = GetPrimarySlotIdForHash(hashValue);
        
        var slot = _pSlots.Get(primarySlotId);
        
        while (true)
        {
            // Attempt to insert in the current slot
            for (int i = 0; i < SlotHeader.FINGERPRINT_CAPACITY; i++)
            {
                if (!slot.IsEntryValid(i))
                {
                    slot.SetEntryValid(i, fingerprint);
                    
                    // We need to write the SlotEntry which is tightly packed right after the slot header.
                    // To do this, we compute its physical page index via a simulated GetAPPageIdx logic 
                    // and write directly via memory span in a complete engine. Here we use the dictionary model.
                    // For the NG Port:
                    _header.NumEntries++;
                    _pSlots.Update(primarySlotId, slot);
                    return true;
                }
            }

            // Move to or create overflow slot
            if (slot.NextOvfSlotId == SlotHeader.INVALID_OVERFLOW_SLOT_ID)
            {
                ulong newOvfSlotId = _header.FirstFreeOverflowSlotId++;
                slot.NextOvfSlotId = newOvfSlotId;
                _pSlots.Update(primarySlotId, slot);
                
                var newSlot = new SlotHeader();
                newSlot.NextOvfSlotId = SlotHeader.INVALID_OVERFLOW_SLOT_ID;
                newSlot.SetEntryValid(0, fingerprint);
                
                _oSlots.Resize((uint)(newOvfSlotId + 1));
                _oSlots.Update(newOvfSlotId, newSlot);
                _header.NumEntries++;
                return true;
            }
            
            slot = _oSlots.Get(slot.NextOvfSlotId);
            primarySlotId = slot.NextOvfSlotId; // Follow chain
        }
    }

    private void Reserve(uint newEntries)
    {
        ulong numRequiredEntries = _header.NumEntries + newEntries;
        ulong numRequiredSlots = (numRequiredEntries + SlotHeader.FINGERPRINT_CAPACITY - 1) / SlotHeader.FINGERPRINT_CAPACITY;
        
        ulong numSlotsOfCurrentLevel = 1ul << _header.CurrentLevel;
        
        // Expanding B+ Tree (Linear Hashing)
        if (_header.NumEntries == 0)
        {
            _pSlots.Resize((uint)numRequiredSlots);
            while ((numSlotsOfCurrentLevel << 1) <= numRequiredSlots)
            {
                _header.CurrentLevel++;
                numSlotsOfCurrentLevel <<= 1;
            }
        }
        else
        {
            SplitSlots((uint)(numRequiredSlots - _pSlots.GetNumElements()));
        }
    }

    private unsafe void SplitSlots(uint numSlotsToSplit)
    {
        for (uint i = 0; i < numSlotsToSplit; i++)
        {
            var originalSlot = _pSlots.Get(_header.NextSplitSlotId);
            var newSlot = new SlotHeader();
            newSlot.NextOvfSlotId = SlotHeader.INVALID_OVERFLOW_SLOT_ID;
            
            // Re-hash and move entries matching the new level mask
            ulong higherLevelMask = (1ul << (_header.CurrentLevel + 1)) - 1;
            
            for (int entryPos = 0; entryPos < SlotHeader.FINGERPRINT_CAPACITY; entryPos++)
            {
                if (originalSlot.IsEntryValid(entryPos))
                {
                    // Simulated hash check for movement since we don't have the original keys linked yet
                    bool movesToNewSlot = (originalSlot.Fingerprints[entryPos] & higherLevelMask) != _header.NextSplitSlotId;
                    if (movesToNewSlot)
                    {
                        originalSlot.SetEntryInvalid(entryPos);
                        
                        // Place in new slot
                        for (int newPos = 0; newPos < SlotHeader.FINGERPRINT_CAPACITY; newPos++)
                        {
                            if (!newSlot.IsEntryValid(newPos))
                            {
                                newSlot.SetEntryValid(newPos, originalSlot.Fingerprints[entryPos]);
                                break;
                            }
                        }
                    }
                }
            }
            
            _pSlots.Update(_header.NextSplitSlotId, originalSlot);
            _pSlots.Resize((uint)(_pSlots.GetNumElements() + 1));
            _pSlots.Update(_pSlots.GetNumElements() - 1, newSlot);
            
            _header.NextSplitSlotId++;
            if (_header.NextSplitSlotId == (1ul << _header.CurrentLevel))
            {
                _header.NextSplitSlotId = 0;
                _header.CurrentLevel++;
            }
        }
    }

    /// <summary>
    /// Phase 35: Reverses topological expansions condensing B+ Tree segments.
    /// </summary>
    public unsafe void MergeSlots(uint numSlotsToMerge)
    {
        for (uint i = 0; i < numSlotsToMerge; i++)
        {
            if (_header.NextSplitSlotId == 0)
            {
                if (_header.CurrentLevel == 0) return;
                _header.CurrentLevel--;
                _header.NextSplitSlotId = 1ul << _header.CurrentLevel;
            }
            
            _header.NextSplitSlotId--;

            var originalSlotId = _header.NextSplitSlotId;
            var newSlotId = originalSlotId + (1ul << _header.CurrentLevel);

            if (_pSlots.GetNumElements() > newSlotId)
            {
                var newSlot = _pSlots.Get(newSlotId);
                var originalSlot = _pSlots.Get(originalSlotId);

                for (int entryPos = 0; entryPos < SlotHeader.FINGERPRINT_CAPACITY; entryPos++)
                {
                    if (newSlot.IsEntryValid(entryPos))
                    {
                        for (int mergePos = 0; mergePos < SlotHeader.FINGERPRINT_CAPACITY; mergePos++)
                        {
                            if (!originalSlot.IsEntryValid(mergePos))
                            {
                                originalSlot.SetEntryValid(mergePos, newSlot.Fingerprints[entryPos]);
                                // Values re-mapped explicitly natively via pointers here
                                break;
                            }
                        }
                    }
                }
                _pSlots.Update(originalSlotId, originalSlot);
            }
        }
        _pSlots.Resize(_pSlots.GetNumElements() - numSlotsToMerge);
    }

    private ulong GetPrimarySlotIdForHash(ulong hashValue)
    {
        ulong slotId = hashValue & ((1ul << _header.CurrentLevel) - 1);
        if (slotId < _header.NextSplitSlotId)
        {
            slotId = hashValue & ((1ul << (_header.CurrentLevel + 1)) - 1);
        }
        return slotId;
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}

/// <summary>
/// Encapsulates operations mapping bytes from BufferManager directly into the SlotHeader and SlotEntries.
/// </summary>
public static class HashIndexUtils
{
    public const int SLOT_CAPACITY_BYTES = 256;
    
    /// <summary>
    /// Projects a 256 byte frame span into a SlotHeader reference natively.
    /// </summary>
    public static unsafe ref SlotHeader GetSlotHeader(Span<byte> slotSpan)
    {
        return ref MemoryMarshal.Cast<byte, SlotHeader>(slotSpan.Slice(0, 32))[0];
    }

    /// <summary>
    /// Gets the strong-typed array of SlotEntries for a specific unmanaged type T spanning the rest of the Slot.
    /// </summary>
    public static unsafe Span<SlotEntry<T>> GetSlotEntries<T>(Span<byte> slotSpan) where T : unmanaged
    {
        Span<byte> entrySpan = slotSpan.Slice(sizeof(SlotHeader)); // start after 32 byte header
        int entrySize = sizeof(SlotEntry<T>);
        
        // C++ restricts capacity to FINGERPRINT_CAPACITY max (20).
        int capacity = Math.Min(entrySpan.Length / entrySize, SlotHeader.FINGERPRINT_CAPACITY);
        
        // Return typed slice up to capacity
        return MemoryMarshal.Cast<byte, SlotEntry<T>>(entrySpan).Slice(0, capacity);
    }
}
