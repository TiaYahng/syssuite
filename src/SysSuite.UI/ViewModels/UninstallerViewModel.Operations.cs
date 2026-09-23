using System.IO;
using System.Text;
using SysSuite.Core.Abstractions.Data;
using SysSuite.Core.Data;

namespace SysSuite.UI.ViewModels;

public sealed partial class UninstallerViewModel
{
    /// <summary>卸载选中应用。Page 负责确认框，这里只跑动作。</summary>
    public async Task UninstallSelectedAsync()
    {
        if (SelectedApp is not { } app)
        {
            await interactions.WarnAsync("请先选择一个应用。").ConfigureAwait(true);
            return;
        }

        if (!await interactions.ConfirmUninstallAsync(app).ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                SetStatus($"正在卸载 {app.Name}...");
                var result = await uninstallService.UninstallAsync(app, UninstallMode.Quiet);
                if (!result.IsSuccess || result.Value is null)
                {
                    SetError(string.IsNullOrWhiteSpace(result.Message) ? "卸载失败。" : result.Message);
                    return;
                }

                SetStatus(result.Value.Outcome switch
                {
                    UninstallOutcome.Succeeded => $"卸载完成。备份：{result.Value.BackupRoot}",
                    UninstallOutcome.RebootRequired => $"卸载完成，需要重启系统。备份：{result.Value.BackupRoot}",
                    UninstallOutcome.TimedOut => result.Value.Message,
                    _ => result.Value.Message
                });

                if (result.Value.Outcome is UninstallOutcome.Succeeded or UninstallOutcome.RebootRequired)
                {
                    await interactions.ShowLeftoversAsync(result.Value.Leftovers ?? []).ConfigureAwait(true);
                }

                if (result.Value.Outcome is not UninstallOutcome.TimedOut)
                {
                    await RefreshCoreAsync(ignoreBusy: true).ConfigureAwait(true);
                }
            },
            "正在卸载...");
    }

    /// <summary>强制删除选中应用的安装目录（需在设置中开启两道开关）。</summary>
    public async Task ForceDeleteSelectedAsync()
    {
        if (!CanForceDelete)
        {
            await interactions.WarnAsync("强制删除默认关闭，需在设置中同时开启实验能力和强制删除。").ConfigureAwait(true);
            return;
        }

        if (SelectedApp?.InstallDir is not { } installDir)
        {
            await interactions.WarnAsync("请选择一个包含安装目录的应用。").ConfigureAwait(true);
            return;
        }

        var requiredText = ForceDeleteService.CreateConfirmation(installDir);
        if (!await interactions.ConfirmForceDeleteAsync(installDir, requiredText).ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                SetStatus("正在强制删除...");
                var result = await forceDeleteService.DeleteAsync(installDir, requiredText);
                if (!result.IsSuccess || result.Value is null)
                {
                    SetError(string.IsNullOrWhiteSpace(result.Message) ? "强制删除失败。" : result.Message);
                    return;
                }

                SetStatus($"强制删除完成：文件 {result.Value.DeletedFiles}，目录 {result.Value.DeletedDirectories}，重启处理 {result.Value.ScheduledForReboot}。");
                await RefreshCoreAsync(ignoreBusy: true).ConfigureAwait(true);
            },
            "正在强制删除...");
    }

    public async Task OpenInstallFolderAsync()
    {
        if (SelectedInstallDirectory is not { } installDir)
        {
            await interactions.WarnAsync("未找到安装目录。").ConfigureAwait(true);
            return;
        }

        interactions.OpenPath(installDir);
    }

    public async Task CopyRegistryPathAsync()
    {
        if (SelectedRegistryPath is not { } keyPath)
        {
            await interactions.WarnAsync("当前应用没有注册表路径。").ConfigureAwait(true);
            return;
        }

        interactions.SetClipboard(keyPath);
        SetStatus("注册表路径已复制。");
    }

    public async Task CopyDetailsAsync()
    {
        if (SelectedApp is null)
        {
            return;
        }

        interactions.SetClipboard(DescribeSelection());
        SetStatus("应用详情已复制。");
    }

    public async Task OpenOnlineSearchAsync()
    {
        if (SelectedApp is not { } app)
        {
            await interactions.WarnAsync("请先选择一个应用。").ConfigureAwait(true);
            return;
        }

        var parts = new[] { app.Name, app.Publisher, app.Version }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var query = string.Join(' ', parts);
        interactions.OpenUrl($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
        SetStatus("已在浏览器打开在线搜索。");
    }

    public async Task ExportHtmlReportAsync()
    {
        if (await interactions.PickExportPathAsync().ConfigureAwait(true) is not { } path)
        {
            return;
        }

        try
        {
            var html = BuildHtmlReport(rows);
            await File.WriteAllTextAsync(path, html, new UTF8Encoding(false)).ConfigureAwait(true);
            SetStatus($"报告已导出：{path}");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetError($"导出失败：{exception.Message}", exception);
        }
    }

    public void ToggleMonitoring()
    {
        if (changeMonitor.IsRunning)
        {
            changeMonitor.StopMonitoring();
        }
        else
        {
            changeMonitor.Start();
        }

        OnPropertyChanged(nameof(MonitorBadgeText));
        OnPropertyChanged(nameof(MonitorButtonText));
    }
}
