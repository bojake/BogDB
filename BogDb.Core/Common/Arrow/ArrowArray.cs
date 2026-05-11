using System;
using System.Runtime.InteropServices;

namespace BogDb.Core.Common.Arrow;

/// <summary>
/// Unmanaged mapping for Apache Arrow C Data Interface representing memory layouts.
/// Provides zero-copy IPC streams matching standard library export structures natively.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArrowArray
{
    public long Length;
    public long NullCount;
    public long Offset;
    public long NBuffers;
    public long NChildren;
    public void** Buffers;
    public ArrowArray** Children;
    public ArrowArray* Dictionary;
    
    public IntPtr Release;
    public IntPtr PrivateData;
}
