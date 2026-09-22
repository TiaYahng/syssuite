using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.Pages;

public partial class SystemInfoPage : UserControl
{
    private static readonly DependencyProperty ContentVerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "ContentVerticalOffset",
        typeof(double),
        typeof(SystemInfoPage),
        new PropertyMetadata(0d, OnContentVerticalOffsetChanged));

    private readonly IHardwareInfoService hardwareInfoService;
    private readonly IMonitorService monitorService;

    public SystemInfoPage()
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
        monitorService.StopMonitoring();
        monitorService.SampleReady -= OnSampleReady;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs args)
    {
        await RefreshAsync();
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

    private void OnOpenEnvironmentVariablesClick(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = "sysdm.cpl,EditEnvironmentVariables",
                UseShellExecute = true
            });
        }
        catch (Win32Exception exception)
        {
            MessageBox.Show(exception.Message, "无法打开环境变量", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "正在获取硬件信息...";
        var result = await hardwareInfoService.GetHardwareInfoAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            StatusText.Text = result.Message;
            return;
        }

        var info = result.Value;
        StatusText.Text = $"计算机：{info.ComputerName}";
        CpuText.Text = $"{info.CpuName} · {info.PhysicalProcessors} 颗 / {info.LogicalProcessors} 逻辑核心";
        MotherboardText.Text = string.IsNullOrWhiteSpace(info.Motherboard) ? "主板：未知" : $"主板：{info.Motherboard}";
        BiosText.Text = string.IsNullOrWhiteSpace(info.BiosVersion) ? "BIOS：未知" : $"BIOS：{info.BiosVersion}";
        MemoryText.Text = $"内存：{info.TotalPhysicalMemory / 1024d / 1024d / 1024d:N1} GB";
        OsText.Text = $"{info.OperatingSystem} · {info.OsVersion}";

        GraphicsList.ItemsSource = info.GraphicsCards.Select(card => new GraphicsCardRow(
            $"{card.Name} · {card.CategoryDescription}",
            BuildGraphicsDetail(card))).ToArray();

        StorageList.Items.Clear();
        foreach (var disk in info.Storage)
        {
            StorageList.Items.Add($"{disk.Model} · {FormatBytes(disk.SizeBytes)}");
        }

        DriveList.ItemsSource = info.Drives.Select(drive => new DriveUsageRow(
            drive.DriveLetter,
            drive.Label,
            FormatBytes(drive.TotalBytes),
            FormatBytes(drive.UsedBytes),
            $"{drive.UsedPercent:F1}%"));

        NetworkList.Items.Clear();
        foreach (var adapter in info.NetworkAdapters)
        {
            NetworkList.Items.Add($"{adapter.Name} · {adapter.MacAddress} · {(adapter.IsEnabled ? "已启用" : "未启用")}");
        }
    }

    private void OnSampleReady(object? sender, MonitorSample sample)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CpuUsageText.Text = $"{sample.CpuUsagePercent:F1}%";
            MemoryUsageText.Text = $"{sample.MemoryUsedPercent:F1}%";
            DiskReadText.Text = FormatBytesPerSecond(sample.DiskReadBytesPerSecond);
            DiskWriteText.Text = FormatBytesPerSecond(sample.DiskWriteBytesPerSecond);
            NetworkText.Text = $"{FormatBytesPerSecond(sample.NetworkReceivedBytesPerSecond)} / {FormatBytesPerSecond(sample.NetworkSentBytesPerSecond)}";
        });
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

    private static List<GraphicsDetailItem> BuildGraphicsDetail(GraphicsCardInfo card)
    {
        return new List<GraphicsDetailItem>
        {
            new("厂商", card.Manufacturer),
            new("显存", card.MemoryBytes > 0 ? FormatBytes((long)card.MemoryBytes) : "未知"),
            new("驱动", string.IsNullOrWhiteSpace(card.DriverVersion) ? "未知" : card.DriverVersion),
            new("输出", string.IsNullOrWhiteSpace(card.VideoModeDescription) ? "未知" : card.VideoModeDescription)
        };
    }

    private sealed record DriveUsageRow(
        string DriveLetter,
        string Label,
        string TotalText,
        string UsedText,
        string UsageText);

    private sealed record GraphicsDetailItem(string Label, string Value);

    private sealed record GraphicsCardRow(string Title, IReadOnlyList<GraphicsDetailItem> Details);
}
