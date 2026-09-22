using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Threading.Channels;

namespace SysSuite.Core.Data;

public sealed class UsnJournalMonitor : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FsctlQueryUsnJournal = 0x000900F4;
    private const uint FsctlReadUsnJournal = 0x000900B8;
    private const uint ReasonFileCreate = 0x00000100;
    private const uint ReasonFileDelete = 0x00000200;
    private const uint ReasonRenameNewName = 0x00002000;
    private const uint ReasonClose = 0x80000000;
    private const int RecordHeaderSize = 60;

    private readonly Dictionary<string, SafeFileHandle> volumeHandles = [];
    private readonly Channel<string> eventChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<Task> workers = [];
    private Task? eventWorker;
    private bool started;

    public event EventHandler<string>? FileChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            return;
        }

        started = true;
        eventWorker = Task.Run(() => ProcessEventsAsync(cancellation.Token));
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            var worker = Task.Run(() => MonitorVolumeAsync(drive.Name, cancellation.Token));
            workers.Add(worker);
        }
    }

    public void Stop()
    {
        if (!started)
        {
            return;
        }

        started = false;
        cancellation.Cancel();
        eventChannel.Writer.TryComplete();
        List<SafeFileHandle> handlesToCancel;
        lock (volumeHandles)
        {
            handlesToCancel = [.. volumeHandles.Values];
        }

        foreach (var handle in handlesToCancel)
        {
            try
            {
                if (!handle.IsClosed)
                {
                    NativeMethods.CancelIoEx(handle, IntPtr.Zero);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            Task.WaitAll([.. workers, eventWorker ?? Task.CompletedTask], TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        volumeHandles.Clear();
        workers.Clear();
    }

    private async Task MonitorVolumeAsync(string driveName, CancellationToken cancellationToken)
    {
        var buffer = IntPtr.Zero;
        var input = IntPtr.Zero;
        var handle = NativeMethods.CreateFile(
            @"\\.\" + driveName[..2],
            GenericRead,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return;
        }

        lock (volumeHandles)
        {
            volumeHandles[driveName] = handle;
        }
        try
        {
            buffer = Marshal.AllocHGlobal(64 * 1024);
            input = Marshal.AllocHGlobal(Marshal.SizeOf<ReadUsnJournalInput>());
            if (!NativeMethods.DeviceIoControl(handle, FsctlQueryUsnJournal, IntPtr.Zero, 0, buffer, 64 * 1024, out _, IntPtr.Zero))
            {
                return;
            }

            var journalId = Marshal.ReadInt64(buffer, 0);
            var nextUsn = Marshal.ReadInt64(buffer, 8);
            var reasonMask = ReasonFileCreate | ReasonFileDelete | ReasonRenameNewName | ReasonClose;
            while (!cancellationToken.IsCancellationRequested)
            {
                Marshal.StructureToPtr(new ReadUsnJournalInput(
                    nextUsn,
                    reasonMask,
                    1,
                    0,
                    journalId), input, false);
                if (!NativeMethods.DeviceIoControl(
                        handle,
                        FsctlReadUsnJournal,
                        input,
                        Marshal.SizeOf<ReadUsnJournalInput>(),
                        buffer,
                        64 * 1024,
                        out var bytesReturned,
                        IntPtr.Zero))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                if (bytesReturned <= sizeof(long))
                {
                    await Task.Delay(200, cancellationToken);
                    continue;
                }

                nextUsn = Marshal.ReadInt64(buffer, 0);
                var offset = sizeof(long);
                while (offset + RecordHeaderSize <= bytesReturned)
                {
                    var recordLength = Marshal.ReadInt32(buffer, offset);
                    if (recordLength <= 0 || offset + recordLength > bytesReturned)
                    {
                        break;
                    }

                    var reason = Marshal.ReadInt32(buffer, offset + 40);
                    var fileNameLength = Marshal.ReadInt16(buffer, offset + 56);
                    var fileNameOffset = Marshal.ReadInt16(buffer, offset + 58);
                    if (fileNameLength > 0 && fileNameOffset >= RecordHeaderSize && offset + fileNameOffset + fileNameLength <= bytesReturned)
                    {
                        var fileName = Marshal.PtrToStringUni(buffer + offset + fileNameOffset, fileNameLength);
                        if (!string.IsNullOrWhiteSpace(fileName)
                            && Path.GetExtension(fileName) is ".exe" or ".msi"
                            && (reason & (ReasonFileCreate | ReasonFileDelete | ReasonRenameNewName)) != 0)
                        {
                            await eventChannel.Writer.WriteAsync(fileName, cancellationToken);
                        }
                    }

                    offset += recordLength;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            lock (volumeHandles)
            {
                volumeHandles.Remove(driveName);
            }

            handle.Dispose();
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (input != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(input);
            }
        }
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        var lastNotification = DateTimeOffset.MinValue;
        await foreach (var fileName in eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;
            if (now - lastNotification < TimeSpan.FromMilliseconds(500))
            {
                continue;
            }

            lastNotification = now;
            FileChanged?.Invoke(this, fileName);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();
        cancellation.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ReadUsnJournalInput(long startUsn, uint reasonMask, byte returnOnlyCloseEvents, ulong bytesToWaitFor, long usnJournalId)
    {
        public readonly long StartUsn = startUsn;
        public readonly uint ReasonMask = reasonMask;
        public readonly byte ReturnOnlyCloseEvents = returnOnlyCloseEvents;
        public readonly ulong BytesToWaitFor = bytesToWaitFor;
        public readonly long UsnJournalId = usnJournalId;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint controlCode,
            IntPtr inputBuffer,
            int inputBufferSize,
            IntPtr outputBuffer,
            int outputBufferSize,
            out int bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CancelIoEx(SafeFileHandle device, IntPtr overlapped);
    }

    private bool disposed;
}
