using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;
using SysSuite.UI.Pages;

namespace SysSuite.UI.ViewModels;

/// <summary>
/// 磁盘清理页面 ViewModel（D1：原先逻辑散落在 <c>DiskCleanerView.xaml.cs</c> 与三个 partial 文件里）。
///
/// 状态说明：<see cref="selectedPaths"/> 是"勾选"的唯一真源，行对象的 IsChecked 只是视图镜像。
/// 组勾选/全选都改写这个集合，再统一 <see cref="UpdateActionState"/> 同步按钮文案与可用性 ——
/// 原先这套逻辑在多个 partial 文件里反复读写 selectedPaths，容易漏同步。
/// </summary>
public sealed partial class CleanerViewModel : PageViewModelBase
{
    private readonly IDiskInspectionService inspectionService;
    private readonly ICleanerInteractions interactions;

    private readonly List<DriveOptionRow> driveRows = [];
    private readonly List<DiskCleanerRow> itemRows = [];
    private readonly List<DiskCategoryRow> categories = [];
    private readonly List<DiskCleanerRow> visibleItems = [];
    private readonly HashSet<string> selectedPaths = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? cancellation;
    private DiskInspectionReport? report;

    public CleanerViewModel(IDiskInspectionService inspectionService, ICleanerInteractions interactions)
    {
        this.inspectionService = inspectionService;
        this.interactions = interactions;
    }

    /// <summary>扫描进度。Page 把它画到进度条与阶段文本上。</summary>
    public event EventHandler<DiskScanProgress>? ProgressChanged;

    /// <summary>
    /// 状态栏需要富文本着色（各分类计数用不同颜色），所以抛结构化事件而不是纯字符串，
    /// 由 Page 决定怎么上色。
    /// </summary>
    public event EventHandler<CleanerStatusEventArgs>? StatusChanged;

    /// <summary>列表内容变化（行/分类/可见项），Page 收到后刷新 ItemsSource。</summary>
    public event EventHandler? ListsChanged;

    public IReadOnlyList<DriveOptionRow> Drives => driveRows;

    public IReadOnlyList<DiskCategoryRow> Categories => categories;

    /// <summary>当前分类下可见的行。分类切换时重建。</summary>
    public IReadOnlyList<DiskCleanerRow> VisibleItems => visibleItems;

    public DiskCategoryRow? SelectedCategory { get; private set; }

    public string ScanPhaseText { get; private set; } = "准备扫描";

    public string ScanDetailText { get; private set; } = "等待开始";

    public double ScanPercent { get; private set; }

    public bool IsScanIndeterminate { get; private set; } = true;

    public string SelectAllButtonText => selectedPaths.Count > 0 ? "取消" : "全选";

    public bool CanClean => selectedPaths.Count > 0 && !IsBusy;

    public bool CanSelectAll => itemRows.Count > 0 && !IsBusy;

    public async Task ActivateAsync()
    {
        var result = await inspectionService.GetDrivesAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            SetStatus(result.Message);
            return;
        }

        driveRows.Clear();
        driveRows.AddRange(result.Value.Select(drive => new DriveOptionRow(drive)));
        OnPropertyChanged(nameof(Drives));
        ListsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ScanAsync()
    {
        var selectedRoots = driveRows
            .Where(row => row.IsChecked && row.CanSelect)
            .Select(row => row.Option.RootPath)
            .ToArray();
        if (selectedRoots.Length == 0)
        {
            SetStatus("请选择要检查的磁盘。");
            return;
        }

        cancellation = new CancellationTokenSource();
        var progress = new Progress<DiskScanProgress>(UpdateProgress);
        var token = cancellation.Token;

        await RunBusyAsync(
            async () =>
            {
                var result = await inspectionService.ScanAsync(selectedRoots, progress, token);
                if (!result.IsSuccess || result.Value is null)
                {
                    // 用户取消不是错误，进度条停下即可
                    if (token.IsCancellationRequested)
                    {
                        SetStatus("已取消扫描。");
                        return;
                    }

                    SetError(result.Message);
                    return;
                }

                report = result.Value;
                selectedPaths.Clear();
                BuildRows();
                BuildCategories();
                ShowReportStatus(report);
            },
            "正在扫描...");

        cancellation?.Dispose();
        cancellation = null;
    }

    public async Task CleanAsync()
    {
        if (report is null)
        {
            return;
        }

        var selectedItems = report.Items.Where(item => selectedPaths.Contains(item.Path)).ToArray();
        if (selectedItems.Length == 0)
        {
            return;
        }

        var totalBytes = selectedItems.Sum(item => item.SizeBytes);
        if (!await interactions.ConfirmCleanAsync(selectedItems.Length, totalBytes).ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var result = await inspectionService.CleanAsync(report, selectedItems);
                if (!result.IsSuccess || result.Value is null)
                {
                    SetError(result.Message);
                    return;
                }

                var cleanResult = result.Value;
                report = DiskInspectionService.RebuildReport(
                    report with { Items = report.Items.Where(item => !selectedPaths.Contains(item.Path)).ToArray() });
                selectedPaths.Clear();
                BuildRows();
                BuildCategories();
                SetStatus(
                    $"清理完成：成功 {cleanResult.DeletedCount}，失败 {cleanResult.FailedCount}，释放 {DiskCleanerFormat.Bytes(cleanResult.FreedBytes)}。"
                        + (cleanResult.Errors.Count > 0 ? $" 首个错误：{cleanResult.Errors[0]}" : string.Empty));
            },
            "正在清理选中内容...");
    }

    public async Task RestoreLatestAsync()
    {
        if (!await interactions.ConfirmRestoreAsync().ConfigureAwait(true))
        {
            return;
        }

        await RunBusyAsync(
            async () =>
            {
                var result = await inspectionService.RestoreLatestAsync();
                if (!result.IsSuccess || result.Value is null)
                {
                    SetError(result.Message);
                    return;
                }

                SetStatus(
                    $"撤销完成：恢复 {result.Value.DeletedCount} 项，失败 {result.Value.FailedCount} 项，恢复 {DiskCleanerFormat.Bytes(result.Value.FreedBytes)}。"
                        + (result.Value.Errors.Count > 0 ? $" 首个错误：{result.Value.Errors[0]}" : string.Empty));
            },
            "正在撤销最近一次清理...");
    }

    public void SelectCategory(DiskCategoryRow? category)
    {
        SelectedCategory = category;
        RefreshVisibleItems();
    }

    /// <summary>切换某一组（组头复选框）。</summary>
    public void ToggleGroup(DiskGroup group)
    {
        var groupRows = itemRows.Where(row => row.GroupKey.Key == group.Key && row.CanSelect).ToArray();
        var select = !groupRows.Any(row => selectedPaths.Contains(row.Path));
        foreach (var row in groupRows)
        {
            row.SetCheckedSilently(select);
        }

        if (select)
        {
            selectedPaths.UnionWith(groupRows.Select(row => row.Path));
        }
        else
        {
            foreach (var row in groupRows)
            {
                selectedPaths.Remove(row.Path);
            }
        }

        UpdateActionState();
    }

    /// <summary>全选/取消当前分类下的可见项。</summary>
    public void ToggleSelectAll()
    {
        var targets = FilterForSelectedCategory().Where(row => row.CanSelect).ToArray();
        var select = targets.Any(row => !selectedPaths.Contains(row.Path));
        foreach (var row in targets)
        {
            row.SetCheckedSilently(select);
        }

        if (select)
        {
            selectedPaths.UnionWith(targets.Select(row => row.Path));
        }
        else
        {
            selectedPaths.ExceptWith(targets.Select(row => row.Path));
        }

        UpdateActionState();
    }

    internal void OnRowCheckedChanged(DiskCleanerRow row)
    {
        if (row.IsChecked)
        {
            selectedPaths.Add(row.Path);
        }
        else
        {
            selectedPaths.Remove(row.Path);
        }

        UpdateActionState();
    }

    protected override void Cancel()
    {
        cancellation?.Cancel();
        UpdateActionState();
    }
}
