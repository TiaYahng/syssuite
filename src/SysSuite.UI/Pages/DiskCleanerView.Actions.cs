using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using SysSuite.Core.Abstractions.System;
using System.Windows.Controls;

namespace SysSuite.UI.Pages;

public partial class DiskCleanerView
{
    private void OnOpenPathClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: DiskCleanerRow row })
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", System.IO.Path.GetDirectoryName(row.Path)!);
        }
        catch (Win32Exception exception)
        {
            SetStatus(exception.Message);
        }
    }

    private void UpdateProgress(DiskScanProgress progress)
    {
        ScanProgress.IsIndeterminate = progress.Progress is null;
        ScanProgress.Value = (progress.Progress ?? 0d) * 100d;
        ProgressPhaseText.Text = progress.Phase;
        ProgressDetailText.Text = progress.EstimatedRemaining is { } remaining
            ? $"{progress.ProcessedFiles}/{progress.TotalFiles} · 剩余约 {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} 秒"
            : $"{progress.ProcessedFiles}/{progress.TotalFiles} · 正在估算";
    }

    private void SetBusy(bool isBusy, string? status = null)
    {
        ScanButton.IsEnabled = !isBusy;
        CancelScanButton.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        SelectAllButton.IsEnabled = !isBusy && itemRows.Count > 0;
        CleanButton.IsEnabled = !isBusy && selectedPaths.Count > 0;
        if (status is not null)
        {
            SetStatus(status);
        }
    }

    private void UpdateActionState()
    {
        SelectAllButton.IsEnabled = itemRows.Count > 0;
        CleanButton.IsEnabled = selectedPaths.Count > 0;
        SelectAllButton.Content = selectedPaths.Count > 0 ? "取消" : "全选";
    }
}
