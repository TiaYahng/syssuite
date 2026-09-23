using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.Pages;

/// <summary>
/// 硬盘健康（T1.3）的展示逻辑。与主页面分开，避免单文件超过 300 行门禁。
///
/// 设计要点：健康数据"读不到"与"读到了但是良好"必须在视觉上截然不同 ——
/// 前者一律显示"未知"灰色徽章并附原因，绝不用绿色徽章掩盖权限不足。
/// </summary>
public partial class SystemInfoPage
{
    private const string HealthGoodBackground = "#E6F4EA";
    private const string HealthGoodForeground = "#1E7B36";
    private const string HealthWarningBackground = "#FEF3D7";
    private const string HealthWarningForeground = "#9A6206";
    private const string HealthCriticalBackground = "#FCE8E6";
    private const string HealthCriticalForeground = "#B3261E";
    private const string HealthUnknownBackground = "#EDEDED";
    private const string HealthUnknownForeground = "#5F5F5F";

    private void ApplyDriveHealth(StorageHealthReport report)
    {
        if (report.Devices.Count == 0)
        {
            DriveHealthList.Visibility = Visibility.Collapsed;
            DriveHealthHintText.Visibility = Visibility.Visible;
            DriveHealthHintText.Text = report.RequiresElevation
                ? "需要管理员权限才能读取硬盘 SMART 数据。"
                : report.UnavailableReason ?? "未检测到可读取健康数据的物理硬盘。";

            // 权限不足时给出可操作的出路，而不是只让用户看到一句"没权限"
            DriveHealthElevateButton.Visibility = report.RequiresElevation
                ? Visibility.Visible
                : Visibility.Collapsed;
            return;
        }

        DriveHealthElevateButton.Visibility = Visibility.Collapsed;

        var rows = new List<DriveHealthRow>(report.Devices.Count);
        foreach (var device in report.Devices)
        {
            rows.Add(BuildDriveHealthRow(device));
        }

        DriveHealthList.ItemsSource = rows;
        DriveHealthList.Visibility = Visibility.Visible;

        DriveHealthHintText.Visibility = Visibility.Visible;
        var degraded = 0;
        foreach (var device in report.Devices)
        {
            if (!device.IsAvailable || device.Level != StorageHealthLevel.Good)
            {
                degraded++;
            }
        }

        DriveHealthHintText.Text = degraded == 0
            ? string.Create(CultureInfo.InvariantCulture, $"共 {report.Devices.Count} 块硬盘，健康状态正常。")
            : string.Create(CultureInfo.InvariantCulture, $"共 {report.Devices.Count} 块硬盘，其中 {degraded} 块需要关注。");
    }

    private static DriveHealthRow BuildDriveHealthRow(StorageDeviceHealth device)
    {
        var (background, foreground) = device.Level switch
        {
            StorageHealthLevel.Good => (HealthGoodBackground, HealthGoodForeground),
            StorageHealthLevel.Warning => (HealthWarningBackground, HealthWarningForeground),
            StorageHealthLevel.Critical => (HealthCriticalBackground, HealthCriticalForeground),
            _ => (HealthUnknownBackground, HealthUnknownForeground),
        };

        return new DriveHealthRow(
            device.DisplayName,
            device.LevelText,
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(background)),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground)),
            BuildDriveHealthDetail(device));
    }

    /// <summary>
    /// 只展示真正读到的项目；读不到的字段整体省略，避免出现"温度 0℃"这类误导性数字。
    /// </summary>
    private static string BuildDriveHealthDetail(StorageDeviceHealth device)
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

    private sealed record DriveHealthRow(
        string Title,
        string LevelText,
        Brush BadgeBackground,
        Brush BadgeForeground,
        string DetailText);
}
