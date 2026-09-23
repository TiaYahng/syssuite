using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace SysSuite.Core.System;

/// <summary>
/// 服务探针的真实实现（与 Service.cs 拆开只为守住 300 行门禁）。
/// 拆的是文件不是职责：写入逻辑仍在 Service.cs，本文件只负责怎么读写系统。
/// </summary>
public sealed partial class WindowsUpdateControlService
{
    /// <summary>
    /// 真实服务探针。
    ///
    /// 两条刻意的选择：
    ///   1. **不用 <c>System.ServiceProcess.ServiceController</c>** —— 该类型在 .NET 8 已拆成独立
    ///      NuGet 包，为一个"读启动类型 + 停服务"引入新依赖不划算；直接用 <c>advapi32</c> 更轻。
    ///   2. **不用 <c>sc.exe</c>** —— 本仓库沙箱把 sc.exe 列入黑名单，且启动子进程会闪控制台窗口。
    ///
    /// 服务句柄一律 <c>CloseServiceHandle</c> 释放：SCM 句柄泄漏会让"启动/停止服务"在长跑进程里
    /// 逐渐失败，症状是"用一会儿就改不动了"，很难反查到句柄上。
    ///
    /// **每个注册表读都必须吞掉 <see cref="SecurityException"/>**：键存在但没有读权限时
    /// <c>OpenSubKey</c> 会抛，而不是返回 null（返回 null 只在键不存在时发生）。
    /// 不吞掉就会让"读不到"变成一次崩溃，直接违背本服务的第一原则。
    /// </summary>
    private sealed class ServiceUpdateProbe : IUpdateServiceProbe
    {
        private const uint ScManagerConnect = 0x0001;
        private const uint ServiceQueryStatus = 0x0004;
        private const uint ServiceStop = 0x0020;
        private const uint ScStatusStopped = 0x00000001;
        private const uint ScStatusStopPending = 0x00000003;

        public string ReadStartType(string serviceName)
        {
            // ServiceController 不暴露启动类型，注册表才是权威来源，且不需要 SCM 句柄
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: false);
                if (key is null)
                {
                    return "Missing";
                }

                var start = key.GetValue("Start")?.ToString();
                return start switch
                {
                    "2" => "Auto",
                    "3" => "Manual",
                    "4" => "Disabled",
                    "0" or "1" => "Boot",
                    _ => "Unknown",
                };
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
            {
                return "Missing";
            }
        }

        public bool SetStartType(string serviceName, string startType)
        {
            var start = startType switch
            {
                "Auto" => 2,
                "Manual" => 3,
                "Disabled" => 4,
                _ => -1,
            };
            if (start < 0)
            {
                return false;
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
                if (key is null)
                {
                    return false;
                }

                key.SetValue("Start", start, RegistryValueKind.DWord);
                return true;
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool StopService(string serviceName)
        {
            var manager = NativeMethods.OpenSCManager(null, null, ScManagerConnect);
            if (manager == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var service = NativeMethods.OpenService(
                    manager, serviceName, ServiceQueryStatus | ServiceStop);
                if (service == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    if (!NativeMethods.QueryServiceStatus(service, out var status))
                    {
                        return false;
                    }

                    if (status.CurrentState is ScStatusStopped or ScStatusStopPending)
                    {
                        return true;
                    }

                    if (!NativeMethods.ControlService(service, ServiceStop, out _))
                    {
                        return false;
                    }

                    return WaitForStopped(service);
                }
                finally
                {
                    _ = NativeMethods.CloseServiceHandle(service);
                }
            }
            finally
            {
                _ = NativeMethods.CloseServiceHandle(manager);
            }
        }

        public string ReadTaskState(string taskPath)
        {
            try
            {
                using var key = OpenTaskKey(taskPath, writable: false);
                if (key is null)
                {
                    return "Missing";
                }

                var value = key.GetValue("Enabled")?.ToString();
                return value switch
                {
                    "0" => "Disabled",
                    _ => "Enabled", // 缺省即启用
                };
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
            {
                return "Missing";
            }
        }

        public bool SetTaskState(string taskPath, bool enabled)
        {
            try
            {
                using var key = OpenTaskKey(taskPath, writable: true);
                if (key is null)
                {
                    return false;
                }

                key.SetValue("Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
                return true;
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// 轮询等待服务停止。不用 <c>WaitForStatus</c> 是因为它已被随 ServiceController 一起移除。
        ///
        /// 15 秒上限按 wuauserv 的最坏情况取 —— 它可能正在下载中，停止请求要等当前
        /// 传输块收尾。超时不当作失败：启动类型已改，重启后照样生效。
        /// </summary>
        private static bool WaitForStopped(IntPtr service)
        {
            var deadline = Environment.TickCount64 + 15_000;
            while (Environment.TickCount64 < deadline)
            {
                if (!NativeMethods.QueryServiceStatus(service, out var status))
                {
                    return false;
                }

                if (status.CurrentState == ScStatusStopped)
                {
                    return true;
                }

                Thread.Sleep(200);
            }

            return false;
        }

        private static RegistryKey? OpenTaskKey(string taskPath, bool writable)
            => Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree\{taskPath}",
                writable);

        /// <summary>advapi32 服务控制面。字段顺序必须与 Windows ABI 完全一致。</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public uint ServiceType;
            public uint CurrentState;
            public uint ControlsAccepted;
            public uint Win32ExitCode;
            public uint ServiceSpecificExitCode;
            public uint CheckPoint;
            public uint WaitHint;
        }

        private static class NativeMethods
        {
            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool CloseServiceHandle(IntPtr handle);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);
        }
    }
}
