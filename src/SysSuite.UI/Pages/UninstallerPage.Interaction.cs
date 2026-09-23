using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.UI.Pages;

public partial class UninstallerPage
{
    private void OnMonitorClick(object sender, RoutedEventArgs args)
    {
        if (changeMonitor.IsRunning)
        {
            changeMonitor.StopMonitoring();
        }
        else
        {
            changeMonitor.Start();
        }

        UpdateMonitorState();
    }

    private void OnAppsListSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        UpdateCommandStates();
    }

    private void OnAppsListDoubleClick(object sender, MouseButtonEventArgs args)
    {
        OnOpenInstallFolderClick(sender, args);
    }

    private void UpdateCommandStates()
    {
        // 顶部已移除「卸载」按钮（右键菜单保留），这里只维护「强制删除」的可用性。
        ForceButton.IsEnabled = GetSelectedApp() is not null && !isBusy;
    }

    private AppRecord? GetSelectedApp()
    {
        return AppsList.SelectedItem as AppRow is { } row ? row.App : null;
    }
}
