using System.Globalization;
using System.IO;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// Windows 更新控制服务（T4.1/T4.2）。
///
/// 分层职责：
///   - <c>WindowsUpdateControlService.Policy.cs</c>  策略层（注册表键值，L1 风险）
///   - <c>WindowsUpdateControlService.Service.cs</c> 服务与计划任务层（L2 风险，默认关闭）
///   - <c>WindowsUpdateControlService.Snapshot.cs</c> 快照采集/落盘/还原
///
/// 一条贯穿全类的原则：**读不到 ≠ 已应用**。读取失败（键不存在、无权限）必须记成 null，
/// 参与比对时判为"不可读"而不是"冲突"，否则自检会长期误报"被系统回滚"。
/// </summary>
public sealed partial class WindowsUpdateControlService : IUpdateControlService
{
    /// <summary>策略层根路径。注意这是 Policies 分支，与 WindowsUpdate\AU 不是同一个键。</summary>
    private const string PolicyKeyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";

    /// <summary>同上的 64 位视图路径，用于兼容 32 位进程读取。</summary>
    private const string WindowsUpdateKeyPath = @"SOFTWARE\Microsoft\Windows\WindowsUpdate\AU";

    private readonly string snapshotPath;

    public WindowsUpdateControlService(string snapshotPath)
    {
        this.snapshotPath = snapshotPath;
    }

    public bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>测试用：不读真实注册表时替换探针。</summary>
    internal IUpdateRegistryProbe? RegistryProbeOverride { get; set; }

    /// <summary>测试用：不读真实服务时替换探针。</summary>
    internal IUpdateServiceProbe? ServiceProbeOverride { get; set; }

    public Task<Result<UpdateControlStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => ReadStatus().Success(), cancellationToken);

    public Task<Result<string>> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => CaptureSnapshot(), cancellationToken);

    public Task<Result<UpdateControlOperationResult>> SetModeAsync(
        UpdateMode mode,
        bool includeServiceLayer,
        CancellationToken cancellationToken = default)
        => Task.Run(() => ApplyMode(mode, includeServiceLayer), cancellationToken);

    public Task<Result<UpdateControlOperationResult>> RestoreAsync(CancellationToken cancellationToken = default)
        => Task.Run(Restore, cancellationToken);

    public Task<bool> HasSnapshotAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(snapshotPath));

    public Task<Result<IReadOnlyList<UpdateControlItem>>> VerifyAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Verify(), cancellationToken);

    /// <summary>
    /// 由逐项读数归纳出整体档位。
    ///
    /// 判定顺序有讲究：先看策略层，因为组策略优先级高于服务位 —— 域管机型上服务是 Automatic
    /// 但策略说停用，此时真实行为是"停用"。若只看服务会得出完全相反的结论。
    /// </summary>
    internal static UpdateMode DeriveMode(UpdateControlStatus status)
    {
        var policyItems = status.Items.Where(item => item.Id.StartsWith("policy.", StringComparison.Ordinal)).ToList();
        var serviceItems = status.Items.Where(item => !item.Id.StartsWith("policy.", StringComparison.Ordinal)).ToList();

        var policyMode = DeriveFromItems(policyItems);
        if (policyMode != UpdateMode.Unknown)
        {
            return policyMode;
        }

        return DeriveFromItems(serviceItems);
    }

    /// <summary>
    /// 由两个策略值归纳档位。**必须以 <see cref="WindowsUpdateControlService.Policy.ExpectedValues"/> 的逆映射为准**，
    /// 否则"应用完再读回来"会得到与刚设置的不同档位。
    ///
    /// 单看 <c>NoAutoUpdate</c> 是不够的：实测本机 <c>NoAutoUpdate</c> 缺失但 <c>AUOptions=2</c>，
    /// 此时 Windows 的实际行为是"通知但不自动安装"，只看 NoAutoUpdate 会误报成"自动更新"。
    /// </summary>
    private static UpdateMode DeriveFromItems(List<UpdateControlItem> items)
    {
        if (items.Count == 0)
        {
            return UpdateMode.Unknown;
        }

        var noAuto = Find(items, "NoAutoUpdate");
        var options = Find(items, "AUOptions");

        // 两个值都读不到时不猜：HKLM 策略键对普通用户可读，读不到通常是权限问题而非"缺省"
        if (noAuto is null && options is null)
        {
            return UpdateMode.Unknown;
        }

        var noAutoInstall = string.Equals(noAuto, "1", StringComparison.Ordinal);

        // AUOptions: 1=从不检查, 2=通知下载并通知安装, 3=自动下载并通知安装,
        //            4=自动下载并按计划安装, 5=交给本地管理员选
        // 分界线是"会不会不经询问就装上"：1 从不查、2/3 装之前会问，故归为仅通知。
        return options switch
        {
            "1" => UpdateMode.Disabled,
            "2" or "3" => UpdateMode.NotifyOnly,
            "4" or "5" => noAutoInstall ? UpdateMode.NotifyOnly : UpdateMode.Automatic,
            null => noAutoInstall ? UpdateMode.NotifyOnly : UpdateMode.Automatic,
            _ => UpdateMode.Unknown,
        };
    }

    private static string? Find(List<UpdateControlItem> items, string idSuffix)
        => items.FirstOrDefault(item => item.Id.EndsWith(idSuffix, StringComparison.Ordinal))?.Actual;
}
