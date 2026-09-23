using System.Windows.Input;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 更新控制的命令与异步操作（与主文件拆开守住 300 行门禁）。
///
/// 一条贯穿的规则：**每次写入前都重新落一次快照**。快照不是"首次安装时存一次"，
/// 而是"每次改动前的状态" —— 用户可能连续切换三档，只有最后一次切换前的状态才是
/// 真正需要还原回去的那个。
/// </summary>
public sealed partial class UpdateControlViewModel
{
    public ICommand ApplyCommand { get; private set; } = default!;

    public ICommand RestoreCommand { get; private set; } = default!;

    /// <summary>把自检发现的漂移项重新应用回目标档位。</summary>
    public ICommand ReapplyCommand { get; private set; } = default!;

    public ICommand CopySnapshotPathCommand { get; private set; } = default!;

    private void InitializeCommands()
    {
        ApplyCommand = new RelayCommand(_ => _ = ApplyAsync(), _ => !IsBusy);
        RestoreCommand = new RelayCommand(_ => _ = RestoreAsync(), _ => !IsBusy && HasSnapshot);
        ReapplyCommand = new RelayCommand(_ => _ = ReapplyAsync(), _ => !IsBusy && HasDrift);
        CopySnapshotPathCommand = new RelayCommand(
            _ => interactions.SetClipboard(snapshotPath ?? string.Empty),
            _ => HasSnapshot);
    }

    /// <summary>页面加载时调用：读状态 + 读快照路径。</summary>
    protected override Task RefreshAsync() => LoadAsync();

    /// <summary>
    /// 供 Page 直接 await 的入口。命令对象本身是 fire-and-forget（<c>ICommand.Execute</c> 无返回值），
    /// 而 XAML 里用 Click 事件时页面需要"跑完再回填控件"，所以两条路径都保留：
    /// 命令给绑定用，这些方法给事件处理器用。
    /// </summary>
    internal Task ApplyFromUiAsync() => ApplyAsync();

    internal Task RestoreFromUiAsync() => RestoreAsync();

    internal Task ReapplyFromUiAsync() => ReapplyAsync();

    internal async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var statusResult = await updateControlService.GetStatusAsync().ConfigureAwait(true);
            if (!statusResult.IsSuccess || statusResult.Value is null)
            {
                SetError(statusResult.Message);
                return;
            }

            status = statusResult.Value;
            selectedMode = status.Mode is UpdateMode.Unknown ? UpdateMode.Automatic : status.Mode;
            NotifyStatusChanged();

            var has = await updateControlService.HasSnapshotAsync().ConfigureAwait(true);
            snapshotPath = has ? await ResolveSnapshotPathAsync().ConfigureAwait(true) : null;
            OnPropertyChanged(nameof(HasSnapshot));
            OnPropertyChanged(nameof(SnapshotPathText));

            SetStatus(RequiresElevation
                ? "已读取当前状态。修改更新设置需要管理员权限。"
                : $"当前：{CurrentModeText}");
        }, "正在读取更新设置…").ConfigureAwait(true);
    }

    private async Task ApplyAsync()
    {
        if (!interactions.ConfirmPolicyChange(selectedMode))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await updateControlService
                .SetModeAsync(selectedMode, isServiceLayerEnabled)
                .ConfigureAwait(true);

            if (result.Value is not null)
            {
                snapshotPath = result.Value.SnapshotPath;
            }

            if (!result.IsSuccess)
            {
                SetError(result.Message);
                return;
            }

            await LoadAsync().ConfigureAwait(true);
            SetStatus($"已应用：{SelectedModeText}（{ScopeText}）");
        }, "正在应用更新设置…").ConfigureAwait(true);
    }

    private async Task RestoreAsync()
    {
        if (!interactions.ConfirmRestore())
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await updateControlService.RestoreAsync().ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                SetError(result.Message);
                return;
            }

            await LoadAsync().ConfigureAwait(true);
            SetStatus("已按快照还原。");
        }, "正在还原更新设置…").ConfigureAwait(true);
    }

    /// <summary>
    /// 重新应用当前目标档位。用于自检发现漂移后的一键修复 ——
    /// 与 <see cref="ApplyAsync"/> 共用写入路径，区别只是不弹确认（用户已经看到告警并主动点了这）。
    /// </summary>
    private async Task ReapplyAsync()
    {
        await RunBusyAsync(async () =>
        {
            var target = status.Mode == UpdateMode.Unknown ? selectedMode : status.Mode;
            var result = await updateControlService
                .SetModeAsync(target, isServiceLayerEnabled)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                SetError(result.Message);
                return;
            }

            await LoadAsync().ConfigureAwait(true);
            SetStatus($"已重新应用：{CurrentModeText}");
        }, "正在重新应用更新设置…").ConfigureAwait(true);
    }

    /// <summary>后台自检。设置页 Timer 调用，不占用 IsBusy（否则用户点不动按钮）。</summary>
    internal async Task CheckDriftAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var result = await updateControlService.VerifyAsync().ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is null)
        {
            return;
        }

        Drifts = result.Value;
        if (HasDrift)
        {
            interactions.Notify(DriftSummaryText);
        }
    }

    /// <summary>
    /// 取回快照落盘路径。接口刻意不暴露路径（实现细节），这里通过"再采一次"拿返回值 ——
    /// 采集是幂等的，且能被 [复制路径] 立即用上。
    /// </summary>
    private async Task<string?> ResolveSnapshotPathAsync()
        => snapshotPath ?? (await updateControlService.CaptureSnapshotAsync().ConfigureAwait(true)).Value;

    private void NotifyStatusChanged()
    {
        OnPropertyChanged(nameof(CurrentMode));
        OnPropertyChanged(nameof(CurrentModeText));
        OnPropertyChanged(nameof(SelectedModeText));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(RequiresElevation));
        OnPropertyChanged(nameof(HasPendingChange));
        OnPropertyChanged(nameof(IsAutomaticSelected));
        OnPropertyChanged(nameof(IsNotifyOnlySelected));
        OnPropertyChanged(nameof(IsDisabledSelected));
    }
}
