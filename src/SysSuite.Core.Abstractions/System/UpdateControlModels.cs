namespace SysSuite.Core.Abstractions.System;

/// <summary>
/// Windows 更新控制档位（T4.1）。
///
/// 三档是**策略语义**，不是"开关"：
///   - <see cref="Automatic"/>：系统默认行为，有更新就自动下载安装。
///   - <see cref="NotifyOnly"/>：只检查并通知，不自动下载安装。多数人想要的"我能拖一拖"。
///   - <see cref="Disabled"/>：连检查都停掉。**这是 L2 风险动作** —— 长期停更意味着
///     安全补丁不再进来，UI 必须把这句话说清楚，而不是只显示一个开关。
/// </summary>
public enum UpdateMode
{
    /// <summary>尚未探测到当前状态（首次加载或读取失败）。</summary>
    Unknown = 0,

    /// <summary>自动下载并安装（系统默认）。</summary>
    Automatic = 1,

    /// <summary>仅通知，不自动下载安装。</summary>
    NotifyOnly = 2,

    /// <summary>完全停用自动更新。</summary>
    Disabled = 3,
}

/// <summary>生效层级。用于区分"当前状态是被谁决定的"。</summary>
public enum UpdateModeScope
{
    /// <summary>未能判定。</summary>
    Unknown = 0,

    /// <summary>由组策略层（HKLM\...\Policies\...\WindowsUpdate\AU）决定。域管机型常见。</summary>
    Policy = 1,

    /// <summary>由服务/任务层（wuauserv / UsoSvc / DoSvc / 计划任务）决定。</summary>
    Service = 2,
}

/// <summary>快照条目类型。分组是为了还原时按类型走不同的写入路径。</summary>
public enum UpdateSnapshotKind
{
    /// <summary>注册表键值。<c>Target</c> 形如 <c>HKLM\...\AU|NoAutoUpdate</c>，<c>Value</c> 为字符串化数据。</summary>
    RegistryValue = 1,

    /// <summary>服务启动类型（wuauserv / UsoSvc / DoSvc）。<c>Value</c> 为启动类型名。</summary>
    ServiceStartType = 2,

    /// <summary>计划任务的启用状态。<c>Value</c> 为 "Enabled" / "Disabled"。</summary>
    ScheduledTaskState = 3,
}

/// <summary>
/// 当前更新控制的探测结果（T4.1）。
///
/// <see cref="Mode"/> 与 <see cref="Scope"/> 分开：同样的"已停用"，由组策略设定和由我们
/// 改服务位得到的，其还原方式与风险完全不同。UI 必须能区分展示。
/// </summary>
public sealed record UpdateControlStatus(
    UpdateMode Mode,
    UpdateModeScope Scope,
    bool IsElevated,
    IReadOnlyList<UpdateControlItem> Items)
{
    /// <summary>策略层与服务层是否出现分歧（例如策略说停用、服务仍自动）。</summary>
    public bool HasConflict => Items.Any(item => item.IsConflicting);

    /// <summary>是否需要管理员权限才能改动。</summary>
    public bool RequiresElevation => !IsElevated;

    public static UpdateControlStatus Unknown(bool isElevated)
        => new(UpdateMode.Unknown, UpdateModeScope.Unknown, isElevated, []);
}

/// <summary>
/// 一个受控项的当前读数。
///
/// <see cref="Expected"/> 是"按当前目标模式应当处于的值"，与 <see cref="Actual"/> 比对即可
/// 发现被 Windows 自己回滚的情况（WaaSMedic 会这么做）。这个比对是 T4.2 自检的基础，
/// 所以两者都必须记录，而不是只记最终值。
/// </summary>
public sealed record UpdateControlItem(
    string Id,
    string Description,
    string? Actual,
    string? Expected)
{
    /// <summary>读不到值（键不存在 / 权限不足）时，不应判定为"被回滚"。</summary>
    public bool IsReadable => Actual is not null;

    public bool IsApplied => IsReadable && string.Equals(Actual, Expected, StringComparison.OrdinalIgnoreCase);

    public bool IsConflicting => IsReadable && !IsApplied;
}

/// <summary>
/// 操作前的状态快照（T4.1 的"一键还原"依据）。
///
/// 刻意做成**纯数据 + JSON 可序列化**：还原是跨进程、跨重启的动作，快照必须能落盘。
/// 记录键值、服务启动类型与计划任务状态三类，因为三者中任何一类漏记都会导致还原不完整。
///
/// <see cref="OriginalMode"/> 与 <see cref="TargetMode"/> 是两件事，别合并：
///   - <see cref="OriginalMode"/>：改动**之前**机器处于哪一档，还原要用它；
///   - <see cref="TargetMode"/>：我们**应用了**哪一档，自检要用它。
/// 只有后者能回答"系统有没有把我们设的东西改回去"。
/// </summary>
public sealed record UpdateControlSnapshot(
    DateTimeOffset CapturedAtUtc,
    string WindowsBuild,
    UpdateMode OriginalMode,
    IReadOnlyList<UpdateSnapshotEntry> Entries,
    UpdateMode? TargetMode = null)
{
    public static UpdateControlSnapshot Empty
        => new(DateTimeOffset.MinValue, string.Empty, UpdateMode.Unknown, []);
}

/// <summary>快照中的一条记录。<see cref="Kind"/> 决定 <see cref="Value"/> 的语义。</summary>
public sealed record UpdateSnapshotEntry(UpdateSnapshotKind Kind, string Target, string? Value);

/// <summary>一次更新控制操作的结果；还原时需要知道快照落在哪里。</summary>
public sealed record UpdateControlOperationResult(
    UpdateMode Mode,
    UpdateModeScope Scope,
    string? SnapshotPath,
    IReadOnlyList<string> AppliedTargets);

/// <summary>
/// 兼容性矩阵条目（T4.2）：记录某一 Windows 版本上各层是否可用。
/// Home 版没有组策略编辑器，但仍能写注册表策略键，二者要分开记录。
/// </summary>
public sealed record UpdateCompatibilityNote(
    string BuildFamily,
    bool HasGroupPolicyEditor,
    bool PolicyRegistryHonored,
    bool ServiceLayerAvailable,
    string? Note);
