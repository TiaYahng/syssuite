using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// 磁盘清理页的「系统瘦身」区块（M3 T3.5）。
/// 与主文件拆开只为守住 300 行门禁 —— 拆的是文件不是职责，两者都是同一个视图层。
/// </summary>
public partial class DiskCleanerView
{
    private SystemSlimmingViewModel? slimmingViewModel;

    private void InitializeSlimming(IServiceProvider services)
    {
        var service = services.GetService<ISystemSlimmingService>();
        if (service is null)
        {
            // 没注册就不显示区块，而不是留一个点了没反应的按钮
            SlimmingExpander.Visibility = Visibility.Collapsed;
            return;
        }

        slimmingViewModel = new SystemSlimmingViewModel(service, new SystemSlimmingInteractions());
        slimmingViewModel.PropertyChanged += OnSlimmingPropertyChanged;
        SyncSlimming();

        // 开关未打开时整块隐藏：实验性能力不该靠"按钮点了没反应"来传达
        SlimmingExpander.Visibility = slimmingViewModel.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DisposeSlimming()
    {
        if (slimmingViewModel is null)
        {
            return;
        }

        slimmingViewModel.PropertyChanged -= OnSlimmingPropertyChanged;
        slimmingViewModel.Dispose();
        slimmingViewModel = null;
    }

    private void OnSlimmingPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (slimmingViewModel is null)
        {
            return;
        }

        switch (args.PropertyName)
        {
            case nameof(SystemSlimmingViewModel.ReportText):
                SlimmingReportText.Text = slimmingViewModel.ReportText;
                break;
            case nameof(SystemSlimmingViewModel.LogText):
                SlimmingLogBox.Text = slimmingViewModel.LogText;
                break;
            case nameof(SystemSlimmingViewModel.ResetBaseWarning):
                SlimmingResetBaseWarningText.Text = slimmingViewModel.ResetBaseWarning;
                SlimmingResetBaseWarningText.Visibility = string.IsNullOrEmpty(slimmingViewModel.ResetBaseWarning)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                break;
        }

        SyncSlimming();
    }

    /// <summary>把可用性与忙碌状态推回控件，并同步业务状态栏。</summary>
    private void SyncSlimming()
    {
        if (slimmingViewModel is null)
        {
            return;
        }

        var enabled = slimmingViewModel.IsAvailable && !slimmingViewModel.IsBusy;
        SlimmingAnalyzeButton.IsEnabled = enabled;
        SlimmingCleanupButton.IsEnabled = enabled;
        SlimmingCompactButton.IsEnabled = enabled;
        SlimmingResetBaseCheckBox.IsEnabled = enabled;
        SlimmingExecutableOnlyCheckBox.IsEnabled = enabled;
        SlimmingExportLogButton.IsEnabled = !slimmingViewModel.IsBusy;

        SlimmingAvailabilityText.Text = slimmingViewModel.AvailabilityText;
        SlimmingAvailabilityText.Visibility = slimmingViewModel.IsAvailable
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (slimmingViewModel.IsBusy)
        {
            SetStatusText(slimmingViewModel.BusyText);
        }
        else if (slimmingViewModel.HasError)
        {
            SetStatusText($"{slimmingViewModel.ErrorText}（诊断 ID：{slimmingViewModel.DiagnosticId}）");
        }
    }

    private async void OnSlimmingAnalyzeClick(object sender, RoutedEventArgs args)
    {
        if (slimmingViewModel is not null)
        {
            await slimmingViewModel.AnalyzeAsync();
        }
    }

    private async void OnSlimmingCleanupClick(object sender, RoutedEventArgs args)
    {
        if (slimmingViewModel is not null)
        {
            await slimmingViewModel.CleanupAsync();
        }
    }

    private async void OnSlimmingCompactClick(object sender, RoutedEventArgs args)
    {
        if (slimmingViewModel is not null)
        {
            await slimmingViewModel.CompactAsync();
        }
    }

    private async void OnSlimmingExportLogClick(object sender, RoutedEventArgs args)
    {
        if (slimmingViewModel is not null)
        {
            await slimmingViewModel.ExportLogAsync();
        }
    }

    private void OnSlimmingResetBaseChanged(object sender, RoutedEventArgs args)
    {
        if (slimmingViewModel is not null)
        {
            slimmingViewModel.ResetBase = SlimmingResetBaseCheckBox.IsChecked == true;
        }
    }

    private void OnSlimmingExecutableOnlyChanged(object sender, RoutedEventArgs args)
    {
        if (slimmingViewModel is not null)
        {
            slimmingViewModel.ExecutableOnly = SlimmingExecutableOnlyCheckBox.IsChecked == true;
        }
    }
}
