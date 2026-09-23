using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.Pages;

/// <summary>
/// 实时监控（T1.4）与传感器（T1.2）、硬盘健康（T1.3）的交互逻辑。
/// 从主页面文件拆出，满足单文件 ≤ 300 行的门禁。
/// </summary>
public partial class SystemInfoPage
{
    /// <summary>传感器刷新节流：每 N 个监控采样（2 秒一个）读一次硬件温度。</summary>
    private const int SensorRefreshTicks = 5;

    private int sensorTickCount;

    private void OnPauseResumeClick(object sender, RoutedEventArgs args)
    {
        if (monitorService.IsPaused)
        {
            monitorService.ResumeMonitoring();
        }
        else
        {
            monitorService.Pause();
        }

        UpdatePauseResumeButton();
    }

    private void OnWindowOneMinuteClick(object sender, RoutedEventArgs args) => ApplyWindow(MonitorWindow.OneMinute);

    private void OnWindowFiveMinutesClick(object sender, RoutedEventArgs args) => ApplyWindow(MonitorWindow.FiveMinutes);

    private void ApplyWindow(MonitorWindow window)
    {
        monitorService.SetWindow(window);
        UpdateWindowButtons();
        RedrawTrend();
    }

    private void UpdatePauseResumeButton()
    {
        PauseResumeButton.Content = monitorService.IsPaused ? "继续" : "暂停";
    }

    /// <summary>选中的时间窗按钮用强调色实底，另一个用描边，形成互斥选择态。</summary>
    private void UpdateWindowButtons()
    {
        var oneMinute = monitorService.Window == MonitorWindow.OneMinute;
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

    /// <summary>
    /// 传感器查询比 WMI 采样昂贵得多（LibreHardwareMonitor 要跑 SMBus/驱动 IO），
    /// 因此每 5 个采样周期（约 10 秒）刷新一次，而不是每次采样都读。
    /// </summary>
    private async Task RefreshSensorsAsync()
    {
        var result = await sensorService.GetSensorsAsync();
        var snapshot = result.IsSuccess ? result.Value : null;

        if (snapshot is not { IsAvailable: true })
        {
            // 读不到传感器是正常情形（虚拟机 / 无传感器主板），用占位符而非报错
            SensorStatusText.Text = "不可用";
            CpuTemperatureText.Text = "--";
            GpuTemperatureText.Text = "--";
            ShowSensorHint(null);
            return;
        }

        SensorStatusText.Text = string.Create(CultureInfo.InvariantCulture, $"{snapshot.Readings.Count} 个读数");

        // CPU 与 GPU 必须各取各的硬件分类温度。
        // 早前两者都调 MaxOf(Temperature)，结果是 GPU Hot Spot（89.8℃）被当成 CPU 温度显示 ——
        // 那个数字其实来自显卡，与 CPU 毫无关系。
        CpuTemperatureText.Text = FormatTemperature(snapshot.MaxTemperatureOf(SensorHardwareClass.Cpu));
        GpuTemperatureText.Text = FormatTemperature(snapshot.MaxTemperatureOf(SensorHardwareClass.Gpu));

        // CPU 温度缺失且缺 PawnIO 内核驱动时，必须说明原因 ——
        // 否则用户只会看到一个莫名的 "--"，误以为程序坏了。
        ShowSensorHint(snapshot.CpuTemperatureNeedsKernelDriver
            ? "CPU 温度需要内核驱动 PawnIO（LibreHardwareMonitor 0.9.6 起用它替代被 Defender 下架的 WinRing0）。"
              + "请从 pawnio.eu 安装并重启本程序；GPU 与硬盘温度不受影响。"
            : null);
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

    private static string FormatTemperature(double celsius)
        => celsius <= 0 ? "--" : string.Create(CultureInfo.InvariantCulture, $"{celsius:F1} ℃");

    private async void OnQueryDriveHealthClick(object sender, RoutedEventArgs args)
    {
        DriveHealthButton.IsEnabled = false;
        DriveHealthElevateButton.Visibility = Visibility.Collapsed;
        DriveHealthHintText.Visibility = Visibility.Visible;
        DriveHealthHintText.Text = "正在读取硬盘健康数据...";

        try
        {
            var result = await smartService.GetStorageHealthAsync();
            if (!result.IsSuccess || result.Value is null)
            {
                DriveHealthHintText.Text = $"读取失败：{result.Message}";
                return;
            }

            ApplyDriveHealth(result.Value);
        }
        finally
        {
            DriveHealthButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 权限不足时以管理员身份重启。
    ///
    /// 注意：新进程起来后本进程**必须**尽快退出，否则新实例会撞上
    /// <c>App</c> 里的单实例 `Mutex` 而直接自我关闭。
    /// </summary>
    private void OnElevateForDriveHealthClick(object sender, RoutedEventArgs args)
    {
        DriveHealthElevateButton.IsEnabled = false;
        var result = elevationService.RestartElevated();

        if (result.Requested)
        {
            DriveHealthHintText.Text = "已请求提权，正在以管理员身份重启...";
            Application.Current.Shutdown();
            return;
        }

        DriveHealthElevateButton.IsEnabled = true;
        DriveHealthHintText.Text = result.UserDeclined
            ? "已取消提权。可右键程序图标选择「以管理员身份运行」重试。"
            : $"提权失败：{result.FailureReason}";
    }
}
