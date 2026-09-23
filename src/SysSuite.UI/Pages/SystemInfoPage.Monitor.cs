using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// 实时监控（T1.4）控件状态与传感器（T1.2）/硬盘健康（T1.3）触发的展示逻辑。
///
/// 判定与文本组装已迁到 <see cref="SystemInfoViewModel"/>；这里只做"把结果画上去"
/// 与调用 ViewModel 的动作。
/// </summary>
public partial class SystemInfoPage
{
    private void OnPauseResumeClick(object sender, RoutedEventArgs args)
    {
        viewModel.ToggleMonitoring();
        UpdatePauseResumeButton();
    }

    private void OnWindowOneMinuteClick(object sender, RoutedEventArgs args)
    {
        viewModel.ApplyWindow(MonitorWindow.OneMinute);
        UpdateWindowButtons();
        RedrawTrend();
    }

    private void OnWindowFiveMinutesClick(object sender, RoutedEventArgs args)
    {
        viewModel.ApplyWindow(MonitorWindow.FiveMinutes);
        UpdateWindowButtons();
        RedrawTrend();
    }

    private void UpdatePauseResumeButton()
        => PauseResumeButton.Content = viewModel.IsMonitoringPaused ? "继续" : "暂停";

    /// <summary>选中的时间窗按钮用强调色实底，另一个用描边，形成互斥选择态。</summary>
    private void UpdateWindowButtons()
    {
        var oneMinute = viewModel.CurrentWindow == MonitorWindow.OneMinute;
        ApplyToggleState(WindowOneMinuteButton, oneMinute);
        ApplyToggleState(WindowFiveMinuteButton, !oneMinute);
    }

    private void ApplyToggleState(System.Windows.Controls.Button button, bool selected)
    {
        button.Background = (Brush)FindResource(selected ? "App.Accent" : "App.Background");
        button.Foreground = (Brush)FindResource(selected ? "App.AccentText" : "App.Text");
        button.BorderBrush = (Brush)FindResource(selected ? "App.Accent" : "App.Border");
        button.BorderThickness = new Thickness(1);
    }

    private void OnSensorsUpdated(object? sender, SensorSnapshotEventArgs args)
    {
        SensorStatusText.Text = args.StatusText;
        CpuTemperatureText.Text = args.CpuTemperature;
        GpuTemperatureText.Text = args.GpuTemperature;
        ShowSensorHint(args.Hint);
    }

    /// <summary>显示/隐藏传感器说明行；传 null 表示隐藏。</summary>
    private void ShowSensorHint(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            SensorHintText.Visibility = Visibility.Collapsed;
            SensorHintText.Text = string.Empty;
            return;
        }

        SensorHintText.Text = message;
        SensorHintText.Visibility = Visibility.Visible;
    }

    private async void OnQueryDriveHealthClick(object sender, RoutedEventArgs args)
    {
        DriveHealthButton.IsEnabled = false;
        DriveHealthElevateButton.Visibility = Visibility.Collapsed;
        DriveHealthHintText.Visibility = Visibility.Visible;
        DriveHealthHintText.Text = "正在读取硬盘健康数据...";

        try
        {
            await viewModel.QueryDriveHealthAsync();
        }
        finally
        {
            DriveHealthButton.IsEnabled = true;
        }
    }

    private void OnElevateForDriveHealthClick(object sender, RoutedEventArgs args)
    {
        DriveHealthElevateButton.IsEnabled = false;
        if (!viewModel.TryElevateForDriveHealth())
        {
            DriveHealthElevateButton.IsEnabled = true;
            DriveHealthHintText.Text = viewModel.BusyText;
            return;
        }

        DriveHealthHintText.Text = viewModel.BusyText;
        Application.Current.Shutdown();
    }

    private static string FormatTemperature(double celsius) => SystemInfoViewModel.FormatTemperature(celsius);

    private static string FormatBytesPerSecond(double value) => SystemInfoViewModel.FormatBytesPerSecond(value);

    private static string FormatBytes(long value)
        => value switch
        {
            >= 1024 * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d / 1024d / 1024d:N1} GB"),
            >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{value / 1024d / 1024d:N1} MB"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{value:N0} B")
        };
}
