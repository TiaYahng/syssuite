using System.IO;
using System.Windows;
using Microsoft.Win32;
using SysSuite.UI.ViewModels;

namespace SysSuite.UI.Pages;

/// <summary>
/// <see cref="ISystemSlimmingInteractions"/> 的 WPF 实现。
///
/// 两个确认框都刻意用了 Warning / 红字级别的措辞：组件存储清理与 compact 都是
/// **改系统状态且不可回退**的动作，普通 Yes/No 的分量不够。
/// 计划要求的"红字警告"在这里落到对话框图标与文案上（MessageBoxImage.Warning），
/// 页面里另有一条常驻的红色提示由 ViewModel 的 ResetBaseWarning 驱动。
/// </summary>
internal sealed class SystemSlimmingInteractions : ISystemSlimmingInteractions
{
    public Task<bool> ConfirmComponentCleanupAsync(bool resetBase)
    {
        var message = resetBase
            ? "即将执行：dism /Online /Cleanup-Image /StartComponentCleanup /ResetBase\n\n"
                + "【不可回退】/ResetBase 会移除所有旧版本组件备份，清理后已安装的更新将无法卸载。\n"
                + "过程可能持续数十分钟，期间请勿关机。建议先创建系统还原点。\n\n继续？"
            : "即将执行：dism /Online /Cleanup-Image /StartComponentCleanup\n\n"
                + "将移除旧版本组件的备份，之后这些更新将无法卸载。\n"
                + "过程可能持续数十分钟，期间请勿关机。建议先创建系统还原点。\n\n继续？";

        return Task.FromResult(MessageBox.Show(
            message,
            resetBase ? "清理组件存储（含 /ResetBase）" : "清理组件存储",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes);
    }

    public Task<string?> PickCompactDirectoryAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择要压缩的目录（仅限非系统保护路径）",
            Multiselect = false,
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }

    public Task<bool> ConfirmCompactAsync(string directory, bool executableOnly)
    {
        var scope = executableOnly ? "仅可执行文件（.exe/.dll）" : "目录内全部文件";
        var message = $"即将对以下目录执行 compact 压缩：\n\n{directory}\n\n"
            + $"范围：{scope}\n"
            + "压缩会改变文件属性（NTFS 压缩位），此操作不可批量回退，且可能影响这些文件的读取性能。\n\n继续？";

        return Task.FromResult(MessageBox.Show(
            message,
            "按文件压缩",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes);
    }

    public Task<string?> ExportLogAsync(string content)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出分析日志",
            Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt",
            FileName = $"slimming-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            DefaultExt = ".log",
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            File.WriteAllText(dialog.FileName, content);
            return Task.FromResult<string?>(dialog.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
