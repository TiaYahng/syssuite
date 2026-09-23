using SysSuite.Core.Abstractions.Data;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// ViewModel 需要的"交互副作用"抽象：弹窗、文件对话框、剪贴板、外链。
///
/// 这些能力天然依赖 WPF，若直接在 ViewModel 里调 <c>MessageBox</c> / <c>Process.Start</c>
/// 就又把 UI 依赖塞回了业务层（D1 要消除的正是这个）。抽成接口后由 Page 实现，
/// 单元测试可以给一个假实现来断言"确认被拒绝时不应该真的卸载"。
/// </summary>
public interface IUninstallerInteractions
{
    Task WarnAsync(string message);

    Task<bool> ConfirmUninstallAsync(AppRecord app);

    Task<bool> ConfirmForceDeleteAsync(string installDir, string requiredText);

    Task ShowLeftoversAsync(IReadOnlyList<LeftoverItem> leftovers);

    /// <summary>让用户选一个导出路径；取消返回 null。</summary>
    Task<string?> PickExportPathAsync();

    void OpenPath(string path);

    void OpenUrl(string url);

    void SetClipboard(string text);
}
