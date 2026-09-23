using System.IO;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// Windows 更新控制 ViewModel（M4 T4.1/T4.2）。
///
/// 三档语义在 UI 上必须**可辨**，所以除了 <see cref="SelectedMode"/> 还暴露
/// <see cref="CurrentMode"/>（实际生效值）—— 两者不一致说明"你选的和系统当前的不是一回事"，
/// 这正是用户最容易困惑的点（例如域管机型上策略把档位钉住了，点选根本写不进去）。
///
/// 危险分级写进属性而不是藏进命令：<see cref="IsServiceLayerEnabled"/> 默认 false，
/// 且每次开启都要过 <see cref="IUpdateControlInteractions.ConfirmServiceLayerChange"/>。
/// </summary>
public sealed partial class UpdateControlViewModel : PageViewModelBase
{
    private readonly IUpdateControlService updateControlService;
    private readonly IUpdateControlInteractions interactions;

    private UpdateControlStatus status = UpdateControlStatus.Unknown(false);
    private UpdateMode selectedMode = UpdateMode.Automatic;
    private bool isServiceLayerEnabled;
    private string? snapshotPath;
    private IReadOnlyList<UpdateControlItem> drifts = [];

    public UpdateControlViewModel(
        IUpdateControlService updateControlService,
        IUpdateControlInteractions interactions)
    {
        this.updateControlService = updateControlService;
        this.interactions = interactions;
        InitializeCommands();
    }

    /// <summary>当前实际生效的档位（由注册表/服务实测得出）。</summary>
    public UpdateMode CurrentMode => status.Mode;

    /// <summary>用户期望切换到的档位。与 <see cref="CurrentMode"/> 分离，便于展示"待应用"差异。</summary>
    public UpdateMode SelectedMode
    {
        get => selectedMode;
        set
        {
            if (SetProperty(ref selectedMode, value))
            {
                OnPropertyChanged(nameof(IsAutomaticSelected));
                OnPropertyChanged(nameof(IsNotifyOnlySelected));
                OnPropertyChanged(nameof(IsDisabledSelected));
                OnPropertyChanged(nameof(HasPendingChange));
            }
        }
    }

    public bool IsAutomaticSelected => selectedMode == UpdateMode.Automatic;

    public bool IsNotifyOnlySelected => selectedMode == UpdateMode.NotifyOnly;

    public bool IsDisabledSelected => selectedMode == UpdateMode.Disabled;

    /// <summary>选择值与实际值不一致，说明还没应用（或用户改了主意）。</summary>
    public bool HasPendingChange => status.Mode != UpdateMode.Unknown && status.Mode != selectedMode;

    /// <summary>服务层开关。默认关闭；打开需要二次确认（L2 风险）。</summary>
    public bool IsServiceLayerEnabled
    {
        get => isServiceLayerEnabled;
        set
        {
            if (value && !isServiceLayerEnabled && !interactions.ConfirmServiceLayerChange(selectedMode))
            {
                return;
            }

            if (SetProperty(ref isServiceLayerEnabled, value))
            {
                OnPropertyChanged(nameof(ServiceLayerHint));
            }
        }
    }

    public string ServiceLayerHint => isServiceLayerEnabled
        ? "将同时停止 wuauserv / UsoSvc 相关计划任务。长期停更意味着安全补丁不再安装。"
        : "只改注册表策略（可随时一键还原）。";

    public bool IsElevated => updateControlService.IsElevated;

    public bool RequiresElevation => !IsElevated;

    /// <summary>当前档位由谁决定：策略层（域管常见）还是服务层。</summary>
    public string ScopeText => status.Scope switch
    {
        UpdateModeScope.Policy => "由组策略决定",
        UpdateModeScope.Service => "由服务/任务层决定",
        _ => "未能判定",
    };

    public string CurrentModeText => ModeDisplayName(status.Mode);

    public string SelectedModeText => ModeDisplayName(selectedMode);

    /// <summary>是否存在可还原的快照，决定 [恢复默认] 是否可用。</summary>
    public bool HasSnapshot => !string.IsNullOrEmpty(snapshotPath) && File.Exists(snapshotPath);

    public string SnapshotPathText => string.IsNullOrEmpty(snapshotPath) ? "尚无快照" : snapshotPath;

    /// <summary>自检发现被系统回滚的项（WaaSMedic 会改回去）。空表示正常。</summary>
    public IReadOnlyList<UpdateControlItem> Drifts
    {
        get => drifts;
        private set
        {
            if (SetProperty(ref drifts, value))
            {
                OnPropertyChanged(nameof(HasDrift));
                OnPropertyChanged(nameof(DriftSummaryText));
            }
        }
    }

    public bool HasDrift => drifts.Count > 0;

    public string DriftSummaryText => drifts.Count == 0
        ? "未检测到设置被系统改动。"
        : $"检测到 {drifts.Count} 项设置被系统改回（Windows 会定期修复更新配置）。";

    /// <summary>把枚举翻成用户能读的中文，不要在 XAML 里做这层转换。</summary>
    internal static string ModeDisplayName(UpdateMode mode) => mode switch
    {
        UpdateMode.Automatic => "自动更新（系统默认）",
        UpdateMode.NotifyOnly => "仅通知，不自动安装",
        UpdateMode.Disabled => "完全停用自动更新",
        _ => "未知",
    };
}
