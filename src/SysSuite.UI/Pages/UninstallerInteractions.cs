using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SysSuite.Core.Abstractions.Data;
using SysSuite.UI.Controls;

namespace SysSuite.UI.Pages;

/// <summary>
/// <see cref="ViewModels.IUninstallerInteractions"/> 的 WPF 实现。
///
/// 所有弹窗与对话框都被关在这一层，ViewModel 因此保持纯净、可单测。
/// 每个成员都只做"问用户"或"执行副作用"，不含任何业务判断。
/// </summary>
internal sealed class UninstallerInteractions(UninstallerPage page) : ViewModels.IUninstallerInteractions
{
    public Task WarnAsync(string message)
    {
        MessageBox.Show(Owner, message, "卸载器", MessageBoxButton.OK, MessageBoxImage.Warning);
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmUninstallAsync(AppRecord app)
        => Task.FromResult(MessageBox.Show(
            Owner,
            $"开始卸载“{app.Name}”？",
            "确认卸载",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes);
    public Task<bool> ConfirmForceDeleteAsync(string installDir, string requiredText)
        => Task.FromResult(ConfirmationInputBox.Show(
            Owner!,
            "强制删除",
            "强制删除会先备份，再移除只读/占用文件；无法删除的文件将安排重启后处理。",
            requiredText));

    public Task ShowLeftoversAsync(IReadOnlyList<LeftoverItem> leftovers)
    {
        new LeftoverWindow(leftovers) { Owner = Owner }.ShowDialog();
        return Task.CompletedTask;
    }

    public Task<string?> PickExportPathAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML 报告|*.html",
            FileName = "SysSuite-卸载器报告.html"
        };
        return Task.FromResult(dialog.ShowDialog(Owner) == true ? dialog.FileName : null);
    }

    public void OpenPath(string path)
        => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    public void OpenUrl(string url)
        => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    public void SetClipboard(string text) => Clipboard.SetText(text);

    private Window? Owner => Window.GetWindow(page);
}
