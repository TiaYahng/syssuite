using SysSuite.Core.Abstractions.System;
using SysSuite.UI.Pages;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 行、分类与可见项的重建，以及进度/状态回报。
/// 从 <c>CleanerViewModel.cs</c> 拆出以满足单文件 ≤ 300 行门禁。
///
/// 不变式：<c>itemRows</c> 始终是报告全量，<c>visibleItems</c> 只是当前分类的投影；
/// 任何重建都必须重挂 <c>PropertyChanged</c>，否则勾选状态会与实际不一致。
/// </summary>
public sealed partial class CleanerViewModel
{

    private void BuildRows()
    {
        foreach (var row in itemRows)
        {
            row.PropertyChanged -= OnItemPropertyChanged;
        }

        itemRows.Clear();
        if (report is not null)
        {
            itemRows.AddRange(report.Items.Select(item => new DiskCleanerRow(item)));
        }

        foreach (var row in itemRows)
        {
            row.PropertyChanged += OnItemPropertyChanged;
        }

        UpdateActionState();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DiskCleanerRow.IsChecked) && sender is DiskCleanerRow row)
        {
            OnRowCheckedChanged(row);
        }
    }

    private void BuildCategories()
    {
        categories.Clear();
        categories.AddRange(itemRows
            .GroupBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DiskCategoryRow(
                group.Key,
                group.Key,
                group.Count(),
                group.Sum(row => row.Item.SizeBytes)))
            .OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase));
        categories.Insert(0, new DiskCategoryRow(string.Empty, "全部", itemRows.Count, itemRows.Sum(row => row.Item.SizeBytes)));

        // 分类重建后选中项作废，回落到「全部」，否则可见项会与摘要不一致
        SelectedCategory = categories[0];
        OnPropertyChanged(nameof(Categories));
        RefreshVisibleItems();
    }

    private void RefreshVisibleItems()
    {
        visibleItems.Clear();
        visibleItems.AddRange(FilterForSelectedCategory());
        OnPropertyChanged(nameof(VisibleItems));
        ListsChanged?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<DiskCleanerRow> FilterForSelectedCategory()
        => SelectedCategory is null || SelectedCategory.Category.Length == 0
            ? itemRows
            : itemRows.Where(row => row.Category == SelectedCategory.Category);

    private void UpdateProgress(DiskScanProgress progress)
    {
        ScanPercent = (progress.Progress ?? 0d) * 100d;
        IsScanIndeterminate = progress.Progress is null;
        ScanPhaseText = progress.Phase;
        ScanDetailText = progress.EstimatedRemaining is { } remaining
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{progress.ProcessedFiles}/{progress.TotalFiles} · 剩余约 {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} 秒")
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{progress.ProcessedFiles}/{progress.TotalFiles} · 正在估算");
        OnPropertyChanged(nameof(ScanPercent));
        OnPropertyChanged(nameof(IsScanIndeterminate));
        OnPropertyChanged(nameof(ScanPhaseText));
        OnPropertyChanged(nameof(ScanDetailText));
        ProgressChanged?.Invoke(this, progress);
    }

    private void ShowReportStatus(DiskInspectionReport current)
    {
        StatusChanged?.Invoke(this, new CleanerStatusEventArgs([
            ("检查完成：", "App.SubtleText"),
            ($"临时文件 {current.Items.Count(item => item.Category == "临时文件")}", "App.SafeText"),
            ("，", "App.SubtleText"),
            ($"重复文件 {current.Items.Count(item => item.Category == "重复文件")}", "App.CautionText"),
            ("，", "App.SubtleText"),
            ($"空文件夹 {current.Items.Count(item => item.Category == "空文件夹")}", "App.RiskyText"),
            ("，", "App.SubtleText"),
            ($"可释放 {DiskCleanerFormat.Bytes(current.Items.Sum(item => item.SizeBytes))}。", "App.Accent")
        ]));
    }

    private void UpdateActionState()
    {
        OnPropertyChanged(nameof(SelectAllButtonText));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(CanSelectAll));
    }
}

/// <summary>状态栏分段文本：一段文字配一个主题资源键，由 Page 负责解析成画刷。</summary>
public sealed class CleanerStatusEventArgs(IReadOnlyList<(string Text, string ResourceKey)> segments) : EventArgs
{
    public IReadOnlyList<(string Text, string ResourceKey)> Segments { get; } = segments;
}
