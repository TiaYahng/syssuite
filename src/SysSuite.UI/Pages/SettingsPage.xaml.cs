using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SysSuite.Core.Abstractions;

namespace SysSuite.UI.Pages;

public partial class SettingsPage : UserControl
{
    private readonly ISettingsService settingsService;
    private bool initialized;

    public SettingsPage()
    {
        InitializeComponent();
        settingsService = ((App)Application.Current).Services.GetRequiredService<ISettingsService>();
        WatchdogAutoStartCheckBox.IsChecked = settingsService.Current.WatchdogAutoStart;
        initialized = true;
    }

    private void OnWatchdogAutoStartChanged(object sender, RoutedEventArgs args)
    {
        if (!initialized)
        {
            return;
        }

        var enabled = WatchdogAutoStartCheckBox.IsChecked == true;
        settingsService.Current.WatchdogAutoStart = enabled;
        settingsService.SaveDebounced();

        using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (runKey is null)
        {
            return;
        }

        if (enabled)
        {
            var watchdogPath = Path.Combine(AppContext.BaseDirectory, "SysSuite.Watchdog.exe");
            if (File.Exists(watchdogPath))
            {
                runKey.SetValue("SysSuite.Watchdog", watchdogPath);
            }
        }
        else
        {
            runKey.DeleteValue("SysSuite.Watchdog", false);
        }
    }
}
