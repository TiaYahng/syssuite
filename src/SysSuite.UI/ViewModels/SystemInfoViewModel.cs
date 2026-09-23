using System.Globalization;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.Pages;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 系统信息页面 ViewModel（D1：原先取数、传感器判定、硬盘健康、报告导出全在四个 partial 文件里）。
///
/// 这里承载"取数与判定"，其中两类判定值得单独说明：
///   1. CPU 温度缺失有**两种**成因（缺内核驱动 / 已装驱动但未提权），处置动作完全不同，
///      必须分开提示，不能笼统说"传感器不可用"。
///   2. 硬盘健康"读不到"与"读到了但良好"必须区分，绝不用绿色徽章掩盖权限不足。
///
/// 颜色与画刷属于展示层，由 Page 依 <see cref="DriveHealthLevelKey"/> 解析。
/// </summary>
public sealed partial class SystemInfoViewModel : PageViewModelBase
{
    private readonly IHardwareInfoService hardwareInfoService;
    private readonly IMonitorService monitorService;
    private readonly IReportExporter reportExporter;
    private readonly ISensorService sensorService;
    private readonly ISmartService smartService;
    private readonly IElevationService elevationService;

    /// <summary>传感器刷新节流：每 N 个监控采样（2 秒一个）读一次硬件温度。</summary>
    internal const int SensorRefreshTicks = 5;

    private int sensorTickCount;

    public SystemInfoViewModel(
        IHardwareInfoService hardwareInfoService,
        IMonitorService monitorService,
        IReportExporter reportExporter,
        ISensorService sensorService,
        ISmartService smartService,
        IElevationService elevationService)
    {
        this.hardwareInfoService = hardwareInfoService;
        this.monitorService = monitorService;
        this.reportExporter = reportExporter;
        this.sensorService = sensorService;
        this.smartService = smartService;
        this.elevationService = elevationService;
        monitorService.SampleReady += OnSampleReady;
    }

    public event EventHandler<MonitorSample>? SampleReady;

    public event EventHandler? HardwareInfoUpdated;

    public event EventHandler<SensorSnapshotEventArgs>? SensorsUpdated;

    public event EventHandler<DriveHealthEventArgs>? DriveHealthUpdated;

    public HardwareInfo? LastHardwareInfo { get; private set; }

    public string CpuText { get; private set; } = "—";

    public string MotherboardText { get; private set; } = "主板：未知";

    public string BiosText { get; private set; } = "BIOS：未知";

    public string MemoryText { get; private set; } = "内存：—";

    public string OsText { get; private set; } = "—";

    public List<GraphicsCardRow> GraphicsCards { get; private set; } = [];

    public List<string> StorageItems { get; private set; } = [];

    public List<DriveUsageRow> Drives { get; private set; } = [];

    public List<string> NetworkItems { get; private set; } = [];

    public bool IsMonitoringPaused => monitorService.IsPaused;

    public MonitorWindow CurrentWindow => monitorService.Window;

    /// <summary>趋势图数据源。绘制全在 Page，但取数走 ViewModel。</summary>
    public IReadOnlyList<MonitorSample> GetHistory() => monitorService.GetHistory();

    public async Task ActivateAsync()
    {
        monitorService.Start();
        await RefreshHardwareAsync();
        await RefreshSensorsAsync();
    }

    public override void Dispose()
    {
        monitorService.SampleReady -= OnSampleReady;
        monitorService.StopMonitoring();
        base.Dispose();
    }

    public async Task RefreshHardwareAsync()
    {
        await RunBusyAsync(
            async () =>
            {
                var result = await hardwareInfoService.GetHardwareInfoAsync();
                if (!result.IsSuccess || result.Value is null)
                {
                    SetError(result.Message);
                    return;
                }

                ApplyHardwareInfo(result.Value);
            },
            "正在获取硬件信息...");
    }

    /// <summary>暂停/继续监控，返回切换后的状态。</summary>
    public bool ToggleMonitoring()
    {
        if (monitorService.IsPaused)
        {
            monitorService.ResumeMonitoring();
        }
        else
        {
            monitorService.Pause();
        }

        OnPropertyChanged(nameof(IsMonitoringPaused));
        return monitorService.IsPaused;
    }

    public MonitorWindow ApplyWindow(MonitorWindow window)
    {
        monitorService.SetWindow(window);
        OnPropertyChanged(nameof(CurrentWindow));
        return monitorService.Window;
    }

    /// <summary>
    /// 传感器查询比 WMI 采样昂贵得多（LibreHardwareMonitor 要跑 SMBus/驱动 IO），
    /// 因此每 5 个采样周期（约 10 秒）刷新一次，而不是每次采样都读。
    /// </summary>
    public async Task RefreshSensorsAsync()
    {
        var result = await sensorService.GetSensorsAsync();
        var snapshot = result.IsSuccess ? result.Value : null;

        if (snapshot is not { IsAvailable: true })
        {
            // 读不到传感器是正常情形（虚拟机 / 无传感器主板），用占位符而非报错
            SensorsUpdated?.Invoke(this, new SensorSnapshotEventArgs(null, "--", "--", null));
            return;
        }

        SensorsUpdated?.Invoke(this, new SensorSnapshotEventArgs(
            snapshot,
            FormatTemperature(snapshot.MaxTemperatureOf(SensorHardwareClass.Cpu)),
            FormatTemperature(snapshot.MaxTemperatureOf(SensorHardwareClass.Gpu)),
            DescribeCpuTemperatureGap(snapshot)));
    }

    public async Task QueryDriveHealthAsync()
    {
        var result = await smartService.GetStorageHealthAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            DriveHealthUpdated?.Invoke(this, new DriveHealthEventArgs(null, $"读取失败：{result.Message}", false));
            return;
        }

        DriveHealthUpdated?.Invoke(this, BuildDriveHealthEventArgs(result.Value));
    }

    /// <summary>
    /// 权限不足时以管理员身份重启。
    ///
    /// 注意：新进程起来后本进程**必须**尽快退出，否则新实例会撞上
    /// <c>App</c> 里的单实例 Mutex 而直接自我关闭。返回 true 表示调用方应立即 Shutdown。
    /// </summary>
    public bool TryElevateForDriveHealth()
    {
        var result = elevationService.RestartElevated();
        if (result.Requested)
        {
            SetStatus("已请求提权，正在以管理员身份重启...");
            return true;
        }

        SetStatus(result.UserDeclined
            ? "已取消提权。可右键程序图标选择「以管理员身份运行」重试。"
            : $"提权失败：{result.FailureReason}");
        return false;
    }

    public async Task ExportReportAsync(ReportFormat format, string path)
    {
        if (LastHardwareInfo is null)
        {
            SetStatus("请先刷新获取硬件信息，再导出报告。");
            return;
        }

        SetStatus("正在导出报告...");
        var result = await reportExporter.ExportAsync(LastHardwareInfo, format, path);
        if (result.IsSuccess)
        {
            SetStatus($"已导出{ReportFormats.GetDisplayName(format)}报告：{result.Value}");
        }
        else
        {
            SetError($"导出失败：{result.Message}");
        }
    }

    internal static string BuildDefaultReportName(ReportFormat format)
        => string.Create(CultureInfo.InvariantCulture, $"SysSuite-系统信息-{DateTime.Now:yyyyMMdd-HHmmss}{ReportFormats.GetExtension(format)}");

    private void OnSampleReady(object? sender, MonitorSample sample)
        => SampleReady?.Invoke(this, sample);

    /// <summary>监控采样推进节流计数；返回 true 表示本轮该刷新传感器。</summary>
    internal bool ShouldRefreshSensorsOnTick() => ++sensorTickCount % SensorRefreshTicks == 0;
}

public sealed record DriveUsageRow(string DriveLetter, string Label, string TotalText, string UsedText, string UsageText);

public sealed record GraphicsDetailItem(string Label, string Value);

public sealed record GraphicsCardRow(string Title, IReadOnlyList<GraphicsDetailItem> Details);

/// <summary>传感器快照事件：CPU/GPU 温度文本与可选的缺失原因提示。</summary>
public sealed class SensorSnapshotEventArgs(SensorSnapshot? snapshot, string cpuTemperature, string gpuTemperature, string? hint) : EventArgs
{
    public SensorSnapshot? Snapshot { get; } = snapshot;

    public string CpuTemperature { get; } = cpuTemperature;

    public string GpuTemperature { get; } = gpuTemperature;

    public string? Hint { get; } = hint;

    public string StatusText => Snapshot is null
        ? "不可用"
        : string.Create(CultureInfo.InvariantCulture, $"{Snapshot.Readings.Count} 个读数");
}

/// <summary>硬盘健康事件：行数据 + 汇总文本 + 是否需要提权。</summary>
public sealed class DriveHealthEventArgs(IReadOnlyList<DriveHealthRow>? rows, string summary, bool requiresElevation) : EventArgs
{
    public IReadOnlyList<DriveHealthRow>? Rows { get; } = rows;

    public string Summary { get; } = summary;

    public bool RequiresElevation { get; } = requiresElevation;
}

/// <summary>硬盘健康行。<c>LevelKey</c> 为 Good/Warning/Critical/Unknown，由 Page 映射到画刷。</summary>
public sealed record DriveHealthRow(string Title, string LevelText, string LevelKey, string DetailText);
