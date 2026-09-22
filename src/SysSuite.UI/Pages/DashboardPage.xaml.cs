using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.Interop;

namespace SysSuite.UI.Pages;

public partial class DashboardPage : UserControl
{
    private static readonly DependencyProperty ContentVerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "ContentVerticalOffset",
        typeof(double),
        typeof(DashboardPage),
        new PropertyMetadata(0d, OnContentVerticalOffsetChanged));

    private readonly IHardwareInfoService hardwareInfoService;
    private readonly IMonitorService monitorService;

    public DashboardPage()
    {
        InitializeComponent();
        hardwareInfoService = ((App)Application.Current).Services.GetRequiredService<IHardwareInfoService>();
        monitorService = ((App)Application.Current).Services.GetRequiredService<IMonitorService>();
        monitorService.SampleReady += OnSampleReady;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        monitorService.Start();
        await RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        monitorService.SampleReady -= OnSampleReady;
    }

    private void OnContentScrollPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (ContentScroll.ScrollableHeight <= 0)
        {
            return;
        }

        args.Handled = true;
        var currentTarget = (double)ContentScroll.GetValue(ContentVerticalOffsetProperty);
        var target = Math.Clamp(currentTarget - args.Delta, 0, ContentScroll.ScrollableHeight);
        var animation = new DoubleAnimation(ContentScroll.VerticalOffset, target, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ContentScroll.BeginAnimation(ContentVerticalOffsetProperty, animation);
    }

    private static void OnContentVerticalOffsetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset((double)args.NewValue);
        }
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "正在获取系统状态...";
        var result = await hardwareInfoService.GetHardwareInfoAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            StatusText.Text = result.Message;
            return;
        }

        var info = result.Value;
        StatusText.Text = $"计算机：{info.ComputerName}";
        CpuSummaryText.Text = $"CPU：{info.CpuName}";
        MemorySummaryText.Text = $"内存：{FormatBytes((long)info.TotalPhysicalMemory)}";
        DiskSummaryText.Text = $"磁盘：{FormatBytes(info.Drives.Sum(drive => drive.TotalBytes))} / 剩余 {FormatBytes(info.Drives.Sum(drive => drive.FreeBytes))}";
        GpuSummaryText.Text = $"显卡：{(info.GraphicsCards.Count > 0 ? info.GraphicsCards[0].Name : "未知")}";
    }

    // T0.2 临时自检入口：验证 C# → C++ 调用链是否可用（见 docs/native-abi.md）
    private void OnNativeCheckClick(object sender, RoutedEventArgs args)
    {
        var abiStatus = NativeInterop.GetAbiVersion(out var abiVersion);
        if (abiStatus != NativeStatus.Ok)
        {
            StatusText.Text = $"原生模块：{NativeInterop.Describe(abiStatus)}";
            return;
        }

        var versionStatus = NativeInterop.GetVersion(out var version);
        StatusText.Text = versionStatus == NativeStatus.Ok
            ? $"原生模块就绪：v{version}（ABI {abiVersion}）"
            : $"原生模块：{NativeInterop.Describe(versionStatus)}";
    }

    private void OnSampleReady(object? sender, MonitorSample sample)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CpuPercentText.Text = $"{sample.CpuUsagePercent:F1}%";
            MemoryPercentText.Text = $"{sample.MemoryUsedPercent:F1}%";
            SetArc(CpuArc, sample.CpuUsagePercent);
            SetArc(MemoryArc, sample.MemoryUsedPercent);
            SetRate(DiskPercentText, DiskArc, Math.Max(sample.DiskReadBytesPerSecond, sample.DiskWriteBytesPerSecond));
            SetRate(NetworkPercentText, NetworkArc, Math.Max(sample.NetworkReceivedBytesPerSecond, sample.NetworkSentBytesPerSecond));
            DiskReadText.Text = FormatBytesPerSecond(sample.DiskReadBytesPerSecond);
            DiskWriteText.Text = FormatBytesPerSecond(sample.DiskWriteBytesPerSecond);
            NetworkText.Text = $"{FormatBytesPerSecond(sample.NetworkReceivedBytesPerSecond)} / {FormatBytesPerSecond(sample.NetworkSentBytesPerSecond)}";
        });
    }

    private static void SetArc(System.Windows.Shapes.Ellipse arc, double percent)
    {
        var ratio = Math.Clamp(percent / 100d, 0d, 1d);
        arc.StrokeDashArray = new System.Windows.Media.DoubleCollection([ratio * 1.57, 10]);
    }

    private static void SetRate(TextBlock text, System.Windows.Shapes.Ellipse arc, double bytesPerSecond)
    {
        var percent = Math.Clamp(bytesPerSecond / (20d * 1024d * 1024d) * 100d, 0d, 100d);
        text.Text = $"{percent:F1}%";
        SetArc(arc, percent);
    }

    private static string FormatBytesPerSecond(double value)
    {
        return value switch
        {
            >= 1024 * 1024 * 1024 => $"{value / 1024 / 1024 / 1024:F1} GB/s",
            >= 1024 * 1024 => $"{value / 1024 / 1024:F1} MB/s",
            >= 1024 => $"{value / 1024:F1} KB/s",
            _ => $"{value:F0} B/s"
        };
    }

    private static string FormatBytes(long value)
    {
        return value switch
        {
            >= 1024 * 1024 * 1024 => $"{value / 1024d / 1024d / 1024d:N1} GB",
            >= 1024 * 1024 => $"{value / 1024d / 1024d:N1} MB",
            _ => $"{value:N0} B"
        };
    }
}
