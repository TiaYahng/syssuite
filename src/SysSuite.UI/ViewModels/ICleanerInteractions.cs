namespace SysSuite.UI.ViewModels;

/// <summary>
/// 磁盘清理页面的交互副作用。
/// 与本仓库既有的 <see cref="IUninstallerInteractions"/> 同样思路：
/// ViewModel 不引 WPF，弹窗都交给 Page 实现，测试可给假实现。
/// </summary>
public interface ICleanerInteractions
{
    Task<bool> ConfirmCleanAsync(int itemCount, long totalBytes);

    Task<bool> ConfirmRestoreAsync();
}
