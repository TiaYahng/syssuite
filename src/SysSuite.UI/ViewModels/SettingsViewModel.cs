using System.IO;
using Microsoft.Win32;
using SysSuite.Core.Abstractions;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 设置页面 ViewModel（D1：原先开关读写与注册表 Run 键写入都在 <c>SettingsPage.xaml.cs</c>）。
///
/// "初始化期间不落盘"这个守卫从 Page 移到了这里：三个 CheckBox 的初始赋值会触发
/// Checked/Unchecked 事件，若不挡住就会在读设置的同时回写一遍设置。
/// </summary>
public sealed class SettingsViewModel : PageViewModelBase
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WatchdogValueName = "SysSuite.Watchdog";

    private readonly ISettingsService settingsService;
    private bool initialized;

    public SettingsViewModel(ISettingsService settingsService)
    {
        this.settingsService = settingsService;
    }

    public bool WatchdogAutoStart => settingsService.Current.WatchdogAutoStart;

    public bool EnableExperimentalFeatures => settingsService.Current.EnableExperimentalFeatures;

    public bool EnableForceDelete => settingsService.Current.EnableForceDelete;

    /// <summary>开关初值已就绪，之后的变更才需要落盘。</summary>
    public void MarkInitialized() => initialized = true;

    public void SetWatchdogAutoStart(bool enabled)
    {
        if (!initialized)
        {
            return;
        }

        settingsService.Current.WatchdogAutoStart = enabled;
        settingsService.SaveDebounced();
        UpdateRunKey(enabled);
        OnPropertyChanged(nameof(WatchdogAutoStart));
    }

    public void SetExperimentalFeatures(bool enabled)
    {
        if (!initialized)
        {
            return;
        }

        settingsService.Current.EnableExperimentalFeatures = enabled;
        settingsService.SaveDebounced();
        OnPropertyChanged(nameof(EnableExperimentalFeatures));
    }

    public void SetForceDelete(bool enabled)
    {
        if (!initialized)
        {
            return;
        }

        settingsService.Current.EnableForceDelete = enabled;
        settingsService.SaveDebounced();
        OnPropertyChanged(nameof(EnableForceDelete));
    }

    private static void UpdateRunKey(bool enabled)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (runKey is null)
        {
            return;
        }

        if (enabled)
        {
            var watchdogPath = Path.Combine(AppContext.BaseDirectory, "SysSuite.Watchdog.exe");
            if (File.Exists(watchdogPath))
            {
                runKey.SetValue(WatchdogValueName, watchdogPath);
            }
        }
        else
        {
            runKey.DeleteValue(WatchdogValueName, false);
        }
    }
}
