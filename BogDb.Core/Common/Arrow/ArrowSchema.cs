using System;
using System.Runtime.InteropServices;

namespace BogDb.Core.Common.Arrow;

/// <summary>
/// Unmanaged schema representing type descriptors accompanying cross-process IPC Apache Arrow arrays.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArrowSchema
{
    public byte* Format;
    public byte* Name;
    public byte* Metadata;
    public long Flags;
    public long NChildren;
    
    public ArrowSchema** Children;
    public ArrowSchema* Dictionary;
    
    public IntPtr Release;
    public IntPtr PrivateData;
}
