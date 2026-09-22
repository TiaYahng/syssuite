using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using SysSuite.Core.Abstractions.System;
using SysSuite.Core.System;

namespace SysSuite.UI.Pages;

public partial class DiskCleanerView : UserControl, IDisposable
{
    private readonly List<DriveOptionRow> driveRows = [];
    private readonly List<DiskCleanerRow> itemRows = [];
    private readonly HashSet<string> selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDiskInspectionService inspectionService;
    private CancellationTokenSource? scanCancellation;
    private DiskInspectionReport? report;

    public DiskCleanerView()
    {
        InitializeComponent();
        inspectionService = ((App)Application.Current).Services.GetRequiredService<IDiskInspectionService>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        var result = await inspectionService.GetDrivesAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            SetStatus(result.Message);
            return;
        }

        driveRows.AddRange(result.Value.Select(drive => new DriveOptionRow(drive)));
        DriveList.ItemsSource = driveRows;
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => CancelScan();

    private void OnCancelScanClick(object sender, RoutedEventArgs args) => CancelScan();

    private async void OnScanClick(object sender, RoutedEventArgs args)
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

        scanCancellation = new CancellationTokenSource();
        SetBusy(true, "正在扫描...");
        try
        {
            var progress = new Progress<DiskScanProgress>(UpdateProgress);
            var result = await inspectionService.ScanAsync(selectedRoots, progress, scanCancellation.Token);
            if (!result.IsSuccess || result.Value is null)
            {
            SetStatus(result.Message);
                return;
            }

            report = result.Value;
            selectedPaths.Clear();
            BuildRows();
            BuildCategories();
            ShowReportStatus(report);
        }
        finally
        {
            scanCancellation?.Dispose();
            scanCancellation = null;
            SetBusy(false);
        }
    }

    private void BuildRows()
    {
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

    private void BuildCategories()
    {
        var categories = itemRows
            .GroupBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DiskCategoryRow(
                group.Key,
                group.Key,
                group.Count(),
                group.Sum(row => row.Item.SizeBytes)))
            .OrderBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        categories.Insert(0, new DiskCategoryRow(string.Empty, "全部", itemRows.Count, itemRows.Sum(row => row.Item.SizeBytes)));
        CategoryList.ItemsSource = categories;
        if (CategoryList.SelectedIndex == 0)
        {
            RefreshItems();
        }
        else
        {
            CategoryList.SelectedIndex = 0;
        }
    }

    private void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs args) => RefreshItems();

    private void RefreshItems()
    {
        var selectedCategory = CategoryList.SelectedItem as DiskCategoryRow;
        var visibleRows = selectedCategory is null || selectedCategory.Category.Length == 0
            ? itemRows
            : itemRows.Where(row => row.Category == selectedCategory.Category);
        ItemList.ItemsSource = visibleRows.ToList();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(DiskCleanerRow.IsChecked) || sender is not DiskCleanerRow row)
        {
            return;
        }

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

    private async void OnRestoreClick(object sender, RoutedEventArgs args)
    {
        var confirm = MessageBox.Show("撤销最近一次清理会恢复备份文件；如果目标文件已经存在，将跳过以避免覆盖。继续？", "撤销清理", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "正在撤销最近一次清理...");
        try
        {
            var result = await inspectionService.RestoreLatestAsync();
            if (!result.IsSuccess || result.Value is null)
            {
                SetStatus(result.Message);
                return;
            }

            SetStatus($"撤销完成：恢复 {result.Value.DeletedCount} 项，失败 {result.Value.FailedCount} 项，恢复 {DiskCleanerFormat.Bytes(result.Value.FreedBytes)}。"
                + (result.Value.Errors.Count > 0 ? $" 首个错误：{result.Value.Errors[0]}" : string.Empty));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnCleanClick(object sender, RoutedEventArgs args)
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

        var confirm = MessageBox.Show(
            $"确定清理 {selectedItems.Length} 项，可释放 {DiskCleanerFormat.Bytes(selectedItems.Sum(item => item.SizeBytes))}？",
            "磁盘清理",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "正在清理选中内容...");
        try
        {
            var result = await inspectionService.CleanAsync(report, selectedItems);
            if (!result.IsSuccess || result.Value is null)
            {
                SetStatus(result.Message);
                return;
            }

            var cleanResult = result.Value;
            report = DiskInspectionService.RebuildReport(
                report with { Items = report.Items.Where(item => !selectedPaths.Contains(item.Path)).ToArray() });
            selectedPaths.Clear();
            BuildRows();
            BuildCategories();
            SetStatus($"清理完成：成功 {cleanResult.DeletedCount}，失败 {cleanResult.FailedCount}，释放 {DiskCleanerFormat.Bytes(cleanResult.FreedBytes)}。"
                + (cleanResult.Errors.Count > 0 ? $" 首个错误：{cleanResult.Errors[0]}" : string.Empty));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CancelScan()
    {
        scanCancellation?.Cancel();
        SetBusy(false);
    }

    public void Dispose()
    {
        CancelScan();
        GC.SuppressFinalize(this);
    }

}
