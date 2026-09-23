using System.Windows;
using SysSuite.Core.Abstractions.System;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// <see cref="IUpdateControlInteractions"/> 的 WPF 实现：确认框与提示都关在这一层。
///
/// Service 层（L2）的确认必须把"安全补丁不再安装"这句话写在正文里，而不只是换个标题 ——
/// 这是本功能唯一会长期损害用户利益的路径，靠一句"确定吗"是拦不住的。
/// </summary>
internal sealed class UpdateControlInteractions : IUpdateControlInteractions
{
    public bool ConfirmPolicyChange(UpdateMode mode)
        => MessageBox.Show(
            $"将把 Windows 更新切换为「{UpdateControlViewModel.ModeDisplayName(mode)}」。\n\n" +
            "这是注册表级设置，可随时用[恢复默认]还原。继续？",
            "Windows 更新设置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool ConfirmServiceLayerChange(UpdateMode mode)
        => MessageBox.Show(
            $"即将同时修改**服务与计划任务层**（目标：{UpdateControlViewModel.ModeDisplayName(mode)}）。\n\n" +
            "这会停止 wuauserv / UsoSvc 及相关计划任务：\n" +
            "  · 长期停更意味着安全补丁不再自动安装；\n" +
            "  · Windows 会定期把这些设置改回来（届时本程序会提示你）。\n\n" +
            "操作前会自动保存快照，可随时还原。确定继续？",
            "高风险：修改更新服务",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public bool ConfirmRestore()
        => MessageBox.Show(
            "将按操作前的快照还原注册表键值、服务启动类型与计划任务状态。\n\n继续？",
            "恢复默认更新设置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Notify(string message)
        => MessageBox.Show(message, "Windows 更新设置已被系统改动", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void SetClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // 剪贴板被其他进程占用是常见竞态，静默失败即可，不必打断用户
        }
    }
}
