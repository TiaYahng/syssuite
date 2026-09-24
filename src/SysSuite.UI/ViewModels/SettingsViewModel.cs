using System.IO;
using Microsoft.Win32;
using SysSuite.Core.Abstractions;
using SysSuite.Core.System;

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
    private readonly bool systemRestoreAvailable;
    private bool initialized;

    public SettingsViewModel(ISettingsService settingsService)
    {
        this.settingsService = settingsService;

        // 构造函数里探一次即可：策略在进程生命周期内不会变，没必要每次访问都读注册表
        systemRestoreAvailable = !SystemRestoreService.IsDisabledByPolicy();
    }

    public bool WatchdogAutoStart => settingsService.Current.WatchdogAutoStart;

    public bool EnableExperimentalFeatures => settingsService.Current.EnableExperimentalFeatures;

    public bool EnableForceDelete => settingsService.Current.EnableForceDelete;

    public bool CreateRestorePointBeforeClean => settingsService.Current.CreateRestorePointBeforeClean;

    /// <summary>L2 实验性：系统瘦身（T3.5）。须与 <see cref="EnableExperimentalFeatures"/> 同时打开才生效。</summary>
    public bool EnableSystemSlimming => settingsService.Current.EnableSystemSlimming;

    /// <summary>
    /// 系统还原被组策略或 SKU 关闭时，开关没有意义 —— 页面据此把复选框置灰并给出说明，
    /// 而不是让用户点开一个永远不会生效的选项。
    /// </summary>
    public bool IsSystemRestoreAvailable => systemRestoreAvailable;

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

    public void SetCreateRestorePointBeforeClean(bool enabled)
    {
        if (!initialized)
        {
            return;
        }

        settingsService.Current.CreateRestorePointBeforeClean = enabled;
        settingsService.SaveDebounced();
        OnPropertyChanged(nameof(CreateRestorePointBeforeClean));
    }

    public void SetSystemSlimming(bool enabled)
    {
        if (!initialized)
        {
            return;
        }

        settingsService.Current.EnableSystemSlimming = enabled;
        settingsService.SaveDebounced();
        OnPropertyChanged(nameof(EnableSystemSlimming));
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
