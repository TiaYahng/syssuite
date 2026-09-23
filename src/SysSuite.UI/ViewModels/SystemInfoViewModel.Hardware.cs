using System.Globalization;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.Pages;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 硬件信息到可读文本与行的映射，以及格式化辅助。
/// 从 <c>SystemInfoViewModel.cs</c> 拆出以满足单文件 ≤ 300 行门禁。
/// </summary>
public sealed partial class SystemInfoViewModel
{
    private void ApplyHardwareInfo(HardwareInfo info)
    {
        LastHardwareInfo = info;
        SetStatus($"计算机：{info.ComputerName}");
        CpuText = $"{info.CpuName} · {info.PhysicalProcessors} 颗 / {info.LogicalProcessors} 逻辑核心";
        MotherboardText = string.IsNullOrWhiteSpace(info.Motherboard) ? "主板：未知" : $"主板：{info.Motherboard}";
        BiosText = string.IsNullOrWhiteSpace(info.BiosVersion) ? "BIOS：未知" : $"BIOS：{info.BiosVersion}";
        MemoryText = string.Create(CultureInfo.InvariantCulture, $"内存：{info.TotalPhysicalMemory / 1024d / 1024d / 1024d:N1} GB");
        OsText = $"{info.OperatingSystem} · {info.OsVersion}";

        GraphicsCards = info.GraphicsCards
            .Select(card => new GraphicsCardRow(
                $"{card.Name} · {card.CategoryDescription}",
                [
                    new("厂商", card.Manufacturer),
                    new("显存", card.MemoryBytes > 0 ? DiskCleanerFormat.Bytes((long)card.MemoryBytes) : "未知"),
                    new("驱动", string.IsNullOrWhiteSpace(card.DriverVersion) ? "未知" : card.DriverVersion),
                    new("输出", string.IsNullOrWhiteSpace(card.VideoModeDescription) ? "未知" : card.VideoModeDescription)
                ]))
            .ToList();

        StorageItems = info.Storage.Select(disk => $"{disk.Model} · {DiskCleanerFormat.Bytes(disk.SizeBytes)}").ToList();
        Drives = info.Drives
            .Select(drive => new DriveUsageRow(
                drive.DriveLetter,
                drive.Label,
                DiskCleanerFormat.Bytes(drive.TotalBytes),
                DiskCleanerFormat.Bytes(drive.UsedBytes),
                string.Create(CultureInfo.InvariantCulture, $"{drive.UsedPercent:F1}%")))
            .ToList();
        NetworkItems = info.NetworkAdapters
            .Select(adapter => $"{adapter.Name} · {adapter.MacAddress} · {(adapter.IsEnabled ? "已启用" : "未启用")}")
            .ToList();

        HardwareInfoUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 依据快照给出 CPU 温度缺失的可操作提示；一切正常时返回 null（隐藏提示行）。
    ///
    /// 两种成因必须分开：缺驱动要用户去 pawnio.eu 装；已装但未提权只需重启程序。
    /// </summary>
    internal static string? DescribeCpuTemperatureGap(SensorSnapshot snapshot)
    {
        if (snapshot.CpuTemperatureNeedsKernelDriver)
        {
            return "CPU 温度需要内核驱动 PawnIO（LibreHardwareMonitor 0.9.6 起用它替代被 Defender 下架的 WinRing0）。"
                + "请从 pawnio.eu 安装后重启本程序；GPU 与硬盘温度不受影响。";
        }

        if (snapshot.CpuTemperatureNeedsElevation)
        {
            return "已检测到 PawnIO，但读取 CPU 温度需要管理员权限。请以管理员身份重启本程序；"
                + "GPU 与硬盘温度不受影响。";
        }

        return null;
    }

    internal static string FormatTemperature(double celsius)
        => celsius <= 0 ? "--" : string.Create(CultureInfo.InvariantCulture, $"{celsius:F1} ℃");

    internal static string FormatBytesPerSecond(double value) => value switch
    {
        >= 1024 * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024 / 1024 / 1024:F1} GB/s"),
        >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024 / 1024:F1} MB/s"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024:F1} KB/s"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{value:F0} B/s")
    };
}
