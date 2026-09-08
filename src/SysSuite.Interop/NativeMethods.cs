using System.Runtime.InteropServices;

namespace SysSuite.Interop;

public static partial class NativeMethods
{
    private const int VersionBufferLength = 128;

    [LibraryImport("SysSuite.Native.dll", EntryPoint = "Native_Version", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial int NativeVersion([Out] char[] buffer, int length);

    public static string? TryGetVersion()
    {
        var buffer = new char[VersionBufferLength];
        return NativeVersion(buffer, buffer.Length) == 0
            ? new string(buffer, 0, Array.IndexOf(buffer, '\0'))
            : null;
    }
}
