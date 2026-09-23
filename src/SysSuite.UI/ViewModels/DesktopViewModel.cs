namespace SysSuite.UI.ViewModels;

/// <summary>
/// 桌面整理页面 ViewModel。M6 尚未开工，先收敛到 <see cref="PageViewModelBase"/> 契约。
/// </summary>
public sealed class DesktopViewModel : PageViewModelBase
{
    public string Summary { get; private set; } = "尚未扫描";

    protected override Task RefreshAsync()
    {
        SetStatus("桌面整理将在 M6 里程碑提供布局快照与还原。");
        return Task.CompletedTask;
    }
}
