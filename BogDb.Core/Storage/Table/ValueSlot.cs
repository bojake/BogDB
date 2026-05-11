using System.Runtime.InteropServices;

namespace BogDb.Core.Storage.Table;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
public struct ValueSlot
{
    public byte Flags;
    public long Payload;
    public int OverflowId;
    public byte Pad1;
    public byte Pad2;
    public byte Pad3;
}
