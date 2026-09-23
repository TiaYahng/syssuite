namespace SysSuite.UI.ViewModels;

/// <summary>
/// 软件管家页面 ViewModel。M6 尚未开工，先收敛到 <see cref="PageViewModelBase"/> 契约。
/// </summary>
public sealed class SoftwareHubViewModel : PageViewModelBase
{
    public string Summary { get; private set; } = "尚未检测";

    protected override Task RefreshAsync()
    {
        SetStatus("软件管家将在 M6 里程碑提供软件检测与批量安装。");
        return Task.CompletedTask;
    }
}
