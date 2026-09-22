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
        var hasSelection = GetSelectedApp() is not null;
        UninstallButton.IsEnabled = hasSelection;
        ForceButton.IsEnabled = hasSelection;
    }

    private AppRecord? GetSelectedApp()
    {
        return AppsList.SelectedItem as AppRow is { } row ? row.App : null;
    }
}
