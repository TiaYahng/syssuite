using System.Windows;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// <see cref="ICleanerInteractions"/> 的 WPF 实现：把两个确认框关在这一层。
/// </summary>
internal sealed class CleanerInteractions : ICleanerInteractions
{
    public Task<bool> ConfirmCleanAsync(int itemCount, long totalBytes)
        => Task.FromResult(MessageBox.Show(
            $"确定清理 {itemCount} 项，可释放 {DiskCleanerFormat.Bytes(totalBytes)}？",
            "磁盘清理",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes);

    public Task<bool> ConfirmRestoreAsync()
        => Task.FromResult(MessageBox.Show(
            "撤销最近一次清理会恢复备份文件；如果目标文件已经存在，将跳过以避免覆盖。继续？",
            "撤销清理",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes);
}
