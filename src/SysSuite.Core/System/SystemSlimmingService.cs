using System.Globalization;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 系统瘦身（T3.5）。
/// </summary>
/// <remarks>
/// 分工：本文件只做**门控与编排**，具体命令在 <c>.Dism.cs</c>，体积测量在 <c>.Targets.cs</c>。
///
/// 门控是双开关而不是单开关：<see cref="IsEnabled"/> 要求
/// <c>EnableExperimentalFeatures</c> **且** <c>EnableSystemSlimming</c>。
/// 单开关在这里不够用 —— 用户打开"实验性功能"往往是为了别的功能，
/// 不该顺带把组件存储清理也放出来。
///
/// 分析（只读）不限开关：非提权下也能跑，缺的数据如实标为"未知"，
/// 而不是拿 0 冒充 —— 用户得先看到"这里有多大"，才谈得上决定要不要清。
/// </remarks>
public sealed partial class SystemSlimmingService : ISystemSlimmingService
{
    internal const string DismExecutable = "dism.exe";
    internal const string CompactExecutable = "compact.exe";

    /// <summary>分析要读整个组件存储，慢机器上可能要几分钟。</summary>
    private static readonly TimeSpan AnalyzeTimeout = TimeSpan.FromMinutes(10);

    /// <summary>StartComponentCleanup 在积累多年的机器上跑半小时以上是常态。</summary>
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(90);

    private static readonly TimeSpan CompactTimeout = TimeSpan.FromMinutes(60);

    /// <summary>体积测量上限：超大目录不让它无限跑下去，达到上限即如实标注。</summary>
    internal const int MeasurementFileCap = 200_000;

    private readonly ISettingsService settingsService;

    public SystemSlimmingService(ISettingsService settingsService, string? windowsDirectory = null)
    {
        this.settingsService = settingsService;
        WindowsDirectory = windowsDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    }

    public bool IsEnabled =>
        settingsService.Current.EnableExperimentalFeatures && settingsService.Current.EnableSystemSlimming;

    /// <summary>组件存储与 Windows.old 都在这个目录下，测量要按 Windows 目录定位而不是硬编码 C 盘。</summary>
    internal string WindowsDirectory { get; }

    public async Task<Result<SystemSlimmingReport>> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var log = new List<string>(64);
        var targets = new List<SlimmingTarget>(8);

        var reclaimable = await AnalyzeComponentStoreAsync(targets, log, cancellationToken).ConfigureAwait(false);

        AddWindowsOldTarget(targets, log);
        AddDeliveryOptimizationTarget(targets, log);

        var manual = targets.Count(target => !target.Actionable);
        var summary = reclaimable > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"组件存储可回收约 {FormatBytes(reclaimable)}；另有 {manual} 项需单独处理。")
            : $"组件存储暂无明显可回收项；另有 {manual} 项需单独处理。";

        return new Result<SystemSlimmingReport>(ErrorType.None, string.Empty, new SystemSlimmingReport(
            targets,
            log,
            Environment.IsPrivilegedProcess,
            summary));
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "未知";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {units[unit]}");
    }
}
