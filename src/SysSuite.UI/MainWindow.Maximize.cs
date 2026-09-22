using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SysSuite.UI;

public partial class MainWindow
{
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (message == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
            var monitor = MonitorFromWindow(hwnd, 2);
            var monitorInfo = default(NativeMonitorInfo);
            monitorInfo.CbSize = Marshal.SizeOf<NativeMonitorInfo>();
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                info.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.Monitor.Left;
                info.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.Monitor.Top;
                info.MaxSize.X = monitorInfo.WorkArea.Width;
                info.MaxSize.Y = monitorInfo.WorkArea.Height;
                Marshal.StructureToPtr(info, lParam, true);
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect(int left, int top, int right, int bottom)
    {
        public int Left = left;
        public int Top = top;
        public int Right = right;
        public int Bottom = bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int CbSize;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }
}
