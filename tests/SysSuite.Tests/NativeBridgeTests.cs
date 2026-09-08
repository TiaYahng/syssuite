using SysSuite.Core;
using System.Runtime.InteropServices;
using Xunit;

namespace SysSuite.Tests;

public partial class NativeBridgeTests
{
    [Fact]
    public void NativeBridgeReturnsVersion()
    {
        var result = new NativeBridge().GetVersion();
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("1.0.0-m0", result.Version);
    }

    [Fact]
    public void NativeVersionAcceptsInvalidParameters()
    {
        Assert.Equal(-1, NativeMethodsExposed.NativeVersion(null, 128));
        Assert.Equal(-1, NativeMethodsExposed.NativeVersion(new char[4], 4));
        Assert.Equal(-2, NativeMethodsExposed.NativeVersion(new char[8], 8));
    }

    internal static partial class NativeMethodsExposed
    {
        [LibraryImport("SysSuite.Native.dll", EntryPoint = "Native_Version", StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.I4)]
        internal static partial int NativeVersion([Out] char[]? buffer, int length);
    }
}
