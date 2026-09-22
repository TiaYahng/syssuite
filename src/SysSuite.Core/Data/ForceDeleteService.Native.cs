using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SysSuite.Core.Data;

public sealed partial class ForceDeleteService
{
    private const int MoveFileDelayUntilReboot = 0x4;

    private static void EnableTakeOwnershipPrivilege()
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), 0x28, out var token))
        {
            return;
        }

        using (token)
        {
            if (!NativeMethods.LookupPrivilegeValue(null, "SeTakeOwnershipPrivilege", out var luid))
            {
                return;
            }

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privilege = new TokenPrivilegeOne
                {
                    Luid = luid,
                    Attributes = 2
                }
            };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TokenPrivileges>());
            try
            {
                Marshal.StructureToPtr(privileges, pointer, false);
                NativeMethods.AdjustTokenPrivileges(token, false, pointer, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr processToken, uint desiredAccess, out SafeFileHandle tokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LookupPrivilegeValue(string? systemName, string privilegeName, out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AdjustTokenPrivileges(
            SafeFileHandle tokenHandle,
            bool disableAllPrivileges,
            IntPtr newState,
            uint bufferLength,
            IntPtr previousState,
            IntPtr returnLength);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivilegeOne
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public int PrivilegeCount;
        public TokenPrivilegeOne Privilege;
    }
}
