using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// 设置页面（D1 后）。读写逻辑在 <see cref="SettingsViewModel"/>，这里只做控件回填与事件转发。
/// </summary>
public partial class SettingsPage : UserControl, IDisposable
{
    private readonly SettingsViewModel viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        viewModel = new SettingsViewModel(
            ((App)Application.Current).Services.GetRequiredService<ISettingsService>());
        WatchdogAutoStartCheckBox.IsChecked = viewModel.WatchdogAutoStart;
        ExperimentalFeaturesCheckBox.IsChecked = viewModel.EnableExperimentalFeatures;
        ForceDeleteCheckBox.IsChecked = viewModel.EnableForceDelete;
        viewModel.MarkInitialized();
        Unloaded += (_, _) => Dispose();
    }

    public void Dispose()
    {
        viewModel.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnWatchdogAutoStartChanged(object sender, RoutedEventArgs args)
        => viewModel.SetWatchdogAutoStart(WatchdogAutoStartCheckBox.IsChecked == true);

    private void OnExperimentalChanged(object sender, RoutedEventArgs args)
        => viewModel.SetExperimentalFeatures(ExperimentalFeaturesCheckBox.IsChecked == true);

    private void OnForceDeleteChanged(object sender, RoutedEventArgs args)
        => viewModel.SetForceDelete(ForceDeleteCheckBox.IsChecked == true);
}
