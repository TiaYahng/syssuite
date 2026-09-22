using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SysSuite.Core.System;

public sealed record MftFileEntry(ulong FileReference, ulong ParentReference, string Name, bool IsDirectory, long SizeBytes);

public static class MftFileIndexService
{
    private const uint FsctlEnumUsnData = 0x000900B3;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int OutputBufferSize = 1024 * 1024;

    public static bool IsSupported(string root)
    {
        try
        {
            var drive = Path.GetPathRoot(root);
            return !string.IsNullOrWhiteSpace(drive)
                && new DriveInfo(drive).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
    }

    public static IReadOnlyList<MftFileEntry> Enumerate(string root, CancellationToken cancellationToken)
    {
        if (!IsSupported(root)) return [];
        var volume = Path.GetPathRoot(root)!;
        var volumePath = volume.EndsWith(Path.DirectorySeparatorChar) ? volume[..^1] : volume;
        using var handle = CreateFile(volumePath, GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return [];

        var entries = new List<MftFileEntry>();
        var input = new MftEnumDataV0 { StartFileReferenceNumber = 0, LowUsn = 0, HighUsn = long.MaxValue };
        var inputSize = Marshal.SizeOf<MftEnumDataV0>();
        var output = new byte[OutputBufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputPtr = Marshal.AllocHGlobal(inputSize);
            try
            {
                Marshal.StructureToPtr(input, inputPtr, false);
                if (!DeviceIoControl(handle, FsctlEnumUsnData, inputPtr, (uint)inputSize, output, (uint)output.Length, out var bytesReturned, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 38 || error == 1179) break;
                    throw new Win32Exception(error);
                }
                if (bytesReturned < 8) break;
                input.StartFileReferenceNumber = BitConverter.ToUInt64(output, 0);
                var offset = 8;
                while (offset + 60 <= bytesReturned)
                {
                    var recordLength = BitConverter.ToUInt32(output, offset);
                    if (recordLength < 60 || offset + recordLength > bytesReturned) break;
                    if (BitConverter.ToUInt16(output, offset + 4) == 2)
                    {
                        var fileRef = BitConverter.ToUInt64(output, offset + 8);
                        var parentRef = BitConverter.ToUInt64(output, offset + 16);
                        var attributes = BitConverter.ToUInt32(output, offset + 52);
                        var nameLength = BitConverter.ToUInt16(output, offset + 56);
                        var nameOffset = BitConverter.ToUInt16(output, offset + 58);
                        if (nameOffset + nameLength <= recordLength)
                        {
                            var name = Encoding.Unicode.GetString(output, offset + nameOffset, nameLength);
                            entries.Add(new MftFileEntry(fileRef, parentRef, name, (attributes & 0x10) != 0, 0));
                        }
                    }
                    offset += (int)recordLength;
                }
            }
            finally { Marshal.FreeHGlobal(inputPtr); }
        }
        return entries;
    }

    public static IReadOnlyList<string> RebuildPaths(IReadOnlyList<MftFileEntry> entries, string root)
    {
        var byRef = entries.GroupBy(entry => entry.FileReference).ToDictionary(group => group.Key, group => group.First());
        var cache = new Dictionary<ulong, string>();
        var result = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.IsDirectory) continue;
            var path = ResolvePath(entry.FileReference, byRef, cache, root, new HashSet<ulong>());
            if (path is not null) result.Add(path);
        }
        return result;
    }

    private static string? ResolvePath(ulong reference, IReadOnlyDictionary<ulong, MftFileEntry> entries, IDictionary<ulong, string> cache, string root, ISet<ulong> visiting)
    {
        if (cache.TryGetValue(reference, out var cached)) return cached;
        if (!entries.TryGetValue(reference, out var entry) || !visiting.Add(reference)) return null;
        try
        {
            var parent = entry.ParentReference == reference
                ? root.TrimEnd(Path.DirectorySeparatorChar)
                : ResolvePath(entry.ParentReference, entries, cache, root, visiting);
            if (parent is null) return null;
            var path = Path.Combine(parent, entry.Name);
            cache[reference] = path;
            return path;
        }
        finally { visiting.Remove(reference); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MftEnumDataV0
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr inBuffer, uint inBufferSize, [Out] byte[] outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);
}
