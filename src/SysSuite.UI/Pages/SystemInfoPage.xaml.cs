using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
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
    private readonly IReportExporter reportExporter;
    private readonly ISensorService sensorService;
    private readonly ISmartService smartService;
    private readonly IElevationService elevationService;
    private HardwareInfo? lastHardwareInfo;

    public SystemInfoPage()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        hardwareInfoService = services.GetRequiredService<IHardwareInfoService>();
        monitorService = services.GetRequiredService<IMonitorService>();
        reportExporter = services.GetRequiredService<IReportExporter>();
        sensorService = services.GetRequiredService<ISensorService>();
        smartService = services.GetRequiredService<ISmartService>();
        elevationService = services.GetRequiredService<IElevationService>();
        monitorService.SampleReady += OnSampleReady;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        monitorService.Start();
        UpdatePauseResumeButton();
        UpdateWindowButtons();
        await RefreshAsync();
        await RefreshSensorsAsync();
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

    private async void OnExportReportClick(object sender, RoutedEventArgs args)
    {
        if (lastHardwareInfo is null)
        {
            StatusText.Text = "请先刷新获取硬件信息，再导出报告。";
            return;
        }

        // Filter 顺序必须与 ReportFormats.All 保持一致，才能直接由 FilterIndex 映射到格式
        var dialog = new SaveFileDialog
        {
            Title = "导出系统信息报告",
            FileName = BuildDefaultReportName(ReportFormat.Html),
            Filter = "文本报告 (*.txt)|*.txt|网页报告 (*.html)|*.html|JSON 数据 (*.json)|*.json",
            FilterIndex = 2,
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var format = ReportFormats.All[Math.Clamp(dialog.FilterIndex - 1, 0, ReportFormats.All.Count - 1)];
        StatusText.Text = "正在导出报告...";

        var result = await reportExporter.ExportAsync(lastHardwareInfo, format, dialog.FileName);
        StatusText.Text = result.IsSuccess
            ? $"已导出{ReportFormats.GetDisplayName(format)}报告：{result.Value}"
            : $"导出失败：{result.Message}";
    }

    private static string BuildDefaultReportName(ReportFormat format)
        => string.Create(CultureInfo.InvariantCulture, $"SysSuite-系统信息-{DateTime.Now:yyyyMMdd-HHmmss}{ReportFormats.GetExtension(format)}");

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
        lastHardwareInfo = info;
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
        Dispatcher.BeginInvoke(async () =>
        {
            CpuUsageText.Text = $"{sample.CpuUsagePercent:F1}%";
            MemoryUsageText.Text = $"{sample.MemoryUsedPercent:F1}%";
            DiskReadText.Text = FormatBytesPerSecond(sample.DiskReadBytesPerSecond);
            DiskWriteText.Text = FormatBytesPerSecond(sample.DiskWriteBytesPerSecond);
            NetworkText.Text = $"{FormatBytesPerSecond(sample.NetworkReceivedBytesPerSecond)} / {FormatBytesPerSecond(sample.NetworkSentBytesPerSecond)}";

            RedrawTrend();

            if (++sensorTickCount % SensorRefreshTicks == 0)
            {
                await RefreshSensorsAsync();
            }
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
