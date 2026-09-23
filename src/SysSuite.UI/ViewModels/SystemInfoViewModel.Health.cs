using System.Globalization;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 硬盘健康（T1.3）的数据加工：把 SMART 报告转成行与汇总文本。
///
/// 设计要点：健康数据"读不到"与"读到了但是良好"必须在视觉上截然不同 ——
/// 前者一律给出原因并要求提权出路，绝不用绿色徽章掩盖权限不足。
/// 画刷映射在 Page 侧，这里只产出 <c>LevelKey</c> 语义标签。
/// </summary>
public sealed partial class SystemInfoViewModel
{
    private static DriveHealthEventArgs BuildDriveHealthEventArgs(StorageHealthReport report)
    {
        if (report.Devices.Count == 0)
        {
            var message = report.RequiresElevation
                ? "需要管理员权限才能读取硬盘 SMART 数据。"
                : report.UnavailableReason ?? "未检测到可读取健康数据的物理硬盘。";
            return new DriveHealthEventArgs(null, message, report.RequiresElevation);
        }

        var degraded = report.Devices.Count(device => !device.IsAvailable || device.Level != StorageHealthLevel.Good);
        var summary = degraded == 0
            ? string.Create(CultureInfo.InvariantCulture, $"共 {report.Devices.Count} 块硬盘，健康状态正常。")
            : string.Create(CultureInfo.InvariantCulture, $"共 {report.Devices.Count} 块硬盘，其中 {degraded} 块需要关注。");

        var rows = report.Devices
            .Select(device => new DriveHealthRow(
                device.DisplayName,
                device.LevelText,
                device.Level switch
                {
                    StorageHealthLevel.Good => "Good",
                    StorageHealthLevel.Warning => "Warning",
                    StorageHealthLevel.Critical => "Critical",
                    _ => "Unknown"
                },
                BuildDriveHealthDetail(device)))
            .ToList();

        return new DriveHealthEventArgs(rows, summary, false);
    }

    /// <summary>
    /// 只展示真正读到的项目；读不到的字段整体省略，避免出现"温度 0℃"这类误导性数字。
    /// </summary>
    internal static string BuildDriveHealthDetail(StorageDeviceHealth device)
    {
        if (!device.IsAvailable)
        {
            return "无法读取该硬盘的 SMART 数据。";
        }

        var parts = new List<string>(4);
        if (device.TemperatureCelsius > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"温度 {device.TemperatureCelsius} ℃"));
        }

        if (device.RemainingLifePercent is { } life)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"剩余寿命 {life}%"));
        }

        if (device.PowerOnHours > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"通电 {device.PowerOnDurationText}"));
        }

        if (device.ReallocatedSectors is > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"重映射扇区 {device.ReallocatedSectors}"));
        }

        if (device.PendingSectors is > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"待处理扇区 {device.PendingSectors}"));
        }

        if (device.UncorrectableErrors is > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"不可纠正错误 {device.UncorrectableErrors}"));
        }

        return parts.Count == 0 ? "已通过 SMART 检查。" : string.Join(" · ", parts);
    }
}
