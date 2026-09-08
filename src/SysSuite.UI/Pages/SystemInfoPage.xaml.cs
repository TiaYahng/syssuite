using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.Pages;

public partial class SystemInfoPage : UserControl
{
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

    private sealed record DriveUsageRow(
        string DriveLetter,
        string Label,
        string TotalText,
        string UsedText,
        string UsageText);
}
