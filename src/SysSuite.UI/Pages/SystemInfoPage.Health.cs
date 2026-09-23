using System.Windows;
using System.Windows.Media;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// 硬盘健康（T1.3）的绘制逻辑。
///
/// 设计要点：健康数据"读不到"与"读到了但是良好"必须在视觉上截然不同 ——
/// 前者一律显示"未知"灰色徽章并附原因，绝不用绿色徽章掩盖权限不足。
/// 判定在 ViewModel，这里只把 LevelKey 映射成颜色。
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

    private void OnDriveHealthUpdated(object? sender, DriveHealthEventArgs args)
    {
        if (args.Rows is null || args.Rows.Count == 0)
        {
            DriveHealthList.Visibility = Visibility.Collapsed;
            DriveHealthHintText.Visibility = Visibility.Visible;
            DriveHealthHintText.Text = args.Summary;

            // 权限不足时给出可操作的出路，而不是只让用户看到一句"没权限"
            DriveHealthElevateButton.Visibility = args.RequiresElevation ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        DriveHealthElevateButton.Visibility = Visibility.Collapsed;
        DriveHealthList.ItemsSource = args.Rows.Select(BuildDriveHealthRow).ToList();
        DriveHealthList.Visibility = Visibility.Visible;
        DriveHealthHintText.Visibility = Visibility.Visible;
        DriveHealthHintText.Text = args.Summary;
    }

    private static DriveHealthDisplayRow BuildDriveHealthRow(DriveHealthRow row)
    {
        var (background, foreground) = row.LevelKey switch
        {
            "Good" => (HealthGoodBackground, HealthGoodForeground),
            "Warning" => (HealthWarningBackground, HealthWarningForeground),
            "Critical" => (HealthCriticalBackground, HealthCriticalForeground),
            _ => (HealthUnknownBackground, HealthUnknownForeground)
        };

        return new DriveHealthDisplayRow(
            row.Title,
            row.LevelText,
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(background)),
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(foreground)),
            row.DetailText);
    }

    private sealed record DriveHealthDisplayRow(
        string Title,
        string LevelText,
        Brush BadgeBackground,
        Brush BadgeForeground,
        string DetailText);
}
