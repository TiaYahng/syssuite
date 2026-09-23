namespace SysSuite.UI.ViewModels;

/// <summary>
/// 工具箱页面 ViewModel。M7 尚未开工，先收敛到 <see cref="PageViewModelBase"/> 契约。
/// </summary>
public sealed class ToolboxViewModel : PageViewModelBase
{
    public IReadOnlyList<string> ToolNames { get; private set; } = [];

    protected override Task RefreshAsync()
    {
        SetStatus("工具箱将在 M7 里程碑提供常用系统小工具。");
        return Task.CompletedTask;
    }
}
