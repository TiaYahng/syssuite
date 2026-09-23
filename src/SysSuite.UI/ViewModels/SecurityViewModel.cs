using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 安全中心页面 ViewModel。
///
/// M5（T5.1~T5.6）尚未开工，页面本身还是空壳；此处先把 ViewModel 收敛到
/// <see cref="PageViewModelBase"/> 契约上，符合 UI-SPEC §3 的强制要求，
/// 后续 M5 只需往这里填 <see cref="LoadStatusAsync"/> 的实现。
/// </summary>
public sealed class SecurityViewModel : PageViewModelBase
{
    public bool HasAntivirusProduct { get; private set; }

    public string AntivirusSummary { get; private set; } = "尚未探测";

    protected override async Task RefreshAsync()
    {
        await RunBusyAsync(
            () =>
            {
                // M5/T5.1 会接入 SecurityCenter2 WMI；当前仅给出契约占位，不做假数据。
                SetStatus("安全中心将在 M5 里程碑接入防护状态探测。");
                return Task.CompletedTask;
            },
            "正在探测防护状态...");
    }
}
