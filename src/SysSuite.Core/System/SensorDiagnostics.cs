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
    private const string PawnIoDriverFileName = "PawnIO.sys";

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

            // 驱动不一定落在 System32\drivers 下：PawnIO 是 INF 安装的，
            // 实际位置是 DriverStore\FileRepository\pawnio.inf_amd64_<hash>\PawnIO.sys，
            // 服务的 ImagePath 就指向那里。**只查 System32\drivers 会误判为"未安装"**
            // （实测踩过：服务 Running、程序已装，检测却返回 false）。
            // 因此优先按 ImagePath 解析，并回退到两个常见位置。
            return DriverFileExists(key.GetValue("ImagePath") as string);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // 注册表不可读（极少数受限环境）时不下结论
            _ = exception;
            return true;
        }
    }

    private static bool DriverFileExists(string? imagePath)
    {
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(systemRoot))
        {
            // 读不到系统目录时以"服务已注册"为准，宁可少报错也不要误报缺失
            return true;
        }

        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var resolved = imagePath
                .Replace(@"\SystemRoot\", $"{systemRoot}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                .Replace(@"\??\", string.Empty, StringComparison.Ordinal);
            if (File.Exists(resolved))
            {
                return true;
            }
        }

        var candidates = new[]
        {
            Path.Combine(systemRoot, "System32", "drivers", PawnIoDriverFileName),
            Path.Combine(systemRoot, "System32", "DriverStore", "FileRepository")
        };

        if (File.Exists(candidates[0]))
        {
            return true;
        }

        // DriverStore 下目录名带 INF 哈希，无法拼死路径，只能扫一层
        try
        {
            var repository = candidates[1];
            return Directory.Exists(repository)
                && Directory.EnumerateDirectories(repository, "pawnio.inf_*").Any(directory =>
                    File.Exists(Path.Combine(directory, PawnIoDriverFileName)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
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

    /// <summary>
    /// PawnIO 已安装，但当前进程**不是管理员**时，CPU 传感器依然全部读不到。
    ///
    /// 2026-09-23 实测（本机 i7-10750H）：装好 PawnIO（服务 Running、驱动已加载）后，
    ///   - 非提权：CPU 39 个传感器中温度/倍频/功耗/电压**全部为 null**；
    ///   - 提权：同一份代码读到 47 个传感器，CPU Core 温度、Power、Clock 齐全。
    /// 也就是说 **"装了 PawnIO" 与 "能读 CPU 温度" 之间还差一个管理员权限** ——
    /// LHM 要通过 PawnIO 加载内核模块，该动作需要管理员。这一点官方文档没写清楚，
    /// 极易被误判为"PawnIO 没装好"。
    /// </summary>
    internal static bool IsPawnIoInstalledButNotElevated()
        => IsPawnIoAvailable() && !Environment.IsPrivilegedProcess;
}
