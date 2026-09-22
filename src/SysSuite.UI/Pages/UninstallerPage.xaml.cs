using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Abstractions.Settings;
using SysSuite.Core.Data;
using SysSuite.UI.Controls;

namespace SysSuite.UI.Pages;

public partial class UninstallerPage : UserControl
{
    private readonly IUninstallEnumerationService enumerationService;
    private readonly IIconCacheService iconCacheService;
    private readonly IUninstallService uninstallService;
    private readonly ILeftoverScanner leftoverScanner;
    private readonly IForceDeleteService forceDeleteService;
    private readonly IAppChangeMonitor changeMonitor;
    private readonly ISettingsService settingsService;
    private bool isBusy;

    public UninstallerPage()
    {
        InitializeComponent();
        var services = ((App)Application.Current).Services;
        enumerationService = services.GetRequiredService<IUninstallEnumerationService>();
        iconCacheService = services.GetRequiredService<IIconCacheService>();
        uninstallService = services.GetRequiredService<IUninstallService>();
        leftoverScanner = services.GetRequiredService<ILeftoverScanner>();
        forceDeleteService = services.GetRequiredService<IForceDeleteService>();
        changeMonitor = services.GetRequiredService<IAppChangeMonitor>();
        settingsService = services.GetRequiredService<ISettingsService>();
        changeMonitor.AppListChanged += OnAppListChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        changeMonitor.Start();
        UpdateMonitorState();
        await RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        changeMonitor.StopMonitoring();
        UpdateMonitorState();
    }

    private void OnAppListChanged(object? sender, IReadOnlyList<AppRecord> apps)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            StatusText.Text = $"检测到应用列表变化，共 {apps.Count} 个应用。";
            await RefreshAsync(true);
        });
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs args)
    {
        await RefreshAsync();
    }

    private async void OnUninstallClick(object sender, RoutedEventArgs args)
    {
        if (GetSelectedApp() is not { } app)
        {
            ShowWarning("请先选择一个应用。");
            return;
        }

        var owner = Window.GetWindow(this);
        if (MessageBox.Show(owner, $"开始卸载“{app.Name}”？", "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOperationAsync(async () =>
        {
            StatusText.Text = $"正在卸载 {app.Name}...";
            var result = await uninstallService.UninstallAsync(app, UninstallMode.Quiet);
            if (!result.IsSuccess || result.Value is null)
            {
                StatusText.Text = string.IsNullOrWhiteSpace(result.Message) ? "卸载失败。" : result.Message;
                return;
            }

            StatusText.Text = result.Value.Outcome switch
            {
                UninstallOutcome.Succeeded => $"卸载完成。备份：{result.Value.BackupRoot}",
                UninstallOutcome.RebootRequired => $"卸载完成，需要重启系统。备份：{result.Value.BackupRoot}",
                UninstallOutcome.TimedOut => result.Value.Message,
                _ => result.Value.Message
            };

            if (result.Value.Outcome is UninstallOutcome.Succeeded or UninstallOutcome.RebootRequired)
            {
                new LeftoverWindow(result.Value.Leftovers ?? []) { Owner = Window.GetWindow(this) }.ShowDialog();
            }

            if (result.Value.Outcome is not UninstallOutcome.TimedOut)
            {
                await RefreshAsync(true);
            }
        });
    }

    private async void OnForceClick(object sender, RoutedEventArgs args)
    {
        if (!settingsService.Current.EnableExperimentalFeatures || !settingsService.Current.EnableForceDelete)
        {
            ShowWarning("强制删除默认关闭，需在设置中同时开启实验能力和强制删除。");
            return;
        }

        if (GetSelectedApp()?.InstallDir is not { } installDir)
        {
            ShowWarning("请选择一个包含安装目录的应用。");
            return;
        }

        var requiredText = ForceDeleteService.CreateConfirmation(installDir);
        if (!ConfirmationInputBox.Show(Window.GetWindow(this), "强制删除", "强制删除会先备份，再移除只读/占用文件；无法删除的文件将安排重启后处理。", requiredText))
        {
            return;
        }

        await RunOperationAsync(async () =>
        {
            StatusText.Text = "正在强制删除...";
            var result = await forceDeleteService.DeleteAsync(installDir, requiredText);
            if (!result.IsSuccess || result.Value is null)
            {
                StatusText.Text = string.IsNullOrWhiteSpace(result.Message) ? "强制删除失败。" : result.Message;
                return;
            }

            StatusText.Text = $"强制删除完成：文件 {result.Value.DeletedFiles}，目录 {result.Value.DeletedDirectories}，重启处理 {result.Value.ScheduledForReboot}。";
            await RefreshAsync(true);
        });
    }

    private void OnOpenInstallFolderClick(object sender, RoutedEventArgs args)
    {
        if (GetSelectedApp()?.InstallDir is not { } installDir || !Directory.Exists(installDir))
        {
            ShowWarning("未找到安装目录。");
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = installDir, UseShellExecute = true });
    }

    private void OnCopyRegistryPathClick(object sender, RoutedEventArgs args)
    {
        if (GetSelectedApp()?.KeyPath is not { } keyPath)
        {
            ShowWarning("当前应用没有注册表路径。");
            return;
        }

        Clipboard.SetText(keyPath);
        StatusText.Text = "注册表路径已复制。";
    }

    private void OnCopyDetailsClick(object sender, RoutedEventArgs args)
    {
        if (GetSelectedApp() is not { } app)
        {
            return;
        }

        Clipboard.SetText($"{app.Name}\n{app.Publisher ?? "—"}\n{app.Version ?? "—"}\n{app.InstallDir ?? "—"}\n{app.KeyPath ?? "—"}");
        StatusText.Text = "应用详情已复制。";
    }

    private async Task RefreshAsync(bool ignoreBusy = false)
    {
        if (isBusy && !ignoreBusy)
        {
            return;
        }

        isBusy = true;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "正在枚举应用...";
        try
        {
            var result = await enumerationService.RefreshAsync();
            if (!result.IsSuccess || result.Value is null)
            {
                var cachedResult = await enumerationService.ListAsync();
                if (cachedResult is not { IsSuccess: true, Value: { Count: > 0 } cachedApps })
                {
                    StatusText.Text = string.IsNullOrWhiteSpace(result.Message) ? "刷新失败。" : result.Message;
                    return;
                }

                result = new Result<IReadOnlyList<AppRecord>>(ErrorType.None, result.Message, cachedApps);
            }

            var apps = result.Value ?? [];
            var rows = apps.Select(app => new AppRow(app)).ToList();
            AppsList.ItemsSource = rows;
            ApplyView();
            UpdateSummary();
            await Task.WhenAll(rows.Select(async row =>
            {
                var iconResult = await iconCacheService.GetIconPathAsync(row.App);
                if (iconResult.IsSuccess && !string.IsNullOrWhiteSpace(iconResult.Value))
                {
                    row.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconResult.Value));
                }
            }));
            UpdateCommandStates();
            UpdateSummary();
            StatusText.Text = $"已加载 {rows.Count} 个应用";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            UpdateCommandStates();
            isBusy = false;
        }
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (isBusy)
        {
            ShowWarning("另一个操作正在执行。");
            return;
        }

        isBusy = true;
        OperationProgress.Visibility = Visibility.Visible;
        UninstallButton.IsEnabled = false;
        ForceButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        SearchBox.IsEnabled = false;
        try
        {
            await operation();
        }
        finally
        {
            OperationProgress.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            UpdateCommandStates();
            SearchBox.IsEnabled = true;
            isBusy = false;
        }
    }

    private void ShowWarning(string message)
    {
        MessageBox.Show(Window.GetWindow(this), message, "卸载器", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void UpdateMonitorState()
    {
        MonitorBadge.Text = changeMonitor.IsRunning ? "监控：开启" : "监控：关闭";
        MonitorButton.Content = changeMonitor.IsRunning ? "关闭监控" : "启动监控";
    }
}
