using System.Diagnostics;
using System.Runtime.InteropServices;
using SysSuite.Core.Abstractions;

namespace SysSuite.Core.Diagnostics;

internal static class CrashDumpService
{
    [Flags]
    private enum MiniDumpType
    {
        Normal = 0,
        WithDataSegs = 1,
        WithHandleData = 4,
        WithThreadInfo = 64
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr processHandle,
        int processId,
        IntPtr fileHandle,
        MiniDumpType dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    public static Result Write(string path)
    {
        try
        {
            using var stream = File.Create(path);
            var process = Process.GetCurrentProcess();
            var dumpType = MiniDumpType.WithDataSegs | MiniDumpType.WithHandleData | MiniDumpType.WithThreadInfo;
            var written = MiniDumpWriteDump(
                process.Handle,
                process.Id,
                stream.SafeFileHandle.DangerousGetHandle(),
                dumpType,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);
            return written ? Result.Success() : Result.Failure(ErrorType.Internal, $"MiniDumpWriteDump failed: {Marshal.GetLastWin32Error()}");
        }
        catch (IOException exception)
        {
            return Result.Failure(ErrorType.Internal, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result.Failure(ErrorType.AccessDenied, exception.Message);
        }
    }
}
