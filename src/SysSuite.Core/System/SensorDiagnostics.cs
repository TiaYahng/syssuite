using Microsoft.Win32;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 传感器能力诊断（T1.2）。
///
/// 背景：LibreHardwareMonitor 0.9.6 起把内核态访问层从 WinRing0 换成了 **PawnIO**
/// （WinRing0 被 Defender 判为易受攻击驱动而全量下架）。官方维护者的原话是：
/// "PawnIO provides the low level hardware layer. If you don't install it,
/// LibreHardwareMonitor could not read or write any value (including CPU values)."
///
/// 结果就是：**没装 PawnIO 时，CPU 的所有温度/倍频/功耗读数一律为 null**，
/// 而 GPU（走 NVAPI/nvml）与 NVMe（走 IOCTL 存储协议）照常能读。
/// 这个"半可用"状态非常容易被误判成程序 bug，所以必须显式诊断出来并告诉用户怎么修，
/// 而不是让界面静默显示 "--"。
/// </summary>
internal static class SensorDiagnostics
{
    private const string PawnIoServiceKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";
    private const string PawnIoDriverPath = @"System32\drivers\PawnIO.sys";

    /// <summary>PawnIO 内核驱动是否可用（服务已注册且驱动文件存在）。</summary>
    internal static bool IsPawnIoAvailable()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PawnIoServiceKey);
            if (key is null)
            {
                return false;
            }

            var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(systemRoot))
            {
                // 读不到系统目录时以"服务已注册"为准，宁可少报错也不要误报缺失
                return true;
            }

            return File.Exists(Path.Combine(systemRoot, PawnIoDriverPath));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // 注册表不可读（极少数受限环境）时不下结论
            _ = exception;
            return true;
        }
    }

    /// <summary>
    /// 需要内核驱动的硬件类别。目前只有 CPU 的 DTS/MSR 通道依赖 PawnIO；
    /// GPU 与存储各有自己的用户态通道，不受影响。
    /// </summary>
    internal static bool RequiresKernelDriver(SensorHardwareClass hardwareClass)
        => hardwareClass == SensorHardwareClass.Cpu;
}
