using System.Runtime.InteropServices;
using BogDb.Core.Common;
using Xunit;

namespace BogDb.Tests.Common;

public unsafe class KuStringTests
{
    [Fact]
    public void KuString_ShouldBeExactlySixteenBytes()
    {
        Assert.Equal(16, Marshal.SizeOf<KuString>());
        Assert.Equal(16, sizeof(KuString));
    }

    [Fact]
    public void GettingShortString_ShouldDecodeProperly()
    {
        var kuStr = new KuString();
        kuStr.Length = 11; // "Hello World"
        
        // Write Prefix "Hell"
        kuStr.Prefix[0] = (byte)'H';
        kuStr.Prefix[1] = (byte)'e';
        kuStr.Prefix[2] = (byte)'l';
        kuStr.Prefix[3] = (byte)'l';

        // Write remainder "o World"
        kuStr.Data[0] = (byte)'o';
        kuStr.Data[1] = (byte)' ';
        kuStr.Data[2] = (byte)'W';
        kuStr.Data[3] = (byte)'o';
        kuStr.Data[4] = (byte)'r';
        kuStr.Data[5] = (byte)'l';
        kuStr.Data[6] = (byte)'d';

        var result = kuStr.GetAsString();
        Assert.Equal("Hello World", result);
        Assert.True(KuString.IsShortString(kuStr.Length));
    }

    [Fact]
    public void GettingLongString_ShouldReadFromPointer()
    {
        var kuStr = new KuString();
        string longString = "This is a very long string that should overflow its 12 bytes buffer capacity.";
        kuStr.Length = (uint)longString.Length;

        // Allocate unmanaged memory for the string just for testing
        IntPtr ptr = Marshal.StringToHGlobalAnsi(longString);
        try
        {
            kuStr.OverflowPtr = (ulong)ptr;

            string result = kuStr.GetAsString();
            Assert.Equal(longString, result);
            Assert.False(KuString.IsShortString(kuStr.Length));
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
