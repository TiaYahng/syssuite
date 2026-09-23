using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.Interop;
using SysSuite.UI.Pages;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 仪表盘页面 ViewModel（D1：原先硬件汇总与监控采样都在 <c>DashboardPage.xaml.cs</c>）。
///
/// 监控采样的数值格式化（弧线比例、速率百分比）属于展示逻辑、依赖控件，留在 Page；
/// 这里只负责"拿数据 + 转成可读文本"，并把原生模块自检结果也收敛进来。
/// </summary>
public sealed partial class DashboardViewModel : PageViewModelBase
{
    private readonly IHardwareInfoService hardwareInfoService;
    private readonly IMonitorService monitorService;

    public DashboardViewModel(IHardwareInfoService hardwareInfoService, IMonitorService monitorService)
    {
        this.hardwareInfoService = hardwareInfoService;
        this.monitorService = monitorService;
        monitorService.SampleReady += OnSampleReady;
    }

    /// <summary>新的监控采样点。</summary>
    public event EventHandler<MonitorSample>? SampleReady;

    public string CpuSummary { get; private set; } = "CPU：—";

    public string MemorySummary { get; private set; } = "内存：—";

    public string DiskSummary { get; private set; } = "磁盘：—";

    public string GpuSummary { get; private set; } = "显卡：—";

    public async Task ActivateAsync()
    {
        monitorService.Start();
        await RefreshCoreAsync();
    }

    public override void Dispose()
    {
        monitorService.SampleReady -= OnSampleReady;
        base.Dispose();
    }

    protected override Task RefreshAsync() => RefreshCoreAsync();

    /// <summary>T0.2 自检入口：验证 C# → C++ 调用链是否可用（见 docs/native-abi.md）。</summary>
    public void RunNativeSelfCheck()
    {
        var abiStatus = NativeInterop.GetAbiVersion(out var abiVersion);
        if (abiStatus != NativeStatus.Ok)
        {
            SetError($"原生模块：{NativeInterop.Describe(abiStatus)}");
            return;
        }

        var versionStatus = NativeInterop.GetVersion(out var version);
        SetStatus(versionStatus == NativeStatus.Ok
            ? $"原生模块就绪：v{version}（ABI {abiVersion}）"
            : $"原生模块：{NativeInterop.Describe(versionStatus)}");
    }

    private async Task RefreshCoreAsync()
    {
        var result = await hardwareInfoService.GetHardwareInfoAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            SetError(result.Message);
            return;
        }

        var info = result.Value;
        SetStatus($"计算机：{info.ComputerName}");
        CpuSummary = $"CPU：{info.CpuName}";
        MemorySummary = $"内存：{FormatBytes((long)info.TotalPhysicalMemory)}";
        DiskSummary = $"磁盘：{FormatBytes(info.Drives.Sum(drive => drive.TotalBytes))} / 剩余 {FormatBytes(info.Drives.Sum(drive => drive.FreeBytes))}";
        GpuSummary = $"显卡：{(info.GraphicsCards.Count > 0 ? info.GraphicsCards[0].Name : "未知")}";
        OnPropertyChanged(nameof(CpuSummary));
        OnPropertyChanged(nameof(MemorySummary));
        OnPropertyChanged(nameof(DiskSummary));
        OnPropertyChanged(nameof(GpuSummary));
    }

    private void OnSampleReady(object? sender, MonitorSample sample)
        => SampleReady?.Invoke(this, sample);

    /// <summary>把每秒字节数限幅到满量程 20 MB/s 的百分比，供弧线使用。</summary>
    internal static double RatePercent(double bytesPerSecond)
        => Math.Clamp(bytesPerSecond / (20d * 1024d * 1024d) * 100d, 0d, 100d);

    internal static string FormatBytesPerSecond(double value) => value switch
    {
        >= 1024 * 1024 * 1024 => $"{value / 1024 / 1024 / 1024:F1} GB/s",
        >= 1024 * 1024 => $"{value / 1024 / 1024:F1} MB/s",
        >= 1024 => $"{value / 1024:F1} KB/s",
        _ => $"{value:F0} B/s"
    };

    internal static string FormatBytes(long value) => value switch
    {
        >= 1024 * 1024 * 1024 => $"{value / 1024d / 1024d / 1024d:N1} GB",
        >= 1024 * 1024 => $"{value / 1024d / 1024d:N1} MB",
        _ => $"{value:N0} B"
    };
}
