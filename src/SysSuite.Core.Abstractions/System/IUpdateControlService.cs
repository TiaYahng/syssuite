namespace SysSuite.Core.Abstractions.System;

/// <summary>
/// Windows 更新控制服务（T4.1）。
///
/// 风险分级：Policy 层 L1（改注册表，可还原）；Service 层 L2（改服务启动类型与计划任务，
/// 默认关闭且设置页需二次确认）。实现必须先 <see cref="CaptureSnapshotAsync"/> 再改动，
/// 且任何失败都要保留快照，否则用户失去退路。
/// </summary>
public interface IUpdateControlService
{
    /// <summary>当前进程是否具有管理员权限；无权限时无法改动服务与策略键。</summary>
    bool IsElevated { get; }

    /// <summary>探测当前实际生效的档位与层级，并逐项读出期望值/实际值。</summary>
    Task<Result<UpdateControlStatus>> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 采集操作前快照并**落盘**，返回快照文件路径。
    /// 在没有任何改动之前调用，是"一键还原"的前提。
    /// </summary>
    Task<Result<string>> CaptureSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>切换到指定档位。<paramref name="includeServiceLayer"/> 为 false 时只动 Policy 层（L1）。</summary>
    Task<Result<UpdateControlOperationResult>> SetModeAsync(
        UpdateMode mode,
        bool includeServiceLayer,
        CancellationToken cancellationToken = default);

    /// <summary>按快照还原。快照文件不存在或损坏时返回失败，绝不"尽力猜一个默认值"回写。</summary>
    Task<Result<UpdateControlOperationResult>> RestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>是否存在可用的快照（决定 [恢复默认] 按钮是否可点）。</summary>
    Task<bool> HasSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 自检：实际值是否仍等于期望值。返回被回滚的项（空集合表示一切正常）。
    /// WaaSMedic 会在后台把服务位改回去，这个检查就是用来发现它的。
    /// </summary>
    Task<Result<IReadOnlyList<UpdateControlItem>>> VerifyAsync(CancellationToken cancellationToken = default);
}
