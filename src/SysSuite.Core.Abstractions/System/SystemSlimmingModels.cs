namespace SysSuite.Core.Abstractions.System;

public enum SlimmingOutcome
{
    Success,

    /// <summary>需要管理员权限（DISM 在非提权下直接返回 740）。</summary>
    RequiresElevation,

    /// <summary>实验性开关未打开。</summary>
    Disabled,

    /// <summary>目标不满足执行条件（例如 compact 指向了系统保护路径）。</summary>
    Rejected,

    Failed,
}

/// <summary>
/// 瘦身目标的一项现状（T3.5）。
/// </summary>
/// <param name="Actionable">
/// 是否可由瘦身服务直接处理。为 false 时 <see cref="Detail"/> 说明该走哪条路径 ——
/// 例如 Windows.old 需要先取得所有权，复用已有的强制删除流程而不是在这里重写一份。
/// </param>
public sealed record SlimmingTarget(
    string Id,
    string Title,
    long SizeBytes,
    string Detail,
    bool Actionable,
    string? Path)
{
    /// <summary>体积未知（没权限测量）与体积为零是两回事，展示上要分开。</summary>
    public bool IsSizeKnown => SizeBytes >= 0;
}

public sealed record SystemSlimmingReport(
    IReadOnlyList<SlimmingTarget> Targets,
    IReadOnlyList<string> ComponentStoreLog,
    bool IsElevated,
    string Summary)
{
    /// <summary>已知体积之和，仅用于展示估算，不等于实际能释放的字节数。</summary>
    public long TotalKnownBytes => Targets.Where(item => item.IsSizeKnown).Sum(item => Math.Max(0, item.SizeBytes));
}

/// <param name="ResetBase">
/// DISM 的 <c>/ResetBase</c>：清理后**已安装的更新无法卸载**。风险等级最高，默认关闭。
/// </param>
public sealed record SystemSlimmingOptions(
    bool ResetBase = false,
    bool CompactExecutables = false,
    string? CompactTargetDirectory = null);

public sealed record SystemSlimmingResult(
    SlimmingOutcome Outcome,
    string Message,
    long FreedBytes,
    IReadOnlyList<string> Log)
{
    public bool IsSuccess => Outcome == SlimmingOutcome.Success;
}
