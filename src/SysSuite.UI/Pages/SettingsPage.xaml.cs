using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// 设置页面（D1 后）。读写逻辑在 <see cref="SettingsViewModel"/> /
/// <see cref="UpdateControlViewModel"/>，这里只做控件回填与事件转发。
///
/// 更新控制的三档是 RadioButton，回填时必须先解绑再设 IsChecked —— 否则赋值本身
/// 会触发 Checked，把"读取到的档位"当成"用户的选择"再写回系统一次。
/// </summary>
public partial class SettingsPage : UserControl, IDisposable
{
    private readonly SettingsViewModel viewModel;
    private readonly UpdateControlViewModel updateViewModel;
    private bool suppressModeEvents;

    public SettingsPage()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;

        viewModel = new SettingsViewModel(services.GetRequiredService<ISettingsService>());
        updateViewModel = new UpdateControlViewModel(
            services.GetRequiredService<IUpdateControlService>(),
            new UpdateControlInteractions());

        WatchdogAutoStartCheckBox.IsChecked = viewModel.WatchdogAutoStart;
        ExperimentalFeaturesCheckBox.IsChecked = viewModel.EnableExperimentalFeatures;
        ForceDeleteCheckBox.IsChecked = viewModel.EnableForceDelete;
        RestorePointCheckBox.IsChecked = viewModel.CreateRestorePointBeforeClean;
        SystemSlimmingCheckBox.IsChecked = viewModel.EnableSystemSlimming;

        // 系统还原被策略关闭时，复选框置灰 + 给出原因，而不是让人点一个永不生效的开关
        RestorePointCheckBox.IsEnabled = viewModel.IsSystemRestoreAvailable;
        RestorePointUnavailableText.Visibility = viewModel.IsSystemRestoreAvailable
            ? Visibility.Collapsed
            : Visibility.Visible;

        viewModel.MarkInitialized();

        Loaded += OnLoaded;
        Unloaded += (_, _) => Dispose();
    }

    public void Dispose()
    {
        viewModel.Dispose();
        updateViewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        await updateViewModel.LoadAsync();
        SyncUpdateControls();
    }

    private void OnWatchdogAutoStartChanged(object sender, RoutedEventArgs args)
        => viewModel.SetWatchdogAutoStart(WatchdogAutoStartCheckBox.IsChecked == true);

    private void OnExperimentalChanged(object sender, RoutedEventArgs args)
        => viewModel.SetExperimentalFeatures(ExperimentalFeaturesCheckBox.IsChecked == true);

    private void OnForceDeleteChanged(object sender, RoutedEventArgs args)
        => viewModel.SetForceDelete(ForceDeleteCheckBox.IsChecked == true);

    private void OnRestorePointChanged(object sender, RoutedEventArgs args)
        => viewModel.SetCreateRestorePointBeforeClean(RestorePointCheckBox.IsChecked == true);

    private void OnSystemSlimmingChanged(object sender, RoutedEventArgs args)
        => viewModel.SetSystemSlimming(SystemSlimmingCheckBox.IsChecked == true);

    private void OnUpdateModeChecked(object sender, RoutedEventArgs args)
    {
        if (suppressModeEvents || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse<UpdateMode>(tag, out var mode))
        {
            updateViewModel.SelectedMode = mode;
        }
    }

    private void OnUpdateServiceLayerChanged(object sender, RoutedEventArgs args)
        => updateViewModel.IsServiceLayerEnabled = UpdateServiceLayerCheckBox.IsChecked == true;

    private async void OnUpdateApplyClick(object sender, RoutedEventArgs args)
    {
        await updateViewModel.ApplyFromUiAsync();
        SyncUpdateControls();
    }

    private async void OnUpdateRestoreClick(object sender, RoutedEventArgs args)
    {
        await updateViewModel.RestoreFromUiAsync();
        SyncUpdateControls();
    }

    private async void OnUpdateReapplyClick(object sender, RoutedEventArgs args)
    {
        await updateViewModel.ReapplyFromUiAsync();
        SyncUpdateControls();
    }

    private void OnUpdateCopySnapshotClick(object sender, RoutedEventArgs args)
        => updateViewModel.CopySnapshotPathCommand.Execute(null);

    private async void OnUpdateVerifyClick(object sender, RoutedEventArgs args)
    {
        await updateViewModel.CheckDriftAsync();
        SyncUpdateControls();
    }

    /// <summary>
    /// 把 ViewModel 状态推回控件。
    ///
    /// <c>suppressModeEvents</c> 只在这一小段里为 true：三档 RadioButton 的 IsChecked 赋值
    /// 会同步触发 Checked 事件，不加这道闸就会出现"刷新界面 → 触发写回 → 再刷新"的循环。
    /// </summary>
    private void SyncUpdateControls()
    {
        suppressModeEvents = true;
        try
        {
            UpdateModeAutomatic.IsChecked = updateViewModel.IsAutomaticSelected;
            UpdateModeNotifyOnly.IsChecked = updateViewModel.IsNotifyOnlySelected;
            UpdateModeDisabled.IsChecked = updateViewModel.IsDisabledSelected;
            UpdateServiceLayerCheckBox.IsChecked = updateViewModel.IsServiceLayerEnabled;
        }
        finally
        {
            suppressModeEvents = false;
        }

        UpdateScopeBadge.Text = updateViewModel.ScopeText;
        UpdateServiceLayerHint.Text = updateViewModel.ServiceLayerHint;
        UpdateCurrentStateText.Text = updateViewModel.RequiresElevation
            ? $"当前：{updateViewModel.CurrentModeText}（{updateViewModel.ScopeText}）\n修改需要管理员权限，请以管理员身份重启本程序。"
            : $"当前：{updateViewModel.CurrentModeText}（{updateViewModel.ScopeText}）";

        UpdateDriftText.Text = updateViewModel.DriftSummaryText;
        UpdateDriftBanner.Visibility = updateViewModel.HasDrift ? Visibility.Visible : Visibility.Collapsed;
        UpdateSnapshotText.Text = $"快照：{updateViewModel.SnapshotPathText}";
        UpdateStatusText.Text = updateViewModel.HasError
            ? $"{updateViewModel.ErrorText}（诊断 ID：{updateViewModel.DiagnosticId}）"
            : updateViewModel.BusyText;

        UpdateScopeBadge.Foreground = (System.Windows.Media.Brush)FindResource(
            updateViewModel.CurrentMode == UpdateMode.Automatic ? "App.SafeText"
            : updateViewModel.CurrentMode == UpdateMode.NotifyOnly ? "App.CautionText"
            : "App.RiskyText");
    }
}
